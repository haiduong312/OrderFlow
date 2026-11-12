using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker.Emails;
using Xunit;

namespace OrderFlow.IntegrationTests.Emails;

[CollectionDefinition("Email", DisableParallelization = true)]
public sealed class EmailCollection : ICollectionFixture<PostgresServer>, ICollectionFixture<MailpitServer>;
public sealed class MailpitServer : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.33.1@sha256:aef2aacc11f075ec0fb6874275ee4a421e688e11a96ebb296134d1735c7761ce";
    private IContainer? container;
    public async Task InitializeAsync()
    {
        container = new ContainerBuilder(Image).WithPortBinding(1025, true).WithPortBinding(8025, true)
            .WithEnvironment("MP_MAX_MESSAGES", "0").WithEnvironment("MP_IGNORE_DUPLICATE_IDS", "false")
            .WithEnvironment("MP_QUIET", "true").WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/readyz"))).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); await container.StartAsync(timeout.Token);
    }
    public DevelopmentSmtpEmailGateway Gateway() => new(new() { Host = "127.0.0.1", Port = container!.GetMappedPublicPort(1025), TimeoutSeconds = 1 });
    public async Task<int> CountAsync(string recipient)
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{container!.GetMappedPublicPort(8025)}"), Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync("/api/v1/search?query=" + Uri.EscapeDataString("to:" + recipient)); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("messages_count").GetInt32();
    }
    public async Task StopAsync() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await container!.StopAsync(timeout.Token); }
    public async Task StartAsync() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await container!.StartAsync(timeout.Token); }
    public async Task DisposeAsync() { if (container is not null) await container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
}
