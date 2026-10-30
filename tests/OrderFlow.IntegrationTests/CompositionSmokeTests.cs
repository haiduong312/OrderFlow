using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Worker;
using Xunit;

namespace OrderFlow.IntegrationTests;

public class CompositionSmokeTests
{
    [Fact]
    public async Task Api_liveness_returns_healthy_without_external_services()
    {
        await using var factory = new WebApplicationFactory<Api.Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/api/health/live", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Worker_starts_and_stops_without_external_services()
    {
        using var host = WorkerHost.Build(["--environment", Environments.Production]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());

        await host.StartAsync(timeout.Token);
        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);

        await host.StopAsync(timeout.Token);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
    }
}
