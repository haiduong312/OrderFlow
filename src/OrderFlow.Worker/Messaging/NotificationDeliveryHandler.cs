using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;
using RabbitMQ.Client;

namespace OrderFlow.Worker.Messaging;

public sealed class NotificationDeliveryHandler(IServiceScopeFactory scopes, ILogger<NotificationDeliveryHandler> logger)
{
    public async Task HandleAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties properties,
        IChannel channel, ulong deliveryTag, CancellationToken token)
    {
        Guid? eventId = Guid.TryParse(properties.MessageId, out var parsedId) ? parsedId : null;
        try
        {
            var message = OrderNotificationEvent.Parse(body, properties.MessageId, properties.Type, properties.ContentType);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessAsync(message, token);
        }
        catch (NotificationMessageException failure)
        {
            logger.LogWarning(new EventId(1901, "NotificationPoisonMessage"),
                "Notification event {EventId} delivery rejected with {Code}", eventId, failure.Code);
            // Main -> finite delay -> retry-work -> parked dead. Never immediate requeue.
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: token);
            return;
        }
        catch (Exception failure) when (TransientDatabaseFailure(failure) && !token.IsCancellationRequested)
        {
            logger.LogWarning(new EventId(1902, "NotificationDatabaseRetry"),
                "Notification event {EventId} transaction failed transiently; delivery enters bounded retry topology", eventId);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: token);
            return;
        }
        // Commit (including inbox + notification + outbox) has returned and scope is disposed.
        // Failure/shutdown here leaves broker delivery unacknowledged and safe to redeliver.
        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: token);
        logger.LogInformation(new EventId(1904, "NotificationDeliveryAcknowledged"),
            "Notification consumer acknowledged event {EventId} after committed inbox", eventId);
    }

    private static bool TransientDatabaseFailure(Exception error)
    {
        if (OrderDatabase.Contention(error) is not null) return true;
        if (error is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException && error.InnerException is { } inner) error = inner;
        return error is NpgsqlException { IsTransient: true };
    }
}
