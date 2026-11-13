using Npgsql;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace OrderFlow.Api.Realtime;

public sealed class RealtimeDeliveryHandler(NotificationRealtimeDispatcher dispatcher, RealtimeSettings settings, ILogger<RealtimeDeliveryHandler> logger)
{
    public async Task HandleAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties properties, IChannel channel, ulong tag, CancellationToken token)
    {
        Guid? id = Guid.TryParse(properties.MessageId, out var parsed) ? parsed : null;
        try { await dispatcher.DispatchAsync(body, properties.MessageId, properties.Type, properties.ContentType, token); }
        catch (NotificationMessageException error)
        {
            logger.LogWarning(new EventId(2401, "RealtimePoison"), "API {Instance} rejected hint {EventId}: {Code}", settings.InstanceName, id, error.Code);
            await channel.BasicNackAsync(tag, false, false, token); return;
        }
        catch (Exception error) when (!token.IsCancellationRequested && (error is RedisException or OperationCanceledException
            || OrderDatabase.Contention(error) is not null || error is NpgsqlException { IsTransient: true }))
        {
            logger.LogWarning(new EventId(2402, "RealtimeDependencyFailure"), "API {Instance} hint {EventId} dependency unavailable; bounded retry then DLQ", settings.InstanceName, id);
            await channel.BasicNackAsync(tag, false, false, token); return;
        }
        await channel.BasicAckAsync(tag, false, token);
        logger.LogInformation(new EventId(2403, "RealtimeHintAttempted"), "API {Instance} attempted hint {EventId}; client receipt is not acknowledged", settings.InstanceName, id);
    }
}
