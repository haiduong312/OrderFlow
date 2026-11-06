using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Simulator;
using OrderFlow.Worker;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

public sealed class SimulatorCompositionTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    public void Simulator_refuses_non_development_or_disabled_startup_without_external_services(string environment, bool enabled)
    {
        Assert.Throws<InvalidOperationException>(() => SimulatorHost.Build(["--environment", environment], builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Simulator:Enabled"] = enabled.ToString() })));
    }

    [Fact]
    public void Simulator_refuses_missing_secrets_and_public_bindings()
    {
        Assert.Throws<InvalidOperationException>(() => SimulatorHost.Build(["--environment", "Testing", "--Simulator:Enabled", "true"]));
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.Throws<InvalidOperationException>(() => SimulatorHost.Build(["--environment", "Testing"], builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Simulator:Enabled"] = "true", ["Simulator:ApiKey"] = key + "a", ["Simulator:ControlKey"] = key + "b",
                ["Simulator:CallbackSigningKey"] = key + "c", ["Urls"] = "http://0.0.0.0:5090"
            })));
    }

    [Fact]
    public void Payment_worker_is_opt_in_seed_suppressed_and_simulator_transport_is_development_only()
    {
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build(["--environment", "Production", "--PaymentProcessing:Enabled", "true"]));
        using var disabled = WorkerHost.Build(["--environment", "Production", "--OrderExpiry:Enabled", "false"]);
        Assert.Empty(disabled.Services.GetServices<IHostedService>());
        var args = new[] { "--environment", "Testing", "--PaymentProcessing:Enabled", "true", "--OrderExpiry:Enabled", "false",
            "--PaymentGateway:BaseUrl", "http://127.0.0.1:5090/", "--PaymentGateway:ApiKey", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
        using var enabled = WorkerHost.Build(args);
        Assert.Single(enabled.Services.GetServices<IHostedService>().OfType<PaymentWorker>());
        using var seed = WorkerHost.Build(args, runBackgroundJobs: false);
        Assert.Empty(seed.Services.GetServices<IHostedService>());
    }
}
