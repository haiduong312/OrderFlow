using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderFlow.Worker;
using Xunit;

namespace OrderFlow.IntegrationTests;

public sealed class WorkerCompositionTests
{
    [Theory]
    [InlineData("--OrderExpiry:BatchSize", "0")]
    [InlineData("--OrderExpiry:BatchSize", "101")]
    [InlineData("--OrderExpiry:PollIntervalSeconds", "0")]
    [InlineData("--OrderExpiry:PollIntervalSeconds", "301")]
    public async Task Invalid_expiry_bounds_fail_startup_without_external_services(string name, string value)
    {
        using var host = WorkerHost.Build(["--OrderExpiry:Enabled", "false", name, value]);
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Only_the_worker_registers_the_scheduler_and_seed_mode_suppresses_it_explicitly()
    {
        using var normal = WorkerHost.Build(["--OrderExpiry:Enabled", "true"]);
        Assert.Single(normal.Services.GetServices<IHostedService>().OfType<OrderExpiryWorker>());
        using var seed = WorkerHost.Build(["--OrderExpiry:Enabled", "true"], runBackgroundJobs: false);
        Assert.Empty(seed.Services.GetServices<IHostedService>());
        using var disabled = WorkerHost.Build(["--OrderExpiry:Enabled", "false"]);
        Assert.Empty(disabled.Services.GetServices<IHostedService>());
    }
}
