using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Realtime;

[CollectionDefinition("Realtime", DisableParallelization = true)]
public sealed class RealtimeCollection : ICollectionFixture<PostgresServer>, ICollectionFixture<RabbitMqServer>, ICollectionFixture<RedisServer>;
public sealed class RedisServer : IAsyncLifetime
{
    public const string Image = "redis:8.10.3-alpine@sha256:7958cfbb9d152f87dadfbc89f1b0b15938f0b81ff8982feb327476f4911935c3";
    private IContainer? container;
    public string ConnectionString => $"127.0.0.1:{container!.GetMappedPublicPort(6379)}";
    public async Task InitializeAsync()
    {
        container = new ContainerBuilder(Image).WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections")) .Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); await container.StartAsync(timeout.Token);
    }
    public async Task StopAsync() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await container!.StopAsync(timeout.Token); }
    public async Task StartAsync() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await container!.StartAsync(timeout.Token); }
    public async Task DisposeAsync() { if (container is not null) await container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)); }
}
