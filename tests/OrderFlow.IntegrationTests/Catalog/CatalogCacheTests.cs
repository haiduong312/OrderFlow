using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Realtime;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Catalog;

[CollectionDefinition("CatalogCache", DisableParallelization = true)]
public sealed class CatalogCacheCollection : ICollectionFixture<PostgresServer>, ICollectionFixture<RedisServer>;

[Collection("CatalogCache"), Trait("Category", "PostgreSQL"), Trait("Category", "Redis")]
public sealed class CatalogCacheTests(PostgresServer server, RedisServer redis, ITestOutputHelper output) : PostgresTest(server)
{
    private readonly string prefix = "catalog-test:" + Guid.NewGuid().ToString("N") + ":";
    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private Task<CatalogCacheApiHost> Host(bool enabled = true) => CatalogCacheApiHost.StartAsync(Database, redis.ConnectionString, prefix, clock, enabled);
    private async Task<Product> Seed()
    {
        await using var factory = new AuthApiFactory(Database);
        return Assert.Single(await CheckoutTestData.StockAsync(Database, factory, count: 1, stock: 2));
    }
    private static Task<ProductResponse?> Product(CatalogCacheApiHost host, Guid id) =>
        host.Client.GetFromJsonAsync<ProductResponse>($"/api/products/{id}", CatalogHttpTests.JsonOptions);
    private static Task<CategoryResponse?> Category(CatalogCacheApiHost host, Guid id) =>
        host.Client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{id}");
    private static Task<ProductResponse> Update(CatalogCacheApiHost host, Product product, string name, int version = 1) =>
        host.CatalogAsync(service => service.UpdateProductAsync(product.Id, new UpdateProductRequest
        {
            Name = name, Description = "Updated", Price = UsdMoney.FromDecimal(19.99m), CategoryId = product.CategoryId,
            IsActive = true, ExpectedVersion = version
        }, default));

