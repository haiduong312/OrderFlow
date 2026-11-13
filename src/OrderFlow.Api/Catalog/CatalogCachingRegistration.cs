using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using OrderFlow.Application.Modules.Catalog;
using StackExchange.Redis;

namespace OrderFlow.Api.Catalog;

public sealed class CatalogCacheSettings
{
    public bool Enabled { get; init; }
    public string RedisConnectionString { get; init; } = "";
    public string RedisPrefix { get; init; } = "orderflow:catalog:";

    public ConfigurationOptions RedisOptions()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString) || RedisPrefix.Length is < 1 or > 100
            || RedisPrefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not (':' or '-' or '_')))
            throw new InvalidOperationException("Enabled CatalogCache requires a Redis connection and a 1–100 character ASCII RedisPrefix (letters/digits/:/-/_).");
        ConfigurationOptions options;
        try { options = ConfigurationOptions.Parse(RedisConnectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("CatalogCache Redis configuration is invalid."); }
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 0;
        options.ConnectTimeout = 250;
        options.AsyncTimeout = 250;
        options.SyncTimeout = 250;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        return options;
    }
}

public static class CatalogCachingRegistration
{
    public static IServiceCollection AddCatalogCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var settings = configuration.GetSection("CatalogCache").Get<CatalogCacheSettings>() ?? new();
            if (settings.Enabled) _ = settings.RedisOptions();
            return settings;
        });
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<CatalogCacheSettings>((options, settings) =>
        {
            if (!settings.Enabled) return;
            options.ConfigurationOptions = settings.RedisOptions();
            options.InstanceName = settings.RedisPrefix;
        });
        services.AddSingleton(provider => new CatalogCache(
            provider.GetRequiredService<CatalogCacheSettings>().Enabled ? provider.GetRequiredService<IDistributedCache>() : null,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<CatalogCache>>()));
        return services;
    }
}
