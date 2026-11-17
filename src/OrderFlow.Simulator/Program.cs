using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Simulator;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args is ["healthcheck"])
        {
            try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; using var response = await client.GetAsync("http://127.0.0.1:8090/health/ready"); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { Environment.ExitCode = 1; }
            return;
        }
        await using var app = SimulatorHost.Build(args is ["migrate"] ? [] : args);
        if (args is ["migrate"])
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>().Database.MigrateAsync(budget.Token);
            return;
        }
        await app.RunAsync();
    }
}
