using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Application.Messaging;
using OrderFlow.Worker;
using OrderFlow.Worker.Messaging;
using Xunit;

namespace OrderFlow.IntegrationTests.Notifications;

public sealed class NotificationCompositionTests
{
    [Fact]
    public void Independent_consumer_flag_requires_credentials_bounded_options_and_seed_suppresses_jobs()
    {
        using var disabled = WorkerHost.Build(["--OrderExpiry:Enabled", "false"]); Assert.Empty(disabled.Services.GetServices<IHostedService>());
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build(["--Notifications:Enabled", "true"]));
        var args = new[] { "--OrderExpiry:Enabled", "false", "--Notifications:Enabled", "true", "--RabbitMq:Password", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
        using var consumer = WorkerHost.Build(args); Assert.Single(consumer.Services.GetServices<IHostedService>().OfType<NotificationConsumerWorker>());
        Assert.Null(consumer.Services.GetService<IEventPublisher>());
        using var seed = WorkerHost.Build(args, runBackgroundJobs: false); Assert.Empty(seed.Services.GetServices<IHostedService>());
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build([.. args, "--Notifications:Prefetch", "0"]));
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build([.. args, "--Notifications:ConsumersPerQueue", "5"]));
    }
}
