using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Worker;
using OrderFlow.Worker.Emails;
using Xunit;

namespace OrderFlow.IntegrationTests.Emails;
public sealed class EmailCompositionTests
{
    [Fact]
    public void Sending_is_opt_in_development_only_bounded_loopback_and_seed_suppresses_worker()
    {
        using var disabled = WorkerHost.Build(["--OrderExpiry:Enabled", "false"]); Assert.Null(disabled.Services.GetService<IEmailGateway>());
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build(["--environment", "Production", "--EmailSending:Enabled", "true"]));
        var args = new[] { "--environment", "Testing", "--OrderExpiry:Enabled", "false", "--EmailSending:Enabled", "true" };
        using var host = WorkerHost.Build(args); Assert.Single(host.Services.GetServices<IHostedService>().OfType<EmailSendingWorker>());
        using var seed = WorkerHost.Build(args, runBackgroundJobs: false); Assert.Empty(seed.Services.GetServices<IHostedService>());
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build([..args,"--EmailSending:Host","smtp.example.com"]));
        Assert.Throws<InvalidOperationException>(() => WorkerHost.Build([..args,"--EmailSending:TimeoutSeconds","30"]));
    }
}