    [Fact]
    public async Task Miss_then_hit_on_two_independent_API_hosts_and_inventory_is_never_cached()
    {
        var product = await Seed();
        await using var a = await Host(); await using var b = await Host();
        Assert.NotSame(a.App.Services.GetRequiredService<IDistributedCache>(), b.App.Services.GetRequiredService<IDistributedCache>());
        Assert.Equal(2, (await Product(a, product.Id))!.Available);
        Assert.Equal(3, a.Queries.Reads); // Product fields, category, live inventory.
        var store = a.App.Services.GetRequiredService<IDistributedCache>();
        var bytes = (await store.GetAsync(CatalogCache.ProductKey(product.Id)))!;
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("available", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reserved", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onHand", text, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(bytes.Length, 1, CatalogCache.MaximumPayloadBytes);
        Assert.Equal(12.50m, JsonDocument.Parse(bytes).RootElement.GetProperty("value").GetProperty("price").GetDecimal());

        Assert.Equal(2, (await Product(b, product.Id))!.Available);
        Assert.Equal(1, b.Queries.Reads); // B shares Redis but owns no local catalog cache.
        // Exercise the existing real cookie/CSRF checkout, which still reserves from PostgreSQL.
        await using var checkout = new AuthApiFactory(Database);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(checkout, "Customer");
        using (browser)
        using (var reserved = await CheckoutTestData.PostAsync(browser, CheckoutTestData.Body([product]), Guid.NewGuid().ToString("N")))
            await CheckoutTestData.CreatedAsync(reserved);
        Assert.Equal(1, (await Product(a, product.Id))!.Available);
        Assert.Equal(1, (await Product(b, product.Id))!.Available);
        Assert.Equal(4, a.Queries.Reads); Assert.Equal(2, b.Queries.Reads);
        Assert.Equal(bytes, await store.GetAsync(CatalogCache.ProductKey(product.Id)));
        var (secondBrowser, _) = await AuthenticatedBrowser.CreateAsync(checkout, "Customer");
        using (secondBrowser)
        using (var overdraw = await CheckoutTestData.PostAsync(secondBrowser, CheckoutTestData.Body([product], quantity: 2), Guid.NewGuid().ToString("N")))
            await AuthenticationTests.Problem(overdraw, 409, "insufficient_stock");
        using var json = await b.Client.GetAsync($"/api/products/{product.Id}");
        Assert.Equal("12.50", (await json.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("price").GetString());
    }

    [Fact]
    public async Task Sequential_product_update_archive_and_category_rename_archive_restore_are_visible_across_hosts()
    {
        var product = await Seed();
        await using var a = await Host(); await using var b = await Host();
        await Product(b, product.Id);
        await Update(a, product, "New product");
        Assert.Equal("New product", (await Product(b, product.Id))!.Name);
        await a.CatalogAsync(service => service.UpdateCategoryAsync(product.CategoryId,
            new UpdateCategoryRequest { Name = "New category", IsActive = true, ExpectedVersion = 1 }, default));
        Assert.Equal("New category", (await Product(b, product.Id))!.Category.Name);
        await a.CatalogAsync(service => service.ArchiveCategoryAsync(product.CategoryId, new ArchiveRequest { ExpectedVersion = 2 }, default));
        using (var hidden = await b.Client.GetAsync($"/api/products/{product.Id}")) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await b.Client.GetAsync($"/api/categories/{product.CategoryId}")) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await a.CatalogAsync(service => service.UpdateCategoryAsync(product.CategoryId,
            new UpdateCategoryRequest { Name = "Restored", IsActive = true, ExpectedVersion = 3 }, default));
        Assert.Equal("Restored", (await Product(b, product.Id))!.Category.Name);
        var target = await a.CatalogAsync(service => service.CreateCategoryAsync(new CreateCategoryRequest { Name = "Reassigned" }, default));
        await a.CatalogAsync(service => service.UpdateProductAsync(product.Id, new UpdateProductRequest
        {
            Name = "Moved", Price = UsdMoney.FromDecimal(20m), CategoryId = target.Id, IsActive = true, ExpectedVersion = 2
        }, default));
        Assert.Equal(target.Id, (await Product(b, product.Id))!.Category.Id);
        await a.CatalogAsync(service => service.ArchiveProductAsync(product.Id, new ArchiveRequest { ExpectedVersion = 3 }, default));
        using (var hidden = await b.Client.GetAsync($"/api/products/{product.Id}")) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Null(await b.App.Services.GetRequiredService<IDistributedCache>().GetAsync(CatalogCache.ProductKey(product.Id)));
        // Admin reads bypass cache and see archived data.
        Assert.False((await b.CatalogAsync(service => service.ProductAsync(product.Id, true, default))).IsActive);
    }

    [Fact]
    public async Task Failed_postcommit_invalidation_leaves_old_shared_entry_only_until_its_deadline()
    {
        var product = await Seed();
        await using var a = await Host(); await using var b = await Host();
        await Product(b, product.Id);
        await using var context = Database.CreateContext();
        var removeObserved = false;
        var faulty = new PausedCache(a.App.Services.GetRequiredService<IDistributedCache>())
        {
            PauseWrites = false, FailRemove = true,
            OnRemove = () => { Assert.Null(context.Database.CurrentTransaction); removeObserved = true; }
        };
        var service = new CatalogService(context, clock, new CatalogCache(faulty, clock, NullLogger<CatalogCache>.Instance));
        var updated = await service.UpdateProductAsync(product.Id, new UpdateProductRequest
        {
            Name = "Committed despite cache", Price = UsdMoney.FromDecimal(20m), CategoryId = product.CategoryId,
            IsActive = true, ExpectedVersion = 1
        }, default);
        Assert.True(removeObserved); Assert.Equal(2, updated.Version);
        Assert.Equal(product.Name, (await Product(b, product.Id))!.Name);
        Assert.Equal("Committed despite cache", (await a.CatalogAsync(catalog => catalog.ProductAsync(product.Id, true, default))).Name);
        clock.Advance(CatalogCache.Lifetime);
        Assert.Equal("Committed despite cache", (await Product(b, product.Id))!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_old_refill_after_invalidation_can_be_stale_but_expires_at_original_read_deadline(bool archive)
    {
        var product = await Seed();
        await using var a = await Host(); await using var b = await Host();
        var store = b.App.Services.GetRequiredService<IDistributedCache>();
        var gate = new PausedCache(store);
        await using var context = Database.CreateContext();
        var oldReader = new CatalogService(context, clock, new CatalogCache(gate, clock, NullLogger<CatalogCache>.Instance));
        var oldRead = oldReader.ProductAsync(product.Id, false, default);
        try
        {
            await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, gate.Participants);
            if (archive) await a.CatalogAsync(service => service.ArchiveProductAsync(product.Id, new ArchiveRequest { ExpectedVersion = 1 }, default));
            else await Update(a, product, "After commit");
            Assert.Null(await store.GetAsync(CatalogCache.ProductKey(product.Id))); // Invalidation really ran.
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(product.Name, (await oldRead).Name);
        Assert.Equal(product.Name, (await Product(b, product.Id))!.Name); // Explicitly permitted stale refill.
        clock.Advance(CatalogCache.Lifetime); // Exact boundary; no real sleeps or Redis clock assumptions.
        if (archive)
        {
            using var hidden = await b.Client.GetAsync($"/api/products/{product.Id}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        else Assert.Equal("After commit", (await Product(b, product.Id))!.Name);
    }

    [Fact]
    public async Task Redis_outage_falls_back_on_each_API_and_mutations_still_commit_with_safe_logs()
    {
        var product = await Seed();
        await using var a = await Host(); await using var b = await Host();
        await Product(a, product.Id);
        await redis.StopAsync();
        try
        {
            await Update(a, product, "During outage");
            Assert.Equal("During outage", (await Product(a, product.Id))!.Name);
            Assert.Equal("During outage", (await Product(b, product.Id))!.Name);
            Assert.Contains(a.Logs.Entries, entry => entry.Event.Id == 2501 && Equals(entry.Fields["Operation"], "remove"));
            Assert.Contains(b.Logs.Entries, entry => entry.Event.Id == 2501 && Equals(entry.Fields["Operation"], "get"));
            Assert.All(a.Logs.Entries.Where(entry => entry.Event.Id == 2501), entry => Assert.Null(entry.Exception));
            b.Queries.RejectReads = true;
            using var failed = await b.Client.GetAsync($"/api/products/{product.Id}");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); // SQL failure is not a cache miss/empty success.
        }
        finally { await redis.StartAsync(); }
    }

    [Fact]
    public async Task Repeatable_ten_category_reads_measure_exact_SQL_reduction_and_lists_stay_uncached()
    {
        var product = await Seed();
        await using var baseline = await Host(enabled: false);
        await using var cached = await Host();
        for (var i = 0; i < 10; i++) Assert.NotNull(await Category(baseline, product.CategoryId));
        for (var i = 0; i < 10; i++) Assert.NotNull(await Category(cached, product.CategoryId));
        Assert.Equal(10, baseline.Queries.Reads); Assert.Equal(1, cached.Queries.Reads);
        output.WriteLine($"GET category detail x10: cache disabled={baseline.Queries.Reads} SQL SELECTs; Redis enabled={cached.Queries.Reads} SQL SELECTs (1 miss, 9 hits).");
        var before = cached.Queries.Reads;
        for (var i = 0; i < 2; i++)
            Assert.NotNull(await cached.Client.GetFromJsonAsync<CatalogPage<CategoryResponse>>("/api/categories?q=Checkout"));
        Assert.Equal(4, cached.Queries.Reads - before); // Count + page each time, no arbitrary query keys.
    }

    private sealed class PausedCache(IDistributedCache inner) : IDistributedCache
    {
        public bool PauseWrites { get; init; } = true;
        public bool FailRemove { get; init; }
        public Action? OnRemove { get; init; }
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Participants { get; private set; }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (PauseWrites && key.StartsWith("catalog:v1:product:", StringComparison.Ordinal))
            {
                Participants++; Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            }
            await inner.SetAsync(key, value, options, token);
        }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            OnRemove?.Invoke();
            if (FailRemove) throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Injected unavailable cache during removal.");
            return inner.RemoveAsync(key, token);
        }
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
    }
}
