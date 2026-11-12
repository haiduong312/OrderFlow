using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.Worker.Messaging;

// One channel/consumer per subscription; dispatch concurrency one owns all ACK/NACK operations.
public sealed class NotificationConsumerSubscription : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly IConnection connection;
    private IChannel? channel;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => completion.Task;
    private NotificationConsumerSubscription(IConnection connection, CancellationToken token)
    { this.connection = connection; stop = CancellationTokenSource.CreateLinkedTokenSource(token); }

    public static async Task<NotificationConsumerSubscription> StartAsync(RabbitMqSettings settings, NotificationConsumerOptions options,
        string queue, NotificationDeliveryHandler handler, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var connection = await settings.Factory().CreateConnectionAsync(budget.Token);
        var subscription = new NotificationConsumerSubscription(connection, token);
        try
        {
            var channel = subscription.channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                    consumerDispatchConcurrency: 1), budget.Token);
            await RabbitMqTopology.DeclareAsync(channel, settings, budget.Token);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.Prefetch, global: false, cancellationToken: budget.Token);
            channel.ChannelShutdownAsync += (_, args) =>
            {
                if (subscription.stop.IsCancellationRequested) return Task.CompletedTask;
                subscription.completion.TrySetException(args.ReplyCode is 403 or 406
                    ? new InvalidOperationException("Notification consumer topology/permission requires review.")
                    : new IOException("Notification consumer broker channel closed."));
                return Task.CompletedTask;
            };
            channel.CallbackExceptionAsync += (_, args) => { subscription.Fail(args.Exception); return Task.CompletedTask; };
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.UnregisteredAsync += (_, _) =>
            {
                if (!subscription.stop.IsCancellationRequested) subscription.Fail(new IOException("Notification consumer cancelled by broker."));
                return Task.CompletedTask;
            };
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                try
                {
                    // Fully parse/use memory before callback returns; never retain RabbitMQ-owned buffers.
                    await handler.HandleAsync(delivery.Body, delivery.BasicProperties, channel, delivery.DeliveryTag, subscription.stop.Token);
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
