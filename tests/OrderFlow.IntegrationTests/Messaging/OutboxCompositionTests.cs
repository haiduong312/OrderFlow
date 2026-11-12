using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Application.Messaging;
using OrderFlow.Worker;
using OrderFlow.Worker.Messaging;
using Xunit;

namespace OrderFlow.IntegrationTests.Messaging;

public sealed class OutboxCompositionTests
{
    [Fact]
    public void Publisher_is_opt_in_requires_private_configuration_and_seed_suppresses_loop()
    {
        using var disabled = WorkerHost.Build(["--OrderExpiry:Enabled","false"]);
        Assert.Null(disabled.Services.GetService<IEventPublisher>()); Assert.Empty(disabled.Services.GetServices<IHostedService>());
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build(["--OutboxPublishing:Enabled","true"]));
        var args = new[] { "--OrderExpiry:Enabled","false","--OutboxPublishing:Enabled","true","--RabbitMq:Password",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
        using var enabled = WorkerHost.Build(args); Assert.Single(enabled.Services.GetServices<IHostedService>().OfType<OutboxPublisherWorker>());
        using var seed = WorkerHost.Build(args, runBackgroundJobs: false); Assert.Empty(seed.Services.GetServices<IHostedService>());
    }
}
