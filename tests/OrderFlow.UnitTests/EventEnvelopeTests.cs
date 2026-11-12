using System.Diagnostics;
using System.Text.Json;
using OrderFlow.Application.Messaging;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class EventEnvelopeTests
{
    private static OutboxMessage Message() => new()
    {
        EventId = Guid.NewGuid(), EventType = "ordering.order-created", SchemaVersion = 1,
        AggregateId = Guid.NewGuid(), AggregateVersion = 1, CorrelationId = Guid.NewGuid(), CausationId = Guid.NewGuid(),
        AggregateType = "Order", Producer = "Ordering", Payload = "{\"total\":\"12.50\",\"quantity\":2}",
        OccurredAt = new DateTimeOffset(2035,1,1,7,0,0,TimeSpan.FromHours(7))
    };
    [Fact]
    public void Stable_event_identity_payload_money_and_producer_trace_survive_delivery_metadata_changes()
    {
        using var activity = new Activity("checkout").SetIdFormat(ActivityIdFormat.W3C).Start();
        var message = Message(); var original = EventEnvelope.Serialize(message); var parent = message.TraceParent;
        activity.Stop();
        message.AttemptCount = 2; message.Status = OutboxStatus.InFlight;
        message.LeaseOwner = "new-worker"; message.LeaseToken = Guid.NewGuid();
        using var publish = new Activity("publish").SetIdFormat(ActivityIdFormat.W3C).Start();
        Assert.Equal(original, EventEnvelope.Serialize(message)); Assert.Equal(parent, message.TraceParent);
        using var doc = JsonDocument.Parse(original);
        Assert.Equal("12.50", doc.RootElement.GetProperty("data").GetProperty("total").GetString());
        Assert.Equal(TimeSpan.Zero, doc.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset);
        Assert.False(doc.RootElement.TryGetProperty("leaseOwner", out _));
    }
    [Theory]
    [InlineData("payload")]
    [InlineData("identity")]
    [InlineData("routing")]
    public void Invalid_events_fail_permanently_without_blind_broker_retry(string wrong)
    {
        var message = Message();
        if (wrong == "payload") message.Payload = "[]";
        else if (wrong == "identity") message.EventId = Guid.Empty;
        else message.EventType = "ordering.#";
        var error = Assert.Throws<EventPublishException>(() => EventEnvelope.Serialize(message));
        Assert.False(error.Retryable); Assert.False(error.Uncertain);
    }
}
