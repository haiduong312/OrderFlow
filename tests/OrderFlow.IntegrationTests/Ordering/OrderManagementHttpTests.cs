using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class OrderManagementHttpTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Lists_scope_before_count_and_page_and_admin_can_filter_all_orders()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (firstClient, owner) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (secondClient, other) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var first = firstClient; using var second = secondClient; using var admin = adminClient;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1, stock: 20);
        var own = new List<OrderResponse>();
        // Identical creation times exercise the unique ID tie breaker.
        for (var index = 0; index < 4; index++) own.Add(await OrderManagementTestData.CreateAsync(first, CheckoutTestData.Body(products)));
        var theirs = await OrderManagementTestData.CreateAsync(second, CheckoutTestData.Body(products));
        using var cancelled = await OrderManagementTestData.CancelAsync(first, own[0].Id);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var pageOne = (await first.GetFromJsonAsync<OrderPage>("/api/orders?pageSize=2&customerId=" + other))!;
        var pageTwo = (await first.GetFromJsonAsync<OrderPage>("/api/orders?pageSize=2&page=2"))!;
        Assert.Equal(4, pageOne.TotalCount); Assert.Equal(4, pageTwo.TotalCount);
        Assert.All(pageOne.Items.Concat(pageTwo.Items), row => Assert.Equal(owner, row.CustomerId));
        Assert.Equal(4, pageOne.Items.Concat(pageTwo.Items).Select(row => row.Id).Distinct().Count());
        await using (var read = Database.CreateContext())
        {
            var expected = await read.Orders.Where(row => row.UserId == owner).OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.Id).Select(row => row.Id).ToListAsync();
            Assert.Equal(expected, pageOne.Items.Concat(pageTwo.Items).Select(row => row.Id));
        }
        var filtered = (await first.GetFromJsonAsync<OrderPage>("/api/orders?status=Cancelled"))!;
        Assert.Equal(1, filtered.TotalCount); Assert.Equal(own[0].Id, Assert.Single(filtered.Items).Id);
        var all = (await admin.GetFromJsonAsync<OrderPage>("/api/admin/orders?pageSize=100"))!;
        Assert.Equal(5, all.TotalCount); Assert.Contains(all.Items, row => row.Id == theirs.Id);
        Assert.All(all.Items, row => Assert.False(row.Expiry.CanCancel));
        var customerFilter = (await admin.GetFromJsonAsync<OrderPage>("/api/admin/orders?customerId=" + other))!;
        Assert.Equal(theirs.Id, Assert.Single(customerFilter.Items).Id);
        var none = (await first.GetFromJsonAsync<OrderPage>("/api/orders?page=99"))!;
        Assert.Empty(none.Items); Assert.Equal(4, none.TotalCount);
    }

    [Fact]
    public async Task Nonowners_and_admin_only_accounts_cannot_cancel_through_customer_paths()
    {
        await using var factory = new AuthApiFactory(Database);
        var (ownerClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (otherClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        var (dualClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin", "Customer");
        using var owner = ownerClient; using var other = otherClient; using var admin = adminClient; using var dual = dualClient;
        using var anonymous = factory.Browser();
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        foreach (var id in new[] { order.Id, Guid.NewGuid() })
        {
            using var hidden = await other.GetAsync(OrderManagementTestData.Customer(id));
            await AuthenticationTests.Problem(hidden, 404, "not_found");
            using var denied = await OrderManagementTestData.CancelAsync(other, id);
            await AuthenticationTests.Problem(denied, 404, "not_found");
            using var dualDenied = await OrderManagementTestData.CancelAsync(dual, id);
            await AuthenticationTests.Problem(dualDenied, 404, "not_found");
            using var adminDenied = await OrderManagementTestData.CancelAsync(admin, id);
            await AuthenticationTests.Problem(adminDenied, 403, "forbidden");
        }
        using var noCookie = await OrderManagementTestData.CancelAsync(anonymous, order.Id, csrf: false);
        await AuthenticationTests.Problem(noCookie, 401, "unauthenticated");
        using var noCsrf = await OrderManagementTestData.CancelAsync(owner, order.Id, csrf: false);
        await AuthenticationTests.Problem(noCsrf, 400, "antiforgery_failed");
        using var adminDetail = await admin.GetAsync(OrderManagementTestData.Admin(order.Id));
        Assert.Equal(HttpStatusCode.OK, adminDetail.StatusCode);
        using var customerAdmin = await owner.GetAsync("/api/admin/orders");
        await AuthenticationTests.Problem(customerAdmin, 403, "forbidden");
        using var anonymousList = await anonymous.GetAsync("/api/orders");
        await AuthenticationTests.Problem(anonymousList, 401, "unauthenticated");
        using var tampered = await BrowserRequests.SendAsync(owner, HttpMethod.Post, OrderManagementTestData.Customer(order.Id) + "/cancel",
            new { status = "Cancelled", userId = Guid.NewGuid() }, await BrowserRequests.TokenAsync(owner));
        await AuthenticationTests.Problem(tampered, 400, "validation_failed");
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Fact]
    public async Task Cancellation_releases_once_and_leaves_other_orders_and_stock_unchanged()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, userId) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (otherClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var owner = client; using var other = otherClient;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 3);
        var target = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products.Take(2), 2), "cancel-original-response");
        var shared = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products.Take(1), 3));
        var unrelated = await OrderManagementTestData.CreateAsync(other, CheckoutTestData.Body(products.Skip(2)));
        long untouchedRevision;
        string[] otherEvents;
        await using (var read = Database.CreateContext())
        {
            untouchedRevision = (await read.InventoryBalances.SingleAsync(row => row.ProductId == products[2].Id)).Revision;
            otherEvents = await read.OutboxMessages.Where(row => row.AggregateId != target.Id).OrderBy(row => row.EventId).Select(row => row.Payload).ToArrayAsync();
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        using var response = await OrderManagementTestData.CancelAsync(owner, target.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
        Assert.Equal("Cancelled", cancelled.Status); Assert.Equal(2, cancelled.Version);
        Assert.False(cancelled.Expiry.CanCancel);
        var fact = cancelled.History.Single(row => row.Version == 2);
        Assert.Equal("PendingPayment", fact.FromStatus); Assert.Equal("Customer", fact.ActorKind);
        Assert.Equal(userId, fact.ActorUserId); Assert.Equal(clock.GetUtcNow(), fact.OccurredAt);
        clock.Advance(TimeSpan.FromHours(1));
        // Empty body is accepted, as well as {}. CSRF is still required.
        using var second = await BrowserRequests.SendAsync(owner, HttpMethod.Post, OrderManagementTestData.Customer(target.Id) + "/cancel",
            null, await BrowserRequests.TokenAsync(owner));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<OrderDetailResponse>())!.Version);
        using var replay = await CheckoutTestData.PostAsync(owner, CheckoutTestData.Body(products.Take(2), 2), "cancel-original-response");
        Assert.Equal("PendingPayment", (await CheckoutTestData.CreatedAsync(replay)).Status);
        await using var final = Database.CreateContext();
        Assert.Equal(3, await final.Orders.CountAsync());
        Assert.Equal(4, await final.OrderStatusHistory.CountAsync());
        Assert.Equal(4, await final.OutboxMessages.CountAsync());
        Assert.Equal(2, await final.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        foreach (var id in new[] { shared.Id, unrelated.Id })
        {
            var row = await final.Orders.SingleAsync(row => row.Id == id);
            Assert.Equal(OrderStatus.PendingPayment, row.Status); Assert.Equal(1, row.Version);
            Assert.All(await final.InventoryReservations.Where(item => item.OrderId == id).ToListAsync(), item => Assert.Equal(ReservationStatus.Active, item.Status));
        }
        var released = await final.InventoryReservations.Where(row => row.OrderId == target.Id).ToListAsync();
        Assert.All(released, row => { Assert.Equal(ReservationStatus.Released, row.Status); Assert.Equal(ReservationReleaseReason.Cancelled, row.ReleaseReason); });
        Assert.Equal(3, (await final.InventoryBalances.SingleAsync(row => row.ProductId == products[0].Id)).Reserved);
        Assert.Equal(0, (await final.InventoryBalances.SingleAsync(row => row.ProductId == products[1].Id)).Reserved);
        var untouched = await final.InventoryBalances.SingleAsync(row => row.ProductId == products[2].Id);
        Assert.Equal(1, untouched.Reserved); Assert.Equal(untouchedRevision, untouched.Revision);
        Assert.All(await final.InventoryBalances.ToListAsync(), row => Assert.Equal(10, row.OnHand));
        Assert.Equal(otherEvents, await final.OutboxMessages.Where(row => row.AggregateId != target.Id).OrderBy(row => row.EventId).Select(row => row.Payload).ToArrayAsync());
    }

    [Theory]
    [InlineData(-1, 200, "Cancelled")]
    [InlineData(0, 409, "Expired")]
    [InlineData(1, 409, "Expired")]
    public async Task Expiry_is_effective_in_reads_and_finalized_atomically_on_cancellation(int microseconds, int status, string finalStatus)
    {
        var start = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var owner = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(microseconds * 10));
        var readOnly = (await owner.GetFromJsonAsync<OrderDetailResponse>(OrderManagementTestData.Customer(order.Id)))!;
        var page = (await owner.GetFromJsonAsync<OrderPage>("/api/orders?status=PendingPayment"))!;
        Assert.Equal("PendingPayment", readOnly.Status);
        Assert.Equal(microseconds >= 0, readOnly.Expiry.IsExpired);
        Assert.Equal(microseconds < 0, readOnly.Expiry.CanCancel);
        Assert.Equal(readOnly.Expiry, Assert.Single(page.Items).Expiry);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1); // Reads did not mutate anything.
        using var response = await OrderManagementTestData.CancelAsync(owner, order.Id);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 409) await AuthenticationTests.Problem(response, 409, "order_expired");
        using var repeated = await OrderManagementTestData.CancelAsync(owner, order.Id);
        Assert.Equal(status, (int)repeated.StatusCode);
        await using var final = Database.CreateContext();
        Assert.Equal(finalStatus, (await final.Orders.SingleAsync()).Status.ToString());
        Assert.Equal(2, (await final.Orders.SingleAsync()).Version);
        var reservation = await final.InventoryReservations.SingleAsync();
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(finalStatus, reservation.ReleaseReason.ToString());
        Assert.Equal(clock.GetUtcNow(), reservation.ReleasedAt);
        Assert.Equal(0, (await final.InventoryBalances.SingleAsync()).Reserved);
        Assert.Equal(10, (await final.InventoryBalances.SingleAsync()).OnHand);
        Assert.Equal(2, await final.OrderStatusHistory.CountAsync()); Assert.Equal(2, await final.OutboxMessages.CountAsync());
    }
}
