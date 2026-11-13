using System.Text;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Application.Modules.Catalog;
using StackExchange.Redis;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class CatalogCacheTests
{
    private readonly Clock clock = new();
    private readonly CacheStore store = new();
    private readonly Guid id = Guid.NewGuid();
    private CatalogCache Cache => new(store, clock, NullLogger<CatalogCache>.Instance);
    private CategoryResponse Value(string name = "Original") => new(id, name, true, 1);

    [Fact]
    public async Task Metrics_distinguish_hits_misses_and_dependency_failures_using_bounded_tags()
    {
        var outcomes = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == CatalogCache.MeterName) owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            Assert.Equal(1, value);
            var labels = tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal(new[] { "kind", "outcome" }, labels.Keys.Order().ToArray());
            Assert.Equal("category", labels["kind"]);
            outcomes.Add((string)labels["outcome"]!);
        });
        listener.Start();
        var cache = Cache;
        await cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(Value()), default);
        await cache.CategoryAsync(id, _ => throw new InvalidOperationException("Must hit cache"), default);
        store.GetFailure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "private");
        await cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(Value()), default);
        Assert.Equal(new[] { "miss", "set", "hit", "get_failure", "miss" }, outcomes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hit_miss_absolute_expiry_and_failed_invalidation_bound_staleness_without_sliding_refresh(bool timeout)
    {
        var cache = Cache;
        var reads = 0;
        Task<CategoryResponse?> Load(CancellationToken _) { reads++; return Task.FromResult<CategoryResponse?>(Value(reads == 1 ? "Old" : "New")); }
        Assert.Equal("Old", (await cache.CategoryAsync(id, Load, default))!.Name);
        var deadline = store.Expiry;
        clock.Now += TimeSpan.FromSeconds(29);
        store.RemoveFailure = timeout ? new RedisTimeoutException("not logged", CommandStatus.Unknown)
            : new RedisConnectionException(ConnectionFailureType.UnableToConnect, "not logged");
        await cache.InvalidateCategoryAsync(id, default);
        Assert.Equal("Old", (await cache.CategoryAsync(id, Load, default))!.Name);
        Assert.Equal(1, reads); Assert.Equal(deadline, store.Expiry);
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal("New", (await cache.CategoryAsync(id, Load, default))!.Name);
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("get", "connection")]
    [InlineData("set", "connection")]
    [InlineData("get", "timeout")]
    [InlineData("set", "timeout")]
    [InlineData("get", "server")]
    [InlineData("set", "server")]
    public async Task Classified_cache_failures_return_database_value(string operation, string kind)
    {
        Exception failure = kind switch
        {
            "timeout" => new RedisTimeoutException("private endpoint", CommandStatus.Unknown),
            "server" => new RedisServerException("private endpoint"),
            _ => new RedisConnectionException(ConnectionFailureType.UnableToConnect, "private endpoint")
        };
        if (operation == "get") store.GetFailure = failure; else store.SetFailure = failure;
        Assert.Equal(Value(), await Cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(Value()), default));
        Assert.Null(store.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_failure_is_propagated_even_when_Redis_also_fails_and_cancellation_is_not_swallowed(bool timeout)
    {
        Exception sqlFailure = timeout ? new TimeoutException("SQL timed out") : new InvalidOperationException("SQL failed");
        store.GetFailure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "private");
        Assert.Same(sqlFailure, await Record.ExceptionAsync(() => Cache.CategoryAsync(id, _ => throw sqlFailure, default)));
        Assert.Null(store.Bytes);
        store.GetFailure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(Value()), default));
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("missing-fields")]
    [InlineData("oversized")]
    public async Task Invalid_or_oversized_payload_is_a_miss_and_is_replaced(string payload)
    {
        store.Bytes = payload switch
        {
            "invalid-json" => Encoding.UTF8.GetBytes("{"),
            "missing-fields" => Encoding.UTF8.GetBytes("{\"validUntil\":\"2026-11-13T00:00:30Z\",\"value\":{}}"),
            _ => new byte[CatalogCache.MaximumPayloadBytes + 1]
        };
        var loaded = 0;
        Assert.Equal(Value(), await Cache.CategoryAsync(id, _ => { loaded++; return Task.FromResult<CategoryResponse?>(Value()); }, default));
        Assert.Equal(1, loaded); Assert.InRange(store.Bytes!.Length, 1, CatalogCache.MaximumPayloadBytes);
    }

    [Fact]
    public async Task Oversized_source_and_not_found_are_not_cached_and_slow_load_gets_no_fresh_TTL()
    {
        var cache = Cache;
        await cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(Value(new string('x', CatalogCache.MaximumPayloadBytes))), default);
        Assert.Null(store.Bytes);
        Assert.Null(await cache.CategoryAsync(id, _ => Task.FromResult<CategoryResponse?>(null), default));
        Assert.Null(store.Bytes);
        await cache.CategoryAsync(id, _ =>
        {
            clock.Now += CatalogCache.Lifetime;
            return Task.FromResult<CategoryResponse?>(Value());
        }, default);
        Assert.Null(store.Bytes);
    }

    [Fact]
    public async Task Disabled_cache_reads_source_without_local_storage()
    {
        var cache = new CatalogCache(null, clock, NullLogger<CatalogCache>.Instance);
        Assert.False(cache.Enabled);
        var reads = 0;
        for (var i = 0; i < 2; i++) await cache.CategoryAsync(id, _ => { reads++; return Task.FromResult<CategoryResponse?>(Value()); }, default);
        Assert.Equal(2, reads);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Deliberately retains expired bytes: tests exercise the application's freshness check, independently of Redis TTL.
    private sealed class CacheStore : IDistributedCache
    {
        public byte[]? Bytes { get; set; }
        public DateTimeOffset? Expiry { get; private set; }
        public Exception? GetFailure { get; set; }
        public Exception? SetFailure { get; set; }
        public Exception? RemoveFailure { get; set; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => GetFailure is null ? Task.FromResult(Bytes) : throw GetFailure;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (SetFailure is not null) throw SetFailure;
            Bytes = value; Expiry = options.AbsoluteExpiration; return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            if (RemoveFailure is not null) throw RemoveFailure;
            Bytes = null; return Task.CompletedTask;
        }
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
    }
}
