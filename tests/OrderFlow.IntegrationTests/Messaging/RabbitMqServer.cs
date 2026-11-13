using OrderFlow.Application.Messaging;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker.Messaging;
using Xunit;

namespace OrderFlow.IntegrationTests.Messaging;

[CollectionDefinition("Messaging", DisableParallelization = true)]
public sealed class MessagingCollection : ICollectionFixture<PostgresServer>, ICollectionFixture<RabbitMqServer>;

public sealed class RabbitMqServer : IAsyncLifetime
{
    public const string Image = "rabbitmq:4.4.0-management@sha256:af6f50d38098d8bd52fef7f3fa85015dd8b2bf6def58e995bf584ae9ba0f6407";
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private IContainer? container;
    public async Task InitializeAsync()
    {
        container = new ContainerBuilder(Image)
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "orderflow_test")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", password)
            .WithEnvironment("RABBITMQ_DEFAULT_VHOST", "orderflow_test")
            .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2 +SDcpu 1 +SDio 1")
            .WithPortBinding(5672, true)
            // Root diagnostics before server startup can create a root-owned Erlang cookie.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")
                .UntilInternalTcpPortIsAvailable(5672))
            .Build();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await container.StartAsync(timeout.Token);
        }
        catch (Exception startupFailure)
        {
            var logs = await container.GetLogsAsync();
            await DisposeAsync();
            throw new InvalidOperationException("RabbitMQ integration prerequisite unavailable. No fake broker or success skip is used. "
                + (logs.Stdout + logs.Stderr).Replace(password, "[redacted]"), startupFailure);
        }
    }
    public RabbitMqSettings Settings(string prefix, int? port = null, int limit = 10000, int timeout = 5) => new()
    {
        HostName = container!.Hostname, Port = port ?? container.GetMappedPublicPort(5672), VirtualHost = "orderflow_test",
        UserName = "orderflow_test", Password = password, Prefix = prefix, RetryDelayMilliseconds = 200,
        QueueMaxLength = limit, PublishTimeoutSeconds = timeout
    };
    public async Task RestartAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await container!.StopAsync(timeout.Token); await container.StartAsync(timeout.Token);
    }
    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        container = null;
    }
}
