using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.Application.Messaging;

// One channel/consumer per subscription; dispatch concurrency one owns all ACK/NACK operations.
public sealed class RabbitMqSubscription : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly IConnection connection;
    private IChannel? channel;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => completion.Task;
    private RabbitMqSubscription(IConnection connection, CancellationToken token)
    { this.connection = connection; stop = CancellationTokenSource.CreateLinkedTokenSource(token); }

    public static async Task<RabbitMqSubscription> StartAsync(RabbitMqSettings settings, ushort prefetch,
        string queue, Func<ReadOnlyMemory<byte>, IReadOnlyBasicProperties, IChannel, ulong, CancellationToken, Task> handler, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var connection = await settings.Factory().CreateConnectionAsync(budget.Token);
        var subscription = new RabbitMqSubscription(connection, token);
        try
        {
            var channel = subscription.channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                    consumerDispatchConcurrency: 1), budget.Token);
            await RabbitMqTopology.DeclareAsync(channel, settings, budget.Token);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, cancellationToken: budget.Token);
            channel.ChannelShutdownAsync += (_, args) =>
            {
                if (subscription.stop.IsCancellationRequested) return Task.CompletedTask;
                subscription.completion.TrySetException(args.ReplyCode is 403 or 406
                    ? new InvalidOperationException("RabbitMQ consumer topology/permission requires review.")
                    : new IOException("RabbitMQ consumer broker channel closed."));
                return Task.CompletedTask;
            };
            channel.CallbackExceptionAsync += (_, args) => { subscription.Fail(args.Exception); return Task.CompletedTask; };
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.UnregisteredAsync += (_, _) =>
            {
                if (!subscription.stop.IsCancellationRequested) subscription.Fail(new IOException("RabbitMQ consumer cancelled by broker."));
                return Task.CompletedTask;
            };
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                try
                {
                    // Fully parse/use memory before callback returns; never retain RabbitMQ-owned buffers.
                    await handler(delivery.Body, delivery.BasicProperties, channel, delivery.DeliveryTag, subscription.stop.Token);
                }
                catch (OperationCanceledException) when (subscription.stop.IsCancellationRequested) { }
                catch (Exception failure) { subscription.Fail(failure); }
            };
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: budget.Token);
            return subscription; // Consume-ok received: subscription is ready.
        }
        catch { await subscription.DisposeAsync(); throw; }
    }

    private void Fail(Exception failure) { completion.TrySetException(failure); stop.Cancel(); }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { if (channel is not null) await channel.DisposeAsync(); }
        finally { await connection.DisposeAsync(); stop.Dispose(); }
    }
}
