using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OrderFlow.IntegrationTests.Http;

public sealed class FoundationApiFactory(string environment = "Production") : WebApplicationFactory<Api.Program>
{
    public CapturingLoggerProvider Logs { get; } = new();
    public CancellationProbe Cancellation { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services =>
        {
            // These probe routes exist only in the test host, never in the shipped API.
            services.AddControllers().AddApplicationPart(typeof(FoundationTestController).Assembly);
            services.AddSingleton(Cancellation);
        });
    }
}

public sealed class CancellationProbe
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
