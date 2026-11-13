using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace OrderFlow.Application.Modules.Catalog;

// Only catalog data belongs here. In particular, this DTO cannot carry Inventory.Available.
public sealed record ProductCatalogEntry(Guid Id, string Sku, string Name, string Description, decimal Price,
    Guid CategoryId, bool IsActive, int Version);

/// <summary>Distributed-only cache-aside. Database delegates run outside all cache exception handlers.</summary>
public sealed class CatalogCache(IDistributedCache? store, TimeProvider clock, ILogger<CatalogCache> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    public const int MaximumPayloadBytes = 16 * 1024;
    public const string MeterName = "OrderFlow.CatalogCache";
    public bool Enabled => store is not null;
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("catalog_cache_operations");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public static string ProductKey(Guid id) => $"catalog:v1:product:{id:N}";
    public static string CategoryKey(Guid id) => $"catalog:v1:category:{id:N}";

    public Task<ProductCatalogEntry?> ProductAsync(Guid id, Func<CancellationToken, Task<ProductCatalogEntry?>> load, CancellationToken token) =>
        ReadAsync(ProductKey(id), "product", load, token);
    public Task<CategoryResponse?> CategoryAsync(Guid id, Func<CancellationToken, Task<CategoryResponse?>> load, CancellationToken token) =>
        ReadAsync(CategoryKey(id), "category", load, token);
    public Task InvalidateProductAsync(Guid id, CancellationToken token) => RemoveAsync(ProductKey(id), "product", token);
    public Task InvalidateCategoryAsync(Guid id, CancellationToken token) => RemoveAsync(CategoryKey(id), "category", token);

    private async Task<T?> ReadAsync<T>(string key, string kind, Func<CancellationToken, Task<T?>> load, CancellationToken token) where T : class
    {
        token.ThrowIfCancellationRequested();
        if (store is null) return await load(token);
        byte[]? bytes = null;
        var readable = true;
        try { bytes = await store.GetAsync(key, token); }
        catch (Exception failure) when (IsCacheFailure(failure)) { readable = false; Failure("get", kind, failure); }

        if (bytes is not null)
        {
            try
            {
                if (bytes.Length > MaximumPayloadBytes) throw new JsonException();
                var entry = JsonSerializer.Deserialize<Entry<T>>(bytes, Json);
                var now = clock.GetUtcNow();
                if (entry?.Value is not null && entry.ValidUntil > now && entry.ValidUntil <= now + Lifetime)
                {
                    Count("hit", kind);
                    return entry.Value;
                }
            }
            catch (JsonException)
            {
                Count("invalid_payload", kind);
                logger.LogWarning(new EventId(2502, "CatalogCacheInvalidPayload"), "Ignored invalid catalog cache payload for {Kind}", kind);
            }
        }

        Count("miss", kind);
        // Anchor freshness BEFORE the DB read. A delayed stale refill must not receive a fresh 30s window.
        var validUntil = clock.GetUtcNow() + Lifetime;
        var value = await load(token); // SQL failures propagate; no negative caching.
        if (value is null || !readable || clock.GetUtcNow() >= validUntil) return value;
        bytes = JsonSerializer.SerializeToUtf8Bytes(new Entry<T>(validUntil, value), Json);
        if (bytes.Length > MaximumPayloadBytes) { Count("oversized", kind); return value; }
        try
        {
            await store.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpiration = validUntil }, token);
            Count("set", kind);
        }
        catch (Exception failure) when (IsCacheFailure(failure)) { Failure("set", kind, failure); }
        return value;
    }

    private async Task RemoveAsync(string key, string kind, CancellationToken token)
    {
        if (store is null) return;
        try { await store.RemoveAsync(key, token); Count("remove", kind); }
        catch (Exception failure) when (IsCacheFailure(failure)) { Failure("remove", kind, failure); }
    }

    // RedisTimeoutException derives from TimeoutException, not RedisException (StackExchange.Redis 2.7).
    private static bool IsCacheFailure(Exception failure) => failure is RedisException or RedisTimeoutException or System.Net.Sockets.SocketException;
    private static void Count(string outcome, string kind) => Operations.Add(1,
        new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("kind", kind));
    private void Failure(string operation, string kind, Exception failure)
    {
        Count(operation + "_failure", kind);
        // SDK exception messages can contain dependency endpoints/credentials. Log only classified type.
        logger.LogWarning(new EventId(2501, "CatalogCacheUnavailable"),
            "Catalog cache {Operation} failed for {Kind}: {FailureType}; PostgreSQL remains authoritative",
            operation, kind, failure.GetType().Name);
    }

    private sealed record Entry<T>(DateTimeOffset ValidUntil, T Value);
}
