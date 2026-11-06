using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class AdminOrderManagementTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Admin_advances_only_the_three_forward_steps_and_paid_orders_cannot_be_cancelled()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, adminId) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var owner = client; using var admin = adminClient;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products.Take(1)));
        var other = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products.Skip(1)));
        await OrderManagementTestData.SeedStatusAsync(Database, order.Id, "Confirmed");
        clock.Advance(TimeSpan.FromMinutes(20)); // Payment deadline does not expire a confirmed order.
        object[] stockBefore;
        await using (var read = Database.CreateContext())
            stockBefore = (await read.InventoryBalances.OrderBy(row => row.ProductId).ToListAsync())
                .Select(row => (object)(row.ProductId, row.OnHand, row.Reserved, row.Revision)).ToArray();
        foreach (var target in new[] { "Shipped", "Completed", "PendingPayment", "Confirmed", "Cancelled", "Expired" })
        {
            using var invalid = await OrderManagementTestData.AdvanceAsync(admin, order.Id, target, 2);
            await AuthenticationTests.Problem(invalid, 409, "state_conflict");
        }
        var version = 2;
        foreach (var target in new[] { "Processing", "Shipped", "Completed" })
        {
            using var paidCancellation = await OrderManagementTestData.CancelAsync(owner, order.Id);
            await AuthenticationTests.Problem(paidCancellation, 409, "state_conflict");
            clock.Advance(TimeSpan.FromMinutes(1));
            using var advanced = await OrderManagementTestData.AdvanceAsync(admin, order.Id, target, version, reason: "  Fulfillment step  ");
            Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);
            var body = (await advanced.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
            Assert.Equal(target, body.Status); Assert.Equal(version + 1, body.Version);
            var fact = body.History.Single(row => row.Version == version + 1);
            Assert.Equal(adminId, fact.ActorUserId); Assert.Equal("Admin", fact.ActorKind);
            Assert.Equal("Fulfillment step", fact.Reason); Assert.Equal(clock.GetUtcNow(), fact.OccurredAt);
            using var stale = await OrderManagementTestData.AdvanceAsync(admin, order.Id, target, version);
            await AuthenticationTests.Problem(stale, 409, "version_conflict");
            using var repeated = await OrderManagementTestData.AdvanceAsync(admin, order.Id, target, version + 1);
            await AuthenticationTests.Problem(repeated, 409, "state_conflict");
            version++;
        }
        using var completedCancellation = await OrderManagementTestData.CancelAsync(owner, order.Id);
        await AuthenticationTests.Problem(completedCancellation, 409, "state_conflict");
        await using var final = Database.CreateContext();
        Assert.Equal(OrderStatus.Completed, (await final.Orders.SingleAsync(row => row.Id == order.Id)).Status);
        Assert.Equal(5, await final.OrderStatusHistory.CountAsync(row => row.OrderId == order.Id));
        Assert.Equal(4, await final.OutboxMessages.CountAsync(row => row.AggregateId == order.Id));
        Assert.Equal(new long[] { 1, 3, 4, 5 }, await final.OutboxMessages.Where(row => row.AggregateId == order.Id)
            .OrderBy(row => row.AggregateVersion).Select(row => row.AggregateVersion).ToArrayAsync());
        var unchanged = await final.Orders.SingleAsync(row => row.Id == other.Id);
        Assert.Equal(OrderStatus.PendingPayment, unchanged.Status); Assert.Equal(1, unchanged.Version);
        Assert.Equal(stockBefore, (await final.InventoryBalances.OrderBy(row => row.ProductId).ToListAsync())
            .Select(row => (object)(row.ProductId, row.OnHand, row.Reserved, row.Revision)).ToArray());
    }

    [Fact]
    public async Task Admin_transition_requires_role_csrf_valid_body_and_expected_version()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var customer = client; using var admin = adminClient; using var anonymous = factory.Browser();
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        var path = OrderManagementTestData.Admin(order.Id) + "/transitions";
        using var roleDenied = await OrderManagementTestData.AdvanceAsync(customer, order.Id, "Processing", 1);
        await AuthenticationTests.Problem(roleDenied, 403, "forbidden");
        using var noCookie = await BrowserRequests.SendAsync(anonymous, HttpMethod.Post, path, new { targetStatus = "Processing", expectedVersion = 1 }, null);
        await AuthenticationTests.Problem(noCookie, 401, "unauthenticated");
        using var noCsrf = await BrowserRequests.SendAsync(admin, HttpMethod.Post, path, new { targetStatus = "Processing", expectedVersion = 1 }, null);
        await AuthenticationTests.Problem(noCsrf, 400, "antiforgery_failed");
        foreach (var body in new object[]
        {
            new { targetStatus = "Processing" }, new { expectedVersion = 1 },
            new { targetStatus = "Processing", expectedVersion = 0 },
            new { targetStatus = "Processing", expectedVersion = "1" },
            new { targetStatus = "processing", expectedVersion = 1 },
            new { targetStatus = "1", expectedVersion = 1 },
            new { targetStatus = "Unknown", expectedVersion = 1 },
            new { targetStatus = "Processing", expectedVersion = 1, actorUserId = Guid.NewGuid() },
            new { targetStatus = "Processing", expectedVersion = 1, reason = new string('r', 501) }
        })
        {
            using var invalid = await BrowserRequests.SendAsync(admin, HttpMethod.Post, path, body, await BrowserRequests.TokenAsync(admin));
            await AuthenticationTests.Problem(invalid, 400, "validation_failed");
        }
        using var pending = await OrderManagementTestData.AdvanceAsync(admin, order.Id, "Processing", 1);
        await AuthenticationTests.Problem(pending, 409, "state_conflict");
        using var missing = await OrderManagementTestData.AdvanceAsync(admin, Guid.NewGuid(), "Processing", 1);
        await AuthenticationTests.Problem(missing, 404, "not_found");
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("status=Unknown")]
    [InlineData("status=1")]
    [InlineData("status=pendingpayment")]
    public async Task Pagination_and_status_filters_are_validated_for_both_roles(string query)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var customer = client; using var admin = adminClient;
        using var first = await customer.GetAsync("/api/orders?" + query);
        await AuthenticationTests.Problem(first, 400, "validation_failed");
        using var second = await admin.GetAsync("/api/admin/orders?" + query);
        await AuthenticationTests.Problem(second, 400, "validation_failed");
    }
}
