using OrderFlow.Application.Messaging;

namespace OrderFlow.Worker.Messaging;

public static class NotificationConsumerSubscription
{
    public static Task<RabbitMqSubscription> StartAsync(RabbitMqSettings settings, NotificationConsumerOptions options,
        string queue, NotificationDeliveryHandler handler, CancellationToken token) =>
        RabbitMqSubscription.StartAsync(settings, options.Prefetch, queue, handler.HandleAsync, token);
}
