using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Persistence;
using System.Diagnostics;
using OrderFlow.Application.Observability;

namespace OrderFlow.Api.Realtime;

public sealed class NotificationRealtimeDispatcher(IServiceScopeFactory scopes, IHubContext<NotificationHub> hub)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, MaxDepth = 16 };
    public async Task DispatchAsync(ReadOnlyMemory<byte> bytes, string? messageId, string? messageType, string? contentType, CancellationToken token)
    {
        var e = Parse(bytes, messageId, messageType, contentType);
        using var operation = OrderFlowTelemetry.StartMessage("realtime.dispatch", ActivityKind.Consumer, e.TraceParent, e.TraceState, e.EventId, e.CorrelationId);
        Guid recipient; NotificationHint hint;
        // This scope ends before Redis/SignalR network I/O. Stored data determines every destination.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == e.NotificationId, token);
            if (row is null || e.RecipientUserId != row.RecipientUserId || e.SourceEventId != row.SourceEventId
                || e.OrderId != row.OrderId || e.Type != row.Type || e.CreatedAt != row.CreatedAt)
                throw new NotificationMessageException("realtime_notification_correlation_failed");
            recipient = row.RecipientUserId; hint = new(e.EventId, row.Id, row.OrderId);
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
        // Dispatch completion is never a durable client acknowledgement. Redis can lose hints on failure/reconnect.
        await hub.Clients.User(recipient.ToString("D")).SendAsync("NotificationAvailable", hint, budget.Token).WaitAsync(budget.Token);
        operation.Complete();
    }
    private sealed record CreatedEvent(Guid EventId, Guid NotificationId, Guid RecipientUserId, Guid SourceEventId,
        Guid OrderId, string? Type, DateTimeOffset CreatedAt, Guid CorrelationId, string? TraceParent, string? TraceState);
    private static CreatedEvent Parse(ReadOnlyMemory<byte> bytes, string? messageId, string? messageType, string? contentType)
    {
        if (bytes.Length is < 1 or > 65536 || contentType != "application/json") throw new NotificationMessageException("realtime_invalid_body");
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes.Span);
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 }); CheckDuplicates(document.RootElement);
            var e = document.RootElement.Deserialize<EventEnvelope>(Json) ?? throw new NotificationMessageException("realtime_invalid_envelope");
            if (e.EnvelopeVersion != 1 || e.SchemaVersion != 1 || e.EventType != "notifications.notification-created" || messageType != e.EventType
                || e.Producer != "Notifications" || e.AggregateType != "Notification" || e.AggregateVersion != 1
                || e.EventId == Guid.Empty || !Guid.TryParseExact(messageId, "D", out var id) || id != e.EventId || e.AggregateId == Guid.Empty
                || e.CorrelationId == Guid.Empty || e.CausationId == Guid.Empty || e.Data.ValueKind != JsonValueKind.Object
                || e.TraceState?.Length > 512 || e.TraceParent is not null && !ActivityContext.TryParse(e.TraceParent, e.TraceState, out _))
                throw new NotificationMessageException("realtime_unsupported_or_invalid_envelope");
            var data = e.Data;
            if (data.GetProperty("notificationId").GetGuid() != e.AggregateId || data.GetProperty("sourceEventId").GetGuid() != e.CausationId
                || data.GetProperty("createdAt").GetDateTimeOffset() != e.OccurredAt)
                throw new NotificationMessageException("realtime_invalid_notification_identity");
            return new(e.EventId, e.AggregateId, data.GetProperty("recipientUserId").GetGuid(), e.CausationId,
                data.GetProperty("orderId").GetGuid(), data.GetProperty("type").GetString(), e.OccurredAt, e.CorrelationId, e.TraceParent, e.TraceState);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or DecoderFallbackException)
        { throw new NotificationMessageException("realtime_malformed_payload"); }
    }
    private static void CheckDuplicates(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new NotificationMessageException("realtime_duplicate_property"); CheckDuplicates(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) CheckDuplicates(item);
    }
}
