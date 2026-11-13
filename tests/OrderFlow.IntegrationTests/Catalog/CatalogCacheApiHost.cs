using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Http;
using OrderFlow.IntegrationTests.Persistence;

namespace OrderFlow.IntegrationTests.Catalog;

internal sealed class CatalogCacheApiHost : IAsyncDisposable
{
    public WebApplication App { get; }
    public HttpClient Client { get; private set; } = null!;
    public CatalogReadCounter Queries { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    private CatalogCacheApiHost(PostgresDatabase db, string redis, string prefix, TimeProvider clock, bool enabled)
    {
        App = Api.Program.Build(["--environment", "Development"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = db.ConnectionString, ["Urls"] = "http://127.0.0.1:0",
                ["Authentication:AllowInsecureHttp"] = "true", ["CatalogCache:Enabled"] = enabled.ToString(),
                ["CatalogCache:RedisConnectionString"] = redis, ["CatalogCache:RedisPrefix"] = prefix
            });
            builder.Services.AddSingleton(clock);
            builder.Services.AddDbContext<OrderFlowDbContext>(options => options.AddInterceptors(Queries));
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(Logs);
        });
    }

    public static async Task<CatalogCacheApiHost> StartAsync(PostgresDatabase db, string redis, string prefix, TimeProvider clock, bool enabled = true)
    {
        var host = new CatalogCacheApiHost(db, redis, prefix, clock, enabled);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.App.StartAsync(budget.Token);
            var address = host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            host.Client = new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(15) };
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    public async Task<T> CatalogAsync<T>(Func<CatalogService, Task<T>> action)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<CatalogService>());
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await App.StopAsync(budget.Token); } finally { await App.DisposeAsync(); }
    }
}

internal sealed class CatalogReadCounter : DbCommandInterceptor
{
    private int reads;
    public int Reads => Volatile.Read(ref reads);
    public bool RejectReads { get; set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref reads);
            if (RejectReads) throw new InvalidOperationException("Injected SQL read failure.");
        }
        return ValueTask.FromResult(result);
    }
}
