using System.Text.Json;
using OrderFlow.Application.Messaging;

namespace OrderFlow.Application.Modules.Ordering;

internal static class OrderEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static OutboxMessage Create(Order order, string eventType, DateTimeOffset now, Guid causationId) => new()
    {
        EventId = Guid.NewGuid(), EventType = eventType, SchemaVersion = 1, Producer = "Ordering", AggregateType = "Order",
        AggregateId = order.Id, AggregateVersion = order.Version, CorrelationId = order.Id, CausationId = causationId,
        OccurredAt = now, NextAttemptAt = now,
        Payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id, userId = order.UserId, status = order.Status.ToString(), version = order.Version,
            currency = order.Currency, total = order.Total.ToString(), createdAt = order.CreatedAt.UtcDateTime,
            expiresAt = order.ExpiresAt.UtcDateTime
        }, Json)
    };
}
