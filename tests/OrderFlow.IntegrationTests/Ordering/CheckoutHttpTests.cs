using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CheckoutHttpTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Checkout_persists_authoritative_snapshots_reservations_history_response_and_outbox_atomically()
    {
        var start = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(19);
        var clock = new ManualTimeProvider(start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, userId) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        using var response = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products.Reverse(), 3), "happy");
        var order = await CheckoutTestData.CreatedAsync(response);
        Assert.Equal("PendingPayment", order.Status);
        Assert.Equal("75.00", order.Total);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(start.UtcTicks - start.UtcTicks % 10, order.CreatedAt.UtcTicks);
        Assert.Equal(order.CreatedAt.AddMinutes(15), order.ExpiresAt);
        Assert.Equal(products.Select(product => product.Id), order.Items.Select(item => item.ProductId));
        Assert.All(order.Items, item => { Assert.Equal("12.50", item.UnitPrice); Assert.Equal("37.50", item.LineTotal); Assert.Equal(3, item.Quantity); });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.EndsWith("Z", json.RootElement.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("total").ValueKind);
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("items")[0].GetProperty("quantity").ValueKind);
        Assert.Equal("/api/orders/" + order.Id, response.Headers.Location!.ToString());
        await CheckoutTestData.AssertCountsAsync(Database, 1, 2);
        await using var read = Database.CreateContext();
        var persisted = await read.Orders.Include(row => row.Items).SingleAsync();
        Assert.Equal(userId, persisted.UserId);
        Assert.Equal(75m, persisted.Total.Value);
        Assert.All(await read.InventoryReservations.ToListAsync(), reservation =>
        {
            Assert.Equal(order.Id, reservation.OrderId);
            Assert.Equal(3, reservation.Quantity);
            Assert.Equal(ReservationStatus.Active, reservation.Status);
            Assert.Equal(order.CreatedAt, reservation.CreatedAt);
            Assert.Equal(order.ExpiresAt, reservation.ExpiresAt);
        });
        Assert.All(await read.InventoryBalances.ToListAsync(), balance => { Assert.Equal(10, balance.OnHand); Assert.Equal(3, balance.Reserved); Assert.Equal(7, balance.Available); });
        var saved = await read.IdempotencyRecords.SingleAsync();
        Assert.Equal(IdempotencyStatus.Completed, saved.Status);
        Assert.Equal(await response.Content.ReadAsByteArrayAsync(), saved.ResponseBody);
        Assert.Equal(201, saved.ResponseStatusCode);
        Assert.Equal(persisted.CheckoutRequestId, saved.Id);
        var outbox = await read.OutboxMessages.SingleAsync();
        Assert.Equal("ordering.order-created", outbox.EventType);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.Equal(order.Id, outbox.AggregateId);
        Assert.Equal(1, outbox.AggregateVersion);
        Assert.Equal(saved.Id, outbox.CausationId);
        Assert.Equal(order.CreatedAt, outbox.OccurredAt);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(order.Id, payload.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal("75.00", payload.RootElement.GetProperty("total").GetString());
    }

    [Fact]
    public async Task Maximum_quantity_uses_exact_cents_and_reserves_the_available_boundary()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1, price: 0.01m, stock: 1000);
        using var response = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products, 1000), "maximum-quantity");
        var order = await CheckoutTestData.CreatedAsync(response);
        Assert.Equal("10.00", order.Total);
        Assert.Equal(1000, Assert.Single(order.Items).Quantity);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
        await using var read = Database.CreateContext();
        Assert.Equal(0, (await read.InventoryBalances.SingleAsync()).Available);
    }

    [Fact]
    public async Task Normalized_replay_returns_original_bytes_after_catalog_deadline_and_order_status_change()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        using var first = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products, 2), "original");
        var original = await CheckoutTestData.CreatedAsync(first);
        await using (var edit = Database.CreateContext())
        {
            var product = await edit.Products.SingleAsync(row => row.Id == products[0].Id);
            product.Update("Renamed after checkout", "", UsdMoney.FromDecimal(99.99m), product.CategoryId, false, product.Version);
            await edit.SaveChangesAsync();
            // Simulate a later lifecycle writer to verify read/replay separation only. No payment
            // or fulfillment endpoint/state transition is implemented by this test setup.
            await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE ordering.\"Orders\" SET \"Status\" = 'Confirmed', \"Version\" = 2 WHERE \"Id\" = {original.Id}");
        }
        clock.Advance(TimeSpan.FromMinutes(16));
        using var replay = await CheckoutTestData.PostAsync(customer, new
        {
            items = products.Reverse().Select(product => new { quantity = 2, productId = product.Id.ToString("D").ToUpperInvariant() })
        }, "original");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal(first.Content.Headers.ContentType, replay.Content.Headers.ContentType);
        var current = (await customer.GetFromJsonAsync<OrderResponse>(OriginalLocation(original)))!;
        Assert.Equal("Confirmed", current.Status);
        Assert.Equal(2, current.Version);
        Assert.Equal(original.Total, current.Total);
        Assert.Equal(original.Items.ToArray(), current.Items.ToArray());
        Assert.Equal(original.CreatedAt, current.CreatedAt);
        Assert.Equal(original.ExpiresAt, current.ExpiresAt);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 2);
    }

    [Fact]
    public async Task Changed_quantity_or_product_under_successful_key_conflicts_without_any_new_effect()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        using var created = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products.Take(1)), "conflict");
        await CheckoutTestData.CreatedAsync(created);
        foreach (var body in new[] { CheckoutTestData.Body(products.Take(1), 2), CheckoutTestData.Body(products.Skip(1)) })
        {
            using var response = await CheckoutTestData.PostAsync(customer, body, "conflict");
            await AuthenticationTests.Problem(response, 409, "idempotency_conflict");
        }
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Fact]
    public async Task Same_key_is_scoped_to_user_and_key_casing_is_not_normalized()
    {
        await using var factory = new AuthApiFactory(Database);
        var (one, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (two, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var first = one;
        using var second = two;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        foreach (var (client, key) in new[] { (first, "Key"), (second, "Key"), (first, "key") })
        {
            using var response = await CheckoutTestData.PostAsync(client, CheckoutTestData.Body(products), key);
            await CheckoutTestData.CreatedAsync(response);
        }
        await CheckoutTestData.AssertCountsAsync(Database, 3, 3);
    }

    [Theory]
    [InlineData("userId", false)]
    [InlineData("role", false)]
    [InlineData("total", false)]
    [InlineData("status", false)]
    [InlineData("currency", false)]
    [InlineData("expiresAt", false)]
    [InlineData("paid", false)]
    [InlineData("price", true)]
    [InlineData("unitPrice", true)]
    [InlineData("lineTotal", true)]
    [InlineData("name", true)]
    public async Task Unknown_client_authority_fields_are_rejected_at_root_and_item_level(string field, bool nested)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var item = new Dictionary<string, object> { ["productId"] = Guid.NewGuid(), ["quantity"] = 1 };
        var body = new Dictionary<string, object> { ["items"] = new[] { item } };
        (nested ? item : body)[field] = "tampered";
        using var response = await CheckoutTestData.PostAsync(customer, body, "tampered");
        await AuthenticationTests.Problem(response, 400, "validation_failed");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1001")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("2147483648")]
    [InlineData("null")]
    public async Task Quantities_are_required_bounded_JSON_integers(string quantity)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        using var body = JsonDocument.Parse("{\"items\":[{\"productId\":\"" + Guid.NewGuid() + "\",\"quantity\":" + quantity + "}]}");
        using var response = await CheckoutTestData.PostAsync(customer, body.RootElement, "quantity");
        await AuthenticationTests.Problem(response, 400, "validation_failed");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
    }

    [Fact]
    public async Task Duplicate_items_missing_fields_and_excessive_item_count_leave_no_claim()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var id = Guid.NewGuid();
        var bodies = new object[]
        {
            new { items = new object[] { new { productId = id, quantity = 1 }, new { productId = id.ToString().ToUpperInvariant(), quantity = 1 } } },
            new { items = Array.Empty<object>() }, new { }, new { items = new object?[] { null } },
            new { items = new[] { new { productId = Guid.Empty, quantity = 1 } } },
            new { items = new[] { new { quantity = 1 } } },
            new { items = new[] { new { productId = id } } },
            new { items = Enumerable.Range(0, 101).Select(_ => new { productId = Guid.NewGuid(), quantity = 1 }).ToArray() }
        };
        foreach (var body in bodies)
        {
            using var response = await CheckoutTestData.PostAsync(customer, body, "invalid-items");
            await AuthenticationTests.Problem(response, 400, "validation_failed");
        }
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
    }

    [Fact]
    public async Task Key_is_required_bounded_and_single_valued()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var body = new { items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } };
        foreach (var key in new[] { null, "", "has space", new string('a', 129) })
        {
            using var response = await CheckoutTestData.PostAsync(customer, body, key);
            await AuthenticationTests.Problem(response, 400, "validation_failed");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", new[] { "one", "two" });
        request.Headers.Add("X-CSRF-TOKEN", await BrowserRequests.TokenAsync(customer));
        using var multiple = await customer.SendAsync(request);
        await AuthenticationTests.Problem(multiple, 400, "validation_failed");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
    }

    [Fact]
    public async Task Real_customer_policy_csrf_and_SQL_ownership_apply_to_checkout_and_reads()
    {
        await using var factory = new AuthApiFactory(Database);
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var (ownerClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (otherClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var owner = ownerClient;
        using var other = otherClient;
        using var admin = adminClient;
        using var anonymous = factory.Browser();
        var body = CheckoutTestData.Body(products);
        using var unauthenticated = await CheckoutTestData.PostAsync(anonymous, body, "auth", csrf: false);
        await AuthenticationTests.Problem(unauthenticated, 401, "unauthenticated");
        using var forbidden = await CheckoutTestData.PostAsync(admin, body, "auth");
        await AuthenticationTests.Problem(forbidden, 403, "forbidden");
        using var noCsrf = await CheckoutTestData.PostAsync(owner, body, "auth", csrf: false);
        await AuthenticationTests.Problem(noCsrf, 400, "antiforgery_failed");
        using var created = await CheckoutTestData.PostAsync(owner, body, "auth");
        var order = await CheckoutTestData.CreatedAsync(created);
        using var permitted = await owner.GetAsync(OriginalLocation(order));
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        foreach (var id in new[] { order.Id, Guid.NewGuid() })
        {
            using var hidden = await other.GetAsync("/api/orders/" + id);
            await AuthenticationTests.Problem(hidden, 404, "not_found");
            using var adminDenied = await admin.GetAsync("/api/orders/" + id);
            await AuthenticationTests.Problem(adminDenied, 403, "forbidden");
        }
        // Security checks are also enforced before successful replay.
        using var replayWithoutCsrf = await CheckoutTestData.PostAsync(owner, body, "auth", csrf: false);
        await AuthenticationTests.Problem(replayWithoutCsrf, 400, "antiforgery_failed");
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Fact]
    public async Task Missing_archived_and_unrepresentable_money_fail_atomically_and_do_not_consume_key()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        using (var missing = await CheckoutTestData.PostAsync(customer, new { items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } }, "retry-failed"))
            await AuthenticationTests.Problem(missing, 404, "not_found");
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1, price: UsdMoney.Maximum);
        using (var overflow = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products, 2), "retry-failed"))
            await AuthenticationTests.Problem(overflow, 400, "validation_failed");
        await using (var edit = Database.CreateContext())
        {
            var product = await edit.Products.SingleAsync();
            product.Update(product.Name, "", UsdMoney.FromDecimal(0.10m), product.CategoryId, false, product.Version);
            await edit.SaveChangesAsync();
        }
        using (var archived = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "retry-failed"))
            await AuthenticationTests.Problem(archived, 409, "state_conflict");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
        await using (var edit = Database.CreateContext())
        {
            var product = await edit.Products.SingleAsync();
            product.Update(product.Name, "", product.Price, product.CategoryId, true, product.Version);
            (await edit.Categories.SingleAsync()).Archive(1);
            await edit.SaveChangesAsync();
        }
        using (var categoryArchived = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "retry-failed"))
            await AuthenticationTests.Problem(categoryArchived, 409, "state_conflict");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
        await using (var edit = Database.CreateContext())
        {
            var category = await edit.Categories.SingleAsync();
            category.Update(category.Name, true, category.Version);
            await edit.SaveChangesAsync();
        }
        using var success = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products, 3), "retry-failed");
        var order = await CheckoutTestData.CreatedAsync(success);
        Assert.Equal("0.30", order.Total);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    private static string OriginalLocation(OrderResponse order) => "/api/orders/" + order.Id;
}
