using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Common.Money;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CatalogHttpTests(PostgresServer server) : PostgresTest(server)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UsdMoneyJsonConverter() }
    };

    private static string Sku() => "CAT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    [Fact]
    public async Task Admin_creates_updates_and_archives_products_with_string_money_and_immutable_sku()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var category = await Category(admin);
        var sku = Sku();
        using var created = await Send(admin, HttpMethod.Post, "/api/admin/products", new { sku = sku.ToLowerInvariant(), name = "  Product  ", description = " info ", price = "12.50", categoryId = category.Id });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, json.GetProperty("price").ValueKind);
        Assert.Equal("12.50", json.GetProperty("price").GetString());
        Assert.Equal("USD", json.GetProperty("currency").GetString());
        var product = json.Deserialize<ProductResponse>(JsonOptions)!;
        Assert.Equal(sku, product.Sku);
        Assert.Equal("Product", product.Name);
        Assert.Equal("info", product.Description);
        Assert.Equal("/api/admin/products/" + product.Id, created.Headers.Location!.AbsolutePath);
        using var changed = await Send(admin, HttpMethod.Put, "/api/admin/products/" + product.Id,
            new { name = "Changed", description = "", price = "19.99", categoryId = category.Id, isActive = true, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions))!;
        Assert.Equal(product.Id, updated.Id);
        Assert.Equal(product.Sku, updated.Sku);
        Assert.Equal(2, updated.Version);
        using var stale = await Send(admin, HttpMethod.Put, "/api/admin/products/" + product.Id,
            new { name = "Stale", price = "1.00", categoryId = category.Id, isActive = true, expectedVersion = 1 });
        await AuthenticationTests.Problem(stale, 409, "version_conflict");
        using var archived = await Send(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/archive", new { expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var archivedProduct = (await archived.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions))!;
        Assert.False(archivedProduct.IsActive);
        Assert.Equal(3, archivedProduct.Version);
        using var repeat = await Send(admin, HttpMethod.Post, $"/api/admin/products/{product.Id}/archive", new { expectedVersion = 3 });
        Assert.Equal(3, (await repeat.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions))!.Version);
        using var publicClient = factory.Browser();
        using var hidden = await publicClient.GetAsync("/api/products/" + product.Id);
        await AuthenticationTests.Problem(hidden, 404, "not_found");
        using var visibleToAdmin = await admin.GetAsync("/api/admin/products/" + product.Id);
        Assert.Equal(HttpStatusCode.OK, visibleToAdmin.StatusCode);
        var adminPage = (await admin.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/admin/products?categoryId=" + category.Id, JsonOptions))!;
        Assert.False(Assert.Single(adminPage.Items).IsActive);
        using var duplicateArchivedSku = await Send(admin, HttpMethod.Post, "/api/admin/products", new { sku, name = "Duplicate archived SKU", price = "1.00", categoryId = category.Id });
        await AuthenticationTests.Problem(duplicateArchivedSku, 409, "sku_conflict");
        using var list = await publicClient.GetAsync("/api/products?categoryId=" + category.Id);
        Assert.Empty((await list.Content.ReadFromJsonAsync<CatalogPage<ProductResponse>>(JsonOptions))!.Items);
        using var deleted = await Send(admin, HttpMethod.Delete, "/api/admin/products/" + product.Id, new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleted.StatusCode);
    }

    [Fact]
    public async Task Category_archival_hides_children_without_cascading_and_restore_respects_product_archival()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var category = await Category(admin);
        var active = await Product(admin, category.Id);
        var archived = await Product(admin, category.Id);
        using var productArchive = await Send(admin, HttpMethod.Post, $"/api/admin/products/{archived.Id}/archive", new { expectedVersion = 1 });
        using var categoryArchive = await Send(admin, HttpMethod.Post, $"/api/admin/categories/{category.Id}/archive", new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, categoryArchive.StatusCode);
        using var customer = factory.Browser();
        foreach (var url in new[] { "/api/products/" + active.Id, "/api/products/" + archived.Id, "/api/categories/" + category.Id })
        {
            using var hidden = await customer.GetAsync(url);
            await AuthenticationTests.Problem(hidden, 404, "not_found");
        }
        var adminProduct = await admin.GetFromJsonAsync<ProductResponse>("/api/admin/products/" + active.Id, JsonOptions);
        Assert.True(adminProduct!.IsActive);
        Assert.False(adminProduct.Category.IsActive);
        using var assignment = await Send(admin, HttpMethod.Post, "/api/admin/products", new { sku = Sku(), name = "Blocked", price = "1.00", categoryId = category.Id });
        await AuthenticationTests.Problem(assignment, 409, "state_conflict");
        using var restore = await Send(admin, HttpMethod.Put, "/api/admin/categories/" + category.Id, new { name = category.Name, isActive = true, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using var visible = await customer.GetAsync("/api/products/" + active.Id);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        using var stillHidden = await customer.GetAsync("/api/products/" + archived.Id);
        await AuthenticationTests.Problem(stillHidden, 404, "not_found");
        using var productRestore = await Send(admin, HttpMethod.Put, "/api/admin/products/" + archived.Id,
            new { name = archived.Name, price = "1.00", categoryId = category.Id, isActive = true, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, productRestore.StatusCode);
        using var restoredVisible = await customer.GetAsync("/api/products/" + archived.Id);
        Assert.Equal(HttpStatusCode.OK, restoredVisible.StatusCode);
        using var staleCategory = await Send(admin, HttpMethod.Put, "/api/admin/categories/" + category.Id, new { name = "Stale", isActive = true, expectedVersion = 1 });
        await AuthenticationTests.Problem(staleCategory, 409, "version_conflict");
    }

    [Fact]
    public async Task Catalog_mutations_require_admin_and_antiforgery_while_public_reads_are_anonymous()
    {
        await using var factory = new AuthApiFactory(Database);
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        var (customerClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var admin = adminClient;
        using var customer = customerClient;
        using var anonymous = factory.Browser();
        var category = await Category(admin);
        var product = await Product(admin, category.Id);
        var cases = new (HttpMethod Method, string Path, object Body)[]
        {
            (HttpMethod.Post, "/api/admin/products", new { sku = Sku(), name = "Name", price = "1.00", categoryId = category.Id }),
            (HttpMethod.Put, "/api/admin/products/" + product.Id, new { name = "Edit", price = "1.00", categoryId = category.Id, isActive = true, expectedVersion = 1 }),
            (HttpMethod.Post, $"/api/admin/products/{product.Id}/archive", new { expectedVersion = 1 }),
            (HttpMethod.Post, "/api/admin/categories", new { name = "Category" }),
            (HttpMethod.Put, "/api/admin/categories/" + category.Id, new { name = "Edit", isActive = true, expectedVersion = 1 }),
            (HttpMethod.Post, $"/api/admin/categories/{category.Id}/archive", new { expectedVersion = 1 })
        };
        foreach (var (method, path, body) in cases)
        {
            using var unauthenticated = await Send(anonymous, method, path, body, csrf: false);
            await AuthenticationTests.Problem(unauthenticated, 401, "unauthenticated");
            using var forbidden = await Send(customer, method, path, body);
            await AuthenticationTests.Problem(forbidden, 403, "forbidden");
            using var missingToken = await Send(admin, method, path, body, csrf: false);
            await AuthenticationTests.Problem(missingToken, 400, "antiforgery_failed");
        }
        foreach (var path in new[] { "/api/products/" + product.Id, "/api/categories/" + category.Id, "/api/products", "/api/categories" })
        {
            using var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var forbiddenRead = await customer.GetAsync("/api/admin/products");
        await AuthenticationTests.Problem(forbiddenRead, 403, "forbidden");
        using var anonymousAdmin = await anonymous.GetAsync("/api/admin/categories");
        await AuthenticationTests.Problem(anonymousAdmin, 401, "unauthenticated");
    }

    [Theory]
    [InlineData("price", "\"0.00\"")]
    [InlineData("price", "null")]
    [InlineData("price", "\"1.000\"")]
    [InlineData("price", "1.25")]
    [InlineData("price", "\"10000000000000000.00\"")]
    [InlineData("price", "\"-1.00\"")]
    [InlineData("name", "\"  \"")]
    [InlineData("sku", "\"not valid\"")]
    [InlineData("categoryId", "\"00000000-0000-0000-0000-000000000000\"")]
    public async Task Product_validation_rejects_invalid_fields_without_writes(string field, string value)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var category = await Category(admin);
        var sku = Sku();
        var body = new Dictionary<string, object?> { ["sku"] = sku, ["name"] = "Name", ["price"] = "1.00", ["categoryId"] = category.Id };
        body[field] = JsonSerializer.Deserialize<JsonElement>(value);
        using var response = await Send(admin, HttpMethod.Post, "/api/admin/products", body);
        await AuthenticationTests.Problem(response, 400, "validation_failed");
        await using var context = Database.CreateContext();
        Assert.False(await context.Products.AnyAsync(product => product.Sku == sku));
    }

    [Theory]
    [InlineData("onHand", "10")]
    [InlineData("reserved", "1")]
    [InlineData("available", "10")]
    [InlineData("sku", "\"CHANGED\"")]
    [InlineData("id", "\"11111111-1111-4111-8111-111111111111\"")]
    public async Task Product_update_rejects_inventory_and_immutable_fields(string field, string value)
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var category = await Category(admin);
        var product = await Product(admin, category.Id);
        var body = new Dictionary<string, object?> { ["name"] = "Changed", ["price"] = "2.00", ["categoryId"] = category.Id, ["isActive"] = true, ["expectedVersion"] = 1 };
        body[field] = JsonSerializer.Deserialize<JsonElement>(value);
        using var response = await Send(admin, HttpMethod.Put, "/api/admin/products/" + product.Id, body);
        await AuthenticationTests.Problem(response, 400, "validation_failed");
        var unchanged = await admin.GetFromJsonAsync<ProductResponse>("/api/admin/products/" + product.Id, JsonOptions);
        Assert.Equal(product, unchanged);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("sort=unsupported")]
    [InlineData("categoryId=invalid")]
    public async Task Invalid_query_returns_400(string query)
    {
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        using var response = await client.GetAsync("/api/products?" + query);
        await AuthenticationTests.Problem(response, 400, "validation_failed");
    }

    [Fact]
    public async Task Missing_resources_and_categories_and_bounded_inputs_have_explicit_results()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        foreach (var body in new[] { new { name = " " }, new { name = new string('x', 201) } })
        {
            using var category = await Send(admin, HttpMethod.Post, "/api/admin/categories", body);
            await AuthenticationTests.Problem(category, 400, "validation_failed");
        }
        using var missingCategory = await Send(admin, HttpMethod.Post, "/api/admin/products", new { sku = Sku(), name = "Name", price = "1.00", categoryId = Guid.NewGuid() });
        await AuthenticationTests.Problem(missingCategory, 400, "validation_failed");
        foreach (var path in new[] { "/api/products/", "/api/categories/", "/api/admin/products/", "/api/admin/categories/" })
        {
            using var missing = await admin.GetAsync(path + Guid.NewGuid());
            await AuthenticationTests.Problem(missing, 404, "not_found");
        }
        using var longSearch = await admin.GetAsync("/api/products?q=" + new string('x', 101));
        await AuthenticationTests.Problem(longSearch, 400, "validation_failed");
        var validCategory = await Category(admin);
        var validProduct = await Product(admin, validCategory.Id);
        foreach (var body in new object[]
        {
            new { name = "Edit", price = "1.00", categoryId = validCategory.Id, expectedVersion = 1 },
            new { name = "Edit", price = "1.00", categoryId = validCategory.Id, isActive = true },
            new { name = "Edit", price = "1.00", categoryId = validCategory.Id, isActive = (bool?)null, expectedVersion = 1 }
        })
        {
            using var invalidUpdate = await Send(admin, HttpMethod.Put, "/api/admin/products/" + validProduct.Id, body);
            await AuthenticationTests.Problem(invalidUpdate, 400, "validation_failed");
        }
    }

    internal static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", await BrowserRequests.TokenAsync(client));
        return await client.SendAsync(request);
    }

    internal static async Task<CategoryResponse> Category(HttpClient admin)
    {
        using var response = await Send(admin, HttpMethod.Post, "/api/admin/categories", new { name = "Category " + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    internal static async Task<ProductResponse> Product(HttpClient admin, Guid categoryId)
    {
        using var response = await Send(admin, HttpMethod.Post, "/api/admin/products", new { sku = Sku(), name = "Product", price = "1.00", categoryId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions))!;
    }
}
