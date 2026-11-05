using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CatalogConcurrencyTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Concurrent_normalized_sku_duplicates_are_resolved_by_the_unique_database_index()
    {
        var gate = new CommandRaceGate(command => command.Contains("INSERT INTO catalog.\"Products\"", StringComparison.Ordinal));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (firstClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        var (secondClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var first = firstClient;
        using var second = secondClient;
        // Different categories let both requests reach the contested SKU insert independently.
        var one = await CatalogHttpTests.Category(first);
        var two = await CatalogHttpTests.Category(second);
        var sku = "RACE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CatalogHttpTests.Send(first, HttpMethod.Post, "/api/admin/products", new { sku, name = "One", price = "1.00", categoryId = one.Id }),
            CatalogHttpTests.Send(second, HttpMethod.Post, "/api/admin/products", new { sku = sku.ToLowerInvariant(), name = "Two", price = "2.00", categoryId = two.Id }));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(new[] { 201, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "sku_conflict");
            response.Dispose();
        }
        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Products.CountAsync(product => product.Sku == sku));
    }

    [Fact]
    public async Task Concurrent_edits_with_the_same_version_cannot_overwrite_each_other()
    {
        var gate = new CommandRaceGate(command => command.Contains("catalog.\"Categories\"", StringComparison.Ordinal)
            && command.Contains("FOR UPDATE", StringComparison.Ordinal));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (firstClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        var (secondClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var first = firstClient;
        using var second = secondClient;
        var category = await CatalogHttpTests.Category(first);
        var product = await CatalogHttpTests.Product(first, category.Id);
        // Each request has already loaded version 1 before reaching this category-lock gate.
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CatalogHttpTests.Send(first, HttpMethod.Put, "/api/admin/products/" + product.Id, new { name = "First edit", price = "2.00", categoryId = category.Id, isActive = true, expectedVersion = 1 }),
            CatalogHttpTests.Send(second, HttpMethod.Put, "/api/admin/products/" + product.Id, new { name = "Second edit", price = "3.00", categoryId = category.Id, isActive = true, expectedVersion = 1 }));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(new[] { 200, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        ProductResponse? winner = null;
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "version_conflict");
            else winner = await response.Content.ReadFromJsonAsync<ProductResponse>(CatalogHttpTests.JsonOptions);
            response.Dispose();
        }
        await using var context = Database.CreateContext();
        var saved = await context.Products.AsNoTracking().SingleAsync(row => row.Id == product.Id);
        Assert.Equal(2, saved.Version);
        Assert.Equal(winner!.Name, saved.Name);
        Assert.Equal(winner.Price, saved.Price);
    }
}
