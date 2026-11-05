using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Catalog;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class InventoryHttpTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Products_start_at_zero_and_receipt_adjustment_append_actor_reason_and_snapshot()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, actorId) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await Product(admin);
        Assert.Equal(0, product.Available);
        var initial = (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!;
        Assert.Equal(new InventoryResponse(product.Id, 0, 0, 0), initial);
        var initialHistory = (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id)))!;
        var opening = Assert.Single(initialHistory.Items);
        Assert.Equal(StockMovementKind.Initialization, opening.Kind);
        Assert.Equal(StockActorKind.System, opening.ActorKind);
        Assert.Null(opening.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(opening.Reason));
        var operationId = Guid.NewGuid();
        using var receipt = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId, quantity = 10, reason = " Supplier delivery " });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        var movement = (await receipt.Content.ReadFromJsonAsync<StockMovementResponse>())!;
        Assert.Equal(operationId, movement.OperationId);
        Assert.Equal(actorId, movement.ActorUserId);
        Assert.Equal(StockActorKind.Admin, movement.ActorKind);
        Assert.Equal("Supplier delivery", movement.Reason);
        Assert.Equal(0, movement.OnHandBefore);
        Assert.Equal(10, movement.OnHandAfter);
        Assert.Equal(1, movement.Revision);
        var raw = await receipt.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        Assert.EndsWith("Z", document.RootElement.GetProperty("occurredAt").GetString());
        using var adjustment = await CatalogHttpTests.Send(admin, HttpMethod.Post, Adjustment(product.Id), new { operationId = Guid.NewGuid(), delta = -3, reason = "Damaged units" });
        Assert.Equal(HttpStatusCode.Created, adjustment.StatusCode);
        var balance = (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!;
        Assert.Equal(7, balance.OnHand);
        Assert.Equal(0, balance.Reserved);
        Assert.Equal(7, balance.Available);
        Assert.Equal(2, balance.Revision);
        using var customer = factory.Browser();
        var publicProduct = (await customer.GetFromJsonAsync<ProductResponse>("/api/products/" + product.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Equal(7, publicProduct.Available);
        Assert.Equal(product.Version, publicProduct.Version); // Inventory does not edit Catalog version.
        var list = (await customer.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?categoryId=" + product.Category.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Equal(7, Assert.Single(list.Items).Available);
        var history = (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id) + "?pageSize=2"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(new long[] { 2, 1 }, history.Items.Select(item => item.Revision));
        var tail = (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id) + "?pageSize=2&page=2"))!;
        Assert.Equal(0, Assert.Single(tail.Items).Revision);
        using var invalidPage = await admin.GetAsync(History(product.Id) + "?pageSize=101");
        await AuthenticationTests.Problem(invalidPage, 400, "validation_failed");
    }

    [Fact]
    public async Task Reserved_floor_is_enforced_and_rejections_leave_no_movement()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await Product(admin);
        using var receipt = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 10, reason = "Opening receipt" });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        // Fixture models an existing reservation; no production reservation mutation API exists yet.
        await using (var setup = Database.CreateContext())
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory.\"Balances\" SET \"Reserved\" = 7 WHERE \"ProductId\" = {product.Id}");
        using var denied = await CatalogHttpTests.Send(admin, HttpMethod.Post, Adjustment(product.Id), new { operationId = Guid.NewGuid(), delta = -4, reason = "Would touch reserved stock" });
        await AuthenticationTests.Problem(denied, 409, "insufficient_stock");
        var unchanged = (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!;
        Assert.Equal(new InventoryResponse(product.Id, 10, 7, 1), unchanged);
        Assert.Equal(2, (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id)))!.TotalCount);
        using var allowed = await CatalogHttpTests.Send(admin, HttpMethod.Post, Adjustment(product.Id), new { operationId = Guid.NewGuid(), delta = -3, reason = "Remove available units only" });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        var boundary = (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!;
        Assert.Equal(7, boundary.OnHand);
        Assert.Equal(7, boundary.Reserved);
        Assert.Equal(0, boundary.Available);
    }

    [Fact]
    public async Task Operation_replay_returns_original_movement_and_conflicting_actor_body_kind_or_product_is_409()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await Product(admin);
        var operationId = Guid.NewGuid();
        using var first = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId, quantity = 5, reason = " Original " });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadAsStringAsync();
        using var later = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 2, reason = "Later" });
        using var replay = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId, quantity = 5, reason = "Original" });
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        using var changed = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId, quantity = 6, reason = "Original" });
        await AuthenticationTests.Problem(changed, 409, "idempotency_conflict");
        using var changedKind = await CatalogHttpTests.Send(admin, HttpMethod.Post, Adjustment(product.Id), new { operationId, delta = 5, reason = "Original" });
        await AuthenticationTests.Problem(changedKind, 409, "idempotency_conflict");
        using var changedProduct = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(Guid.NewGuid()), new { operationId, quantity = 5, reason = "Original" });
        await AuthenticationTests.Problem(changedProduct, 409, "idempotency_conflict");
        var (otherClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var other = otherClient;
        using var changedActor = await CatalogHttpTests.Send(other, HttpMethod.Post, Receipt(product.Id), new { operationId, quantity = 5, reason = "Original" });
        await AuthenticationTests.Problem(changedActor, 409, "idempotency_conflict");
        Assert.Equal(7, (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!.OnHand);
        Assert.Equal(3, (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id)))!.TotalCount);
    }

    [Fact]
    public async Task Stock_endpoints_require_admin_and_writes_require_csrf()
    {
        await using var factory = new AuthApiFactory(Database);
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        var (customerClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var admin = adminClient;
        using var customer = customerClient;
        using var anonymous = factory.Browser();
        var product = await Product(admin);
        foreach (var id in new[] { product.Id, Guid.NewGuid() })
        {
            foreach (var path in new[] { Inventory(id), History(id) })
            {
                using var unauthenticated = await anonymous.GetAsync(path);
                await AuthenticationTests.Problem(unauthenticated, 401, "unauthenticated");
                using var forbidden = await customer.GetAsync(path);
                await AuthenticationTests.Problem(forbidden, 403, "forbidden");
            }
        }
        foreach (var path in new[] { Receipt(product.Id), Adjustment(product.Id) })
        {
            var body = new { operationId = Guid.NewGuid(), quantity = 1, delta = 1, reason = "Reason" };
            using var unauthenticated = await CatalogHttpTests.Send(anonymous, HttpMethod.Post, path, body, csrf: false);
            await AuthenticationTests.Problem(unauthenticated, 401, "unauthenticated");
            using var forbidden = await CatalogHttpTests.Send(customer, HttpMethod.Post, path, body);
            await AuthenticationTests.Problem(forbidden, 403, "forbidden");
            using var missingCsrf = await CatalogHttpTests.Send(admin, HttpMethod.Post, path, body, csrf: false);
            await AuthenticationTests.Problem(missingCsrf, 400, "antiforgery_failed");
        }
        using var missing = await admin.GetAsync(Inventory(Guid.NewGuid()));
        await AuthenticationTests.Problem(missing, 404, "not_found");
    }

    [Theory]
    [InlineData("quantity", "0")]
    [InlineData("quantity", "-1")]
    [InlineData("quantity", "1.5")]
    [InlineData("quantity", "2147483648")]
    [InlineData("reason", "\" \"")]
    [InlineData("operationId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("actorUserId", "\"11111111-1111-4111-8111-111111111111\"")]
    [InlineData("onHand", "10")]
    [InlineData("reserved", "1")]
    [InlineData("available", "10")]
    public async Task Receipt_validation_and_actor_or_derived_value_spoofing_are_rejected(string field, string value)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await Product(admin);
        var body = new Dictionary<string, object?> { ["operationId"] = Guid.NewGuid(), ["quantity"] = 1, ["reason"] = "Reason" };
        body[field] = JsonSerializer.Deserialize<JsonElement>(value);
        using var denied = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), body);
        await AuthenticationTests.Problem(denied, 400, "validation_failed");
        Assert.Equal(0, (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!.OnHand);
        Assert.Equal(1, (await admin.GetFromJsonAsync<StockHistoryResponse>(History(product.Id)))!.TotalCount);
    }

    [Fact]
    public async Task Limits_missing_fields_and_archived_inventory_are_explicit()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await Product(admin);
        foreach (var body in new object[]
        {
            new { operationId = Guid.NewGuid(), delta = 0, reason = "Reason" },
            new { operationId = Guid.NewGuid(), reason = "Missing delta" },
            new { operationId = Guid.NewGuid(), delta = 1, reason = new string('r', 501) }
        })
        {
            using var invalid = await CatalogHttpTests.Send(admin, HttpMethod.Post, Adjustment(product.Id), body);
            await AuthenticationTests.Problem(invalid, 400, "validation_failed");
        }
        using var archived = await CatalogHttpTests.Send(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/archive", new { expectedVersion = 1 });
        using var receipt = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = int.MaxValue, reason = "Archived physical stock" });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        using var overflow = await CatalogHttpTests.Send(admin, HttpMethod.Post, Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 1, reason = "Overflow" });
        await AuthenticationTests.Problem(overflow, 409, "stock_limit_exceeded");
        Assert.Equal(int.MaxValue, (await admin.GetFromJsonAsync<InventoryResponse>(Inventory(product.Id)))!.OnHand);
        using var hidden = await admin.GetAsync("/api/products/" + product.Id);
        await AuthenticationTests.Problem(hidden, 404, "not_found");
    }

    [Fact]
    public async Task Openapi_describes_inventory_and_available_as_read_only_response_data()
    {
        await using var factory = new AuthApiFactory(Database, "Development");
        using var client = factory.Browser();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("StockReceiptRequest").GetProperty("properties");
        Assert.False(request.TryGetProperty("actorUserId", out _));
        Assert.False(request.TryGetProperty("available", out _));
        Assert.Equal("integer", request.GetProperty("quantity").GetProperty("type").GetString());
        Assert.Equal("integer", schemas.GetProperty("ProductResponse").GetProperty("properties").GetProperty("available").GetProperty("type").GetString());
        Assert.Equal("integer", schemas.GetProperty("InventoryResponse").GetProperty("properties").GetProperty("available").GetProperty("type").GetString());
        Assert.Equal("string", schemas.GetProperty("StockMovementResponse").GetProperty("properties").GetProperty("occurredAt").GetProperty("type").GetString());
        Assert.True(document.GetProperty("paths").GetProperty("/api/admin/products/{productId}/inventory").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(document.GetProperty("paths").GetProperty("/api/admin/products/{productId}/stock-history").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/admin/products/{productId}/stock-adjustments", out _));
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/admin/products/{productId}/stock-receipts", out _));
    }

    internal static string Inventory(Guid id) => $"/api/admin/products/{id}/inventory";
    internal static string History(Guid id) => $"/api/admin/products/{id}/stock-history";
    internal static string Receipt(Guid id) => $"/api/admin/products/{id}/stock-receipts";
    internal static string Adjustment(Guid id) => $"/api/admin/products/{id}/stock-adjustments";
    internal static async Task<ProductResponse> Product(HttpClient admin)
    {
        var category = await CatalogHttpTests.Category(admin);
        return await CatalogHttpTests.Product(admin, category.Id);
    }
}
