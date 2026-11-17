using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;
using RabbitMQ.Client;
using OrderFlow.Application.Observability;
using System.Diagnostics;

namespace OrderFlow.Worker.Messaging;

public sealed class NotificationDeliveryHandler(IServiceScopeFactory scopes, ILogger<NotificationDeliveryHandler> logger)
{
    public async Task HandleAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties properties,
        IChannel channel, ulong deliveryTag, CancellationToken token)
    {
        Guid? eventId = Guid.TryParse(properties.MessageId, out var parsedId) ? parsedId : null;
        string? traceId = null; Guid? correlationId = null;
        try
        {
            var message = OrderNotificationEvent.Parse(body, properties.MessageId, properties.Type, properties.ContentType);
            if (ActivityContext.TryParse(message.Envelope.TraceParent, message.Envelope.TraceState, out var context)) traceId = context.TraceId.ToString();
            correlationId = message.Envelope.CorrelationId;
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessAsync(message, token);
        }
        catch (NotificationMessageException failure)
        {
            OrderFlowTelemetry.ProcessingFailed(ProcessingComponent.Notifications, FailureKind.InvalidMessage);
            logger.LogWarning(new EventId(1901, "NotificationPoisonMessage"),
                "Notification event {EventId} delivery rejected with {Code}; trace {TraceId}", eventId, failure.Code, traceId);
            // Main -> finite delay -> retry-work -> parked dead. Never immediate requeue.
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: token);
            return;
        }
        catch (Exception failure) when (TransientDatabaseFailure(failure) && !token.IsCancellationRequested)
        {
            OrderFlowTelemetry.ProcessingFailed(ProcessingComponent.Notifications, FailureKind.Dependency);
            logger.LogWarning(new EventId(1902, "NotificationDatabaseRetry"),
                "Notification event {EventId} transaction failed transiently; trace {TraceId}; bounded retry", eventId, traceId);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: token);
            return;
        }
        catch (Exception failure) when (!token.IsCancellationRequested)
        {
            OrderFlowTelemetry.ProcessingFailed(ProcessingComponent.Notifications, FailureKind.Unexpected);
            logger.LogError(new EventId(2804, "NotificationHandlingFailed"), "Notification handling failed with {ExceptionType}; event {EventId}; trace {TraceId}", failure.GetType().Name, eventId, traceId);
            throw;
        }
        // Commit (including inbox + notification + outbox) has returned and scope is disposed.
        // Failure/shutdown here leaves broker delivery unacknowledged and safe to redeliver.
        try { await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: token); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            OrderFlowTelemetry.ProcessingFailed(ProcessingComponent.Notifications, FailureKind.Dependency);
            logger.LogWarning(new EventId(2805, "NotificationAcknowledgementFailed"),
                "ACK not confirmed for event {EventId}; trace {TraceId}; committed inbox remains safe to redeliver", eventId, traceId);
            throw;
        }
        logger.LogInformation(new EventId(1904, "NotificationDeliveryAcknowledged"),
            "Notification consumer acknowledged event {EventId}, correlation {CorrelationId}, trace {TraceId} after committed inbox", eventId, correlationId, traceId);
    }

    private static bool TransientDatabaseFailure(Exception error)
    {
        if (OrderDatabase.Contention(error) is not null) return true;
        if (error is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException && error.InnerException is { } inner) error = inner;
        return error is NpgsqlException { IsTransient: true };
    }
}
