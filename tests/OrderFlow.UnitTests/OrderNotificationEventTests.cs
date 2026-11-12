using System.Text;
using System.Text.Json.Nodes;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Notifications;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class OrderNotificationEventTests
{
    private static OutboxMessage Message() => new()
    {
        EventId = Guid.NewGuid(), EventType = "ordering.order-created", SchemaVersion = 1, AggregateType = "Order", Producer = "Ordering",
        AggregateId = Guid.NewGuid(), AggregateVersion = 1, CorrelationId = Guid.NewGuid(), CausationId = Guid.NewGuid(),
        Payload = "{}", OccurredAt = DateTimeOffset.UtcNow
    };
    [Theory]
    [InlineData("ordering.order-created", "PendingPayment")]
    [InlineData("ordering.order-confirmed", "Confirmed")]
    [InlineData("ordering.order-cancelled", "Cancelled")]
    [InlineData("ordering.order-expired", "Expired")]
    [InlineData("ordering.order-processing", "Processing")]
    [InlineData("ordering.order-shipped", "Shipped")]
    [InlineData("ordering.order-completed", "Completed")]
    public void Each_supported_event_maps_to_its_historical_state_and_one_recipient(string type, string status)
    {
        var message = Message(); var user = Guid.NewGuid(); message.EventType = type;
        message.Payload = System.Text.Json.JsonSerializer.Serialize(new { userId = user, orderId = message.AggregateId, version = 1, status });
        var body = EventEnvelope.Serialize(message);
        var parsed = OrderNotificationEvent.Parse(body, message.EventId.ToString("D"), type, "application/json");
        Assert.Equal(user, parsed.RecipientId); Assert.Equal(status, parsed.Status);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(body), parsed.Hash);
    }

    [Theory]
    [InlineData("old-schema")]
    [InlineData("future-schema")]
    [InlineData("envelope")]
    [InlineData("missing-type")]
    [InlineData("unknown-type")]
    [InlineData("message-id")]
    [InlineData("content-type")]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("empty-recipient")]
    [InlineData("trace")]
    [InlineData("duplicate-property")]
    [InlineData("utf8")]
    [InlineData("oversize")]
    public void Invalid_or_unsupported_messages_are_poison_without_silent_fallback(string fault)
    {
        var message = Message(); message.Payload = System.Text.Json.JsonSerializer.Serialize(new
            { userId = Guid.NewGuid(), orderId = message.AggregateId, version = 1, status = "PendingPayment" });
        var node = JsonNode.Parse(EventEnvelope.Serialize(message))!.AsObject();
        var id = message.EventId.ToString("D"); var content = "application/json";
        switch (fault)
        {
            case "old-schema": node["schemaVersion"] = 0; break;
            case "future-schema": node["schemaVersion"] = 2; break;
            case "envelope": node["envelopeVersion"] = 2; break;
            case "missing-type": node.Remove("eventType"); break;
            case "unknown-type": node["eventType"] = "ordering.unknown"; break;
            case "message-id": id = Guid.NewGuid().ToString("D"); break;
            case "content-type": content = "text/plain"; break;
            case "status": node["data"]!["status"] = "Confirmed"; break;
            case "version": node["data"]!["version"] = 2; break;
            case "empty-recipient": node["data"]!["userId"] = Guid.Empty; break;
            case "trace": node["traceParent"] = "bad"; break;
        }
        var type = fault == "unknown-type" ? "ordering.unknown" : fault == "missing-type" ? null : message.EventType;
        var body = Encoding.UTF8.GetBytes(node.ToJsonString());
        if (fault == "duplicate-property") body = Encoding.UTF8.GetBytes(node.ToJsonString().Replace("\"envelopeVersion\":1", "\"envelopeVersion\":1,\"envelopeVersion\":1", StringComparison.Ordinal));
        if (fault == "utf8") body = [0xff];
        if (fault == "oversize") body = new byte[OrderNotificationEvent.MaximumBytes + 1];
        Assert.Throws<NotificationMessageException>(() => OrderNotificationEvent.Parse(body, id, type, content));
    }
}
