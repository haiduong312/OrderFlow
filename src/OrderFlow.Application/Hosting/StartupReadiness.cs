using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Observability;

namespace OrderFlow.Application.Hosting;

public static class StartupReadiness
{
    // Only startup probes retry. No business transaction/operation is implicitly retried.
    public static async Task WaitAsync(IServiceProvider services, IConfiguration configuration, CancellationToken token)
    {
        if (!configuration.GetValue("Startup:WaitForDependencies", false)) return;
        var readiness = services.GetRequiredService<IRuntimeReadiness>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("OrderFlow.Startup");
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if ((await readiness.CheckAsync(token)).Ready) return;
            logger.LogWarning(new EventId(2500, "StartupDependencyWait"), "Dependencies unavailable on startup attempt {Attempt}", attempt);
            if (attempt < 8) await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        throw new InvalidOperationException("Startup dependencies remained unavailable after eight bounded probes. Check schema and configured services.");
    }
}
