using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderFlow.Application.Messaging;

namespace OrderFlow.Application.Modules.Notifications;

public sealed class NotificationMessageException(string code) : Exception("Notification message requires review.")
{
    public string Code { get; } = code;
}

public sealed record OrderNotificationEvent(EventEnvelope Envelope, Guid RecipientId, string Status, string RawJson, byte[] Hash)
{
    public const string ConsumerName = "notifications.orders.v1";
    public const int MaximumBytes = 64 * 1024;
    public static IReadOnlyDictionary<string, string> SupportedTypes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ordering.order-created"] = "PendingPayment", ["ordering.order-confirmed"] = "Confirmed",
        ["ordering.order-cancelled"] = "Cancelled", ["ordering.order-expired"] = "Expired",
        ["ordering.order-processing"] = "Processing", ["ordering.order-shipped"] = "Shipped", ["ordering.order-completed"] = "Completed"
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, MaxDepth = 32 };

    public static OrderNotificationEvent Parse(ReadOnlyMemory<byte> body, string? messageId, string? messageType, string? contentType)
    {
        if (body.Length is < 1 or > MaximumBytes) throw new NotificationMessageException("notification_body_size");
        if (contentType != "application/json") throw new NotificationMessageException("notification_content_type");
        try
        {
            var raw = new UTF8Encoding(false, true).GetString(body.Span);
            using var document = JsonDocument.Parse(body, new() { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement);
            var e = document.RootElement.Deserialize<EventEnvelope>(Json) ?? throw new NotificationMessageException("notification_invalid_envelope");
            // No older schema exists yet. Never silently interpret an unknown envelope/payload version.
            if (e.EnvelopeVersion != 1 || e.SchemaVersion != 1) throw new NotificationMessageException("notification_unsupported_schema");
            if (!Guid.TryParseExact(messageId, "D", out var id) || id == Guid.Empty || id != e.EventId || messageType != e.EventType
                || e.AggregateId == Guid.Empty || e.CorrelationId == Guid.Empty || e.CausationId == Guid.Empty
                || string.IsNullOrWhiteSpace(e.EventType) || e.AggregateVersion is < 1 or > int.MaxValue || e.Producer != "Ordering" || e.AggregateType != "Order"
                || e.OccurredAt == default || e.Data.ValueKind != JsonValueKind.Object
                || e.TraceState?.Length > 512 || e.TraceParent is not null && !ActivityContext.TryParse(e.TraceParent, e.TraceState, out _))
                throw new NotificationMessageException("notification_invalid_envelope");
            if (!SupportedTypes.TryGetValue(e.EventType, out var status)) throw new NotificationMessageException("notification_unsupported_type");
            var data = e.Data;
            var userId = data.GetProperty("userId").GetGuid();
            if (userId == Guid.Empty || data.GetProperty("orderId").GetGuid() != e.AggregateId
                || data.GetProperty("version").GetInt64() != e.AggregateVersion || data.GetProperty("status").GetString() != status)
                throw new NotificationMessageException("notification_invalid_order_snapshot");
            return new(e with { Data = e.Data.Clone() }, userId, status, raw, SHA256.HashData(body.Span));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException
            or DecoderFallbackException or OverflowException)
        { throw new NotificationMessageException("notification_malformed_json"); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new NotificationMessageException("notification_duplicate_json_property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
