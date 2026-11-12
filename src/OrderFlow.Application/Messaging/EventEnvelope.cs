using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OrderFlow.Application.Messaging;

public interface IEventPublisher
{
    // Return only after mandatory routing and broker confirmation; never implies consumer completion.
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class EventPublishException(string code, bool retryable, bool uncertain = false)
    : Exception("Event publication did not complete reliably.")
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public bool Uncertain { get; } = uncertain;
}

public sealed record EventEnvelope(int EnvelopeVersion, Guid EventId, string EventType, int SchemaVersion,
    DateTimeOffset OccurredAt, string Producer, string AggregateType, Guid AggregateId, long AggregateVersion,
    Guid CorrelationId, Guid CausationId, string? TraceParent, string? TraceState, JsonElement Data)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static byte[] Serialize(OutboxMessage message)
    {
        if (message.EventId == Guid.Empty || message.AggregateId == Guid.Empty || message.CorrelationId == Guid.Empty
            || message.SchemaVersion < 1 || message.AggregateVersion < 1 || string.IsNullOrWhiteSpace(message.EventType)
            || Encoding.UTF8.GetByteCount(message.EventType) > 200
            || message.EventType.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
            || (message.TraceParent is not null && !ActivityContext.TryParse(message.TraceParent, message.TraceState, out _)))
            throw new EventPublishException("invalid_event_envelope", retryable: false);
        try
        {
            using var payload = JsonDocument.Parse(message.Payload, new() { MaxDepth = 64 });
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
                throw new EventPublishException("invalid_event_payload", retryable: false);
            return JsonSerializer.SerializeToUtf8Bytes(new EventEnvelope(1, message.EventId, message.EventType,
                message.SchemaVersion, message.OccurredAt.ToUniversalTime(), message.Producer, message.AggregateType,
                message.AggregateId, message.AggregateVersion, message.CorrelationId, message.CausationId,
                message.TraceParent, message.TraceState, payload.RootElement), Json);
        }
        catch (JsonException) { throw new EventPublishException("invalid_event_payload", retryable: false); }
    }
}
