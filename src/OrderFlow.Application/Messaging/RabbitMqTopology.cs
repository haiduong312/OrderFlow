using RabbitMQ.Client;
using OrderFlow.Application.Modules.Notifications;

namespace OrderFlow.Application.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, RabbitMqSettings settings, CancellationToken token)
    {
        // Finite path: work -> delay -> retry-work -> dead. No automatic cycle back to the first queue.
        foreach (var exchange in new[] { settings.Exchange, settings.RetryExchange, settings.RetryReadyExchange, settings.DeadExchange,
            settings.NotificationRetryExchange, settings.NotificationReadyExchange, settings.NotificationDeadExchange, settings.RealtimeRetryExchange, settings.RealtimeReadyExchange, settings.RealtimeDeadExchange })
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: token);
        await QueueAsync(channel, settings.DeadQueue, settings.DeadExchange, null, settings, token);
        await QueueAsync(channel, settings.RetryQueue, settings.RetryReadyExchange, settings.DeadExchange, settings, token);
        await QueueAsync(channel, settings.DelayQueue, settings.RetryExchange, settings.RetryReadyExchange, settings, token, delay: true);
        await QueueAsync(channel, settings.Queue, settings.Exchange, settings.RetryExchange, settings, token);
        await QueueAsync(channel, settings.NotificationDeadQueue, settings.NotificationDeadExchange, null, settings, token);
        await QueueAsync(channel, settings.NotificationRetryQueue, settings.NotificationReadyExchange, settings.NotificationDeadExchange, settings, token);
        await QueueAsync(channel, settings.NotificationDelayQueue, settings.NotificationRetryExchange, settings.NotificationReadyExchange, settings, token, delay: true);
        await QueueAsync(channel, settings.NotificationQueue, settings.Exchange, settings.NotificationRetryExchange, settings, token,
            bindings: OrderNotificationEvent.SupportedTypes.Keys.ToArray());
        await QueueAsync(channel, settings.RealtimeDeadQueue, settings.RealtimeDeadExchange, null, settings, token);
        await QueueAsync(channel, settings.RealtimeRetryQueue, settings.RealtimeReadyExchange, settings.RealtimeDeadExchange, settings, token);
        await QueueAsync(channel, settings.RealtimeDelayQueue, settings.RealtimeRetryExchange, settings.RealtimeReadyExchange, settings, token, delay: true);
        await QueueAsync(channel, settings.RealtimeQueue, settings.Exchange, settings.RealtimeRetryExchange, settings, token,
            bindings: ["notifications.notification-created"]);
    }

    private static async Task QueueAsync(IChannel channel, string queue, string exchange, string? deadExchange,
        RabbitMqSettings settings, CancellationToken token, bool delay = false, string[]? bindings = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum", ["x-overflow"] = "reject-publish", ["x-max-length"] = settings.QueueMaxLength,
            ["x-delivery-limit"] = 5
        };
        if (deadExchange is not null)
        {
            arguments["x-dead-letter-exchange"] = deadExchange;
            arguments["x-dead-letter-strategy"] = "at-least-once";
        }
        if (delay) arguments["x-message-ttl"] = settings.RetryDelayMilliseconds;
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: token);
        foreach (var binding in bindings ?? ["#"])
            await channel.QueueBindAsync(queue, exchange, binding, cancellationToken: token);
    }
}
