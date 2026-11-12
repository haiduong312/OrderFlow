using System.IO;
using OrderFlow.Application.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Worker.Messaging;

// This semaphore protects ONLY the client's channel ownership/frame ordering, not durable business coordination.
public sealed class RabbitMqEventPublisher(RabbitMqSettings settings) : IEventPublisher, IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim channelGate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var body = EventEnvelope.Serialize(message);
        await channelGate.WaitAsync(cancellationToken);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(settings.PublishTimeoutSeconds));
            try
            {
                await EnsureConnectedAsync(budget.Token);
                var properties = new BasicProperties
                {
                    MessageId = message.EventId.ToString("D"), Type = message.EventType, CorrelationId = message.CorrelationId.ToString("D"),
                    ContentType = "application/json", ContentEncoding = "utf-8", Persistent = true,
                    Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                    Headers = new Dictionary<string, object?> { ["envelope-version"] = 1, ["schema-version"] = message.SchemaVersion }
                };
                if (message.TraceParent is not null) properties.Headers["traceparent"] = message.TraceParent;
                if (message.TraceState is not null) properties.Headers["tracestate"] = message.TraceState;
                // 7.2.2 tracking correlates basic.return as well as nack; awaiting this includes the confirm.
                await channel!.BasicPublishAsync(settings.Exchange, message.EventType, mandatory: true,
                    basicProperties: properties, body: body, cancellationToken: budget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { await ResetAsync(); throw new EventPublishException("broker_confirm_timeout", retryable: true, uncertain: true); }
            catch (PublishException failure)
            { await ResetAsync(); throw new EventPublishException(failure.IsReturn ? "broker_unroutable" : "broker_nack", retryable: true, uncertain: !failure.IsReturn); }
            catch (OperationInterruptedException failure)
            {
                await ResetAsync();
                var permanent = failure.ShutdownReason?.ReplyCode is 403 or 406;
                throw new EventPublishException(permanent ? "broker_topology_or_permission_error" : "broker_channel_closed", !permanent, uncertain: true);
            }
            catch (BrokerUnreachableException failure)
            {
                await ResetAsync();
                var permanent = failure.InnerException is AuthenticationFailureException
                    || failure.InnerException is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(e => e is AuthenticationFailureException);
                throw new EventPublishException(permanent ? "broker_authentication_failed" : "broker_unavailable", !permanent, uncertain: true);
            }
            catch (IOException) { await ResetAsync(); throw new EventPublishException("broker_connection_lost", retryable: true, uncertain: true); }
            catch (OperationCanceledException) { await ResetAsync(); throw; }
        }
        finally { channelGate.Release(); }
    }

    public async Task EnsureTopologyAsync(CancellationToken cancellationToken)
    {
        await channelGate.WaitAsync(cancellationToken);
        try { await EnsureConnectedAsync(cancellationToken); }
        finally { channelGate.Release(); }
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if (connection?.IsOpen == true && channel?.IsOpen == true) return;
        await ResetAsync();
        connection = await settings.Factory().CreateConnectionAsync(token);
        channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), token);
        await RabbitMqTopology.DeclareAsync(channel, settings, token);
    }

    private async Task ResetAsync()
    {
        // Disposal uses the client's bounded continuation/connection timeouts. Never reuse an uncertain channel.
        var oldChannel = channel; var oldConnection = connection; channel = null; connection = null;
        try { if (oldChannel is not null) await oldChannel.DisposeAsync().ConfigureAwait(false); }
        finally { if (oldConnection is not null) await oldConnection.DisposeAsync().ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        await channelGate.WaitAsync().ConfigureAwait(false);
        try { await ResetAsync().ConfigureAwait(false); }
        finally { channelGate.Release(); }
        channelGate.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
