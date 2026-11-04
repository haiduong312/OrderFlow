using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CatalogReadTests(PostgresDatabase database)
{
    [Fact]
    public async Task Search_is_case_insensitive_literal_substring_and_category_filter_is_composed()
    {
        var marker = "find-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UnixEpoch;
        var first = Category.Create(marker + " A", now);
        var second = Category.Create(marker + " B", now);
        var products = new[]
        {
            Product.Create("KEY-" + Guid.NewGuid().ToString("N"), marker + " MixedCase", null, UsdMoney.FromDecimal(1m), first.Id, now),
            Product.Create("KEY-" + Guid.NewGuid().ToString("N"), marker + " MixedCase", null, UsdMoney.FromDecimal(2m), second.Id, now),
            Product.Create("KEY-" + Guid.NewGuid().ToString("N"), marker + " 100%_\\ literal", null, UsdMoney.FromDecimal(3m), first.Id, now)
        };
        await using (var context = database.CreateContext())
        {
            context.AddRange(first, second);
            context.AddRange(products);
            await context.SaveChangesAsync();
        }
        await using var factory = new AuthApiFactory(database);
        using var client = factory.Browser();
        var byName = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?q=" + Uri.EscapeDataString(marker.ToUpperInvariant() + " MIXEDCASE"), CatalogHttpTests.JsonOptions))!;
        Assert.Equal(2, byName.TotalCount);
        var filtered = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?q=" + marker + "&categoryId=" + first.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Equal(2, filtered.TotalCount);
        Assert.All(filtered.Items, product => Assert.Equal(first.Id, product.Category.Id));
        var bySku = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?q=" + products[0].Sku.ToLowerInvariant(), CatalogHttpTests.JsonOptions))!;
        Assert.Equal(products[0].Id, Assert.Single(bySku.Items).Id);
        var literal = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?q=" + Uri.EscapeDataString(marker + " 100%_\\"), CatalogHttpTests.JsonOptions))!;
        Assert.Equal(products[2].Id, Assert.Single(literal.Items).Id);
        var missing = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?categoryId=" + Guid.NewGuid(), CatalogHttpTests.JsonOptions))!;
        Assert.Empty(missing.Items);
        Assert.Equal(0, missing.TotalCount);
        var categories = (await client.GetFromJsonAsync<CatalogPage<CategoryResponse>>("/api/categories?q=" + marker.ToUpperInvariant()))!;
        Assert.Equal(2, categories.TotalCount);
    }

    [Fact]
    public async Task Pagination_has_defaults_limits_empty_past_end_and_unique_tie_breakers()
    {
        var now = DateTimeOffset.UnixEpoch;
        var category = Category.Create("Paging " + Guid.NewGuid().ToString("N"), now);
        var products = Enumerable.Range(0, 5).Select(_ => Product.Create("PAGE-" + Guid.NewGuid().ToString("N"),
            "Same name", null, UsdMoney.FromDecimal(4m), category.Id, now)).ToArray();
        await using (var context = database.CreateContext())
        {
            context.Add(category);
            context.AddRange(products);
            await context.SaveChangesAsync();
        }
        await using var factory = new AuthApiFactory(database);
        using var client = factory.Browser();
        foreach (var sort in new[] { "newest", "name", "price-asc", "price-desc" })
        {
            var actual = new List<Guid>();
            for (var page = 1; page <= 3; page++)
            {
                var url = $"/api/products?categoryId={category.Id}&sort={sort}&page={page}&pageSize=2";
                var result = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>(url, CatalogHttpTests.JsonOptions))!;
                Assert.Equal(5, result.TotalCount);
                Assert.Equal(page, result.Page);
                Assert.Equal(page == 3 ? 1 : 2, result.Items.Count);
                var repeat = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>(url, CatalogHttpTests.JsonOptions))!;
                Assert.Equal(result.Items.Select(product => product.Id), repeat.Items.Select(product => product.Id));
                actual.AddRange(result.Items.Select(product => product.Id));
            }
            var expected = products.Select(product => product.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
            if (sort is "newest" or "price-desc") Array.Reverse(expected);
            Assert.Equal(expected, actual);
            Assert.Equal(5, actual.Distinct().Count());
        }
        var defaults = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?categoryId=" + category.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Equal(1, defaults.Page);
        Assert.Equal(20, defaults.PageSize);
        var maximum = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?pageSize=100&categoryId=" + category.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Equal(100, maximum.PageSize);
        var empty = (await client.GetFromJsonAsync<CatalogPage<ProductResponse>>("/api/products?page=4&pageSize=2&categoryId=" + category.Id, CatalogHttpTests.JsonOptions))!;
        Assert.Empty(empty.Items);
        Assert.Equal(5, empty.TotalCount);
        await using var read = database.CreateContext();
        var service = new CatalogService(read, TimeProvider.System);
        await service.ProductsAsync(new ProductQuery { CategoryId = category.Id }, false, default);
        await service.CategoriesAsync(new CatalogQuery(), false, default);
        await service.ProductAsync(products[0].Id, false, default);
        await service.CategoryAsync(category.Id, false, default);
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Category_pagination_search_and_archival_are_bounded_and_admin_can_see_hidden_rows()
    {
        var marker = "categories-" + Guid.NewGuid().ToString("N");
        var categories = Enumerable.Range(0, 4).Select(_ => Category.Create(marker, DateTimeOffset.UnixEpoch)).ToArray();
        categories[0].Archive(1);
        await using (var context = database.CreateContext())
        {
            context.AddRange(categories);
            await context.SaveChangesAsync();
        }
        await using var factory = new AuthApiFactory(database);
        using var client = factory.Browser();
        var first = (await client.GetFromJsonAsync<CatalogPage<CategoryResponse>>("/api/categories?q=" + marker + "&sort=name&pageSize=2"))!;
        var second = (await client.GetFromJsonAsync<CatalogPage<CategoryResponse>>("/api/categories?q=" + marker + "&sort=name&pageSize=2&page=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(category => category.Id).Distinct().Count());
        Assert.DoesNotContain(categories[0].Id, first.Items.Concat(second.Items).Select(category => category.Id));
        var expected = categories.Skip(1).Select(category => category.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal);
        Assert.Equal(expected, first.Items.Concat(second.Items).Select(category => category.Id));
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = adminClient;
        var all = (await admin.GetFromJsonAsync<CatalogPage<CategoryResponse>>("/api/admin/categories?q=" + marker))!;
        Assert.Equal(4, all.TotalCount);
        using var invalid = await client.GetAsync("/api/categories?pageSize=101");
        await AuthenticationTests.Problem(invalid, 400, "validation_failed");
        using var invalidSort = await client.GetAsync("/api/categories?sort=price-asc");
        await AuthenticationTests.Problem(invalidSort, 400, "validation_failed");
    }

    [Fact]
    public async Task Openapi_documents_catalog_requests_responses_money_and_bounded_query_inputs()
    {
        await using var factory = new AuthApiFactory(database, "Development");
        using var client = factory.Browser();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        var create = paths.GetProperty("/api/admin/products").GetProperty("post");
        Assert.True(create.GetProperty("responses").TryGetProperty("201", out var created));
        Assert.True(created.GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(create.GetProperty("responses").GetProperty("409").GetProperty("content").TryGetProperty("application/problem+json", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CreateProductRequest");
        Assert.Contains("price", request.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var price = request.GetProperty("properties").GetProperty("price");
        Assert.Contains("string", price.GetProperty("type").ToString());
        Assert.True(price.TryGetProperty("pattern", out _));
        Assert.Contains("Positive USD price", price.GetProperty("description").GetString());
        Assert.False(request.GetProperty("properties").TryGetProperty("onHand", out _));
        Assert.False(schemas.GetProperty("UpdateProductRequest").GetProperty("properties").TryGetProperty("sku", out _));
        var updateProperties = schemas.GetProperty("UpdateProductRequest").GetProperty("properties");
        Assert.Equal("boolean", updateProperties.GetProperty("isActive").GetProperty("type").GetString());
        Assert.Equal("integer", updateProperties.GetProperty("expectedVersion").GetProperty("type").GetString());
        var response = schemas.GetProperty("ProductResponse").GetProperty("properties");
        Assert.Equal("string", response.GetProperty("price").GetProperty("type").GetString());
        Assert.Equal("string", response.GetProperty("currency").GetProperty("type").GetString());
        var parameters = paths.GetProperty("/api/products").GetProperty("get").GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "CategoryId");
        Assert.True(paths.TryGetProperty("/api/admin/categories/{id}/archive", out _));
        Assert.True(paths.TryGetProperty("/api/admin/products/{id}/archive", out _));
        Assert.False(paths.GetProperty("/api/admin/products/{id}").TryGetProperty("delete", out _));
    }
}
