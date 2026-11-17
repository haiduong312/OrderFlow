using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Observability;
using System.Diagnostics;

namespace OrderFlow.Application.Modules.Notifications;

public sealed class NotificationProcessor(OrderFlowDbContext database, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task ProcessAsync(OrderNotificationEvent message, CancellationToken cancellationToken)
    {
        var e = message.Envelope;
        using var operation = OrderFlowTelemetry.StartMessage("notifications.handle", ActivityKind.Consumer,
            e.TraceParent, e.TraceState, e.EventId, e.CorrelationId);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var now = BusinessTime.GetUtcNow(clock);
        // Unique INSERT arbitrates simultaneous duplicates, including a winner that later rolls back.
        // Never continue an aborted transaction after catching a uniqueness exception.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messaging."ProcessedMessages"
                ("ConsumerName","MessageId","MessageType","SchemaVersion","Payload","PayloadHash","ReceivedAt","Status","AttemptCount","NextAttemptAt")
            VALUES ({OrderNotificationEvent.ConsumerName},{e.EventId},{e.EventType},{e.SchemaVersion},CAST({message.RawJson} AS jsonb),
                {message.Hash},{now},'Pending',0,{now})
            ON CONFLICT ("ConsumerName","MessageId") DO NOTHING
            """, cancellationToken);
        var inbox = await database.ProcessedMessages.FromSqlInterpolated($"""
            SELECT * FROM messaging."ProcessedMessages"
            WHERE "ConsumerName"={OrderNotificationEvent.ConsumerName} AND "MessageId"={e.EventId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        if (inbox.MessageType != e.EventType || inbox.SchemaVersion != e.SchemaVersion
            || !CryptographicOperations.FixedTimeEquals(inbox.PayloadHash, message.Hash))
            throw new NotificationMessageException("notification_event_identity_conflict");
        if (inbox.Status == InboxStatus.Processed)
        {
            await transaction.CommitAsync(cancellationToken);
            operation.Complete();
            return;
        }
        if (inbox.Status != InboxStatus.Pending) throw new NotificationMessageException("notification_inbox_state_conflict");
        // Owner is authoritative in PostgreSQL. Historical events remain valid after later transitions.
        var order = await database.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == e.AggregateId, cancellationToken);
        var status = Enum.Parse<OrderStatus>(message.Status);
        if (order is null || order.UserId != message.RecipientId
            || !await database.OrderStatusHistory.AnyAsync(h => h.OrderId == e.AggregateId && h.Version == (int)e.AggregateVersion
                && h.Status == status, cancellationToken))
            throw new NotificationMessageException("notification_order_correlation_failed");
        var notification = new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = order.UserId, SourceEventId = e.EventId, Type = e.EventType,
            OrderId = order.Id, CreatedAt = now
        };
        database.Notifications.Add(notification);
        database.OutboxMessages.Add(new OutboxMessage
        {
            EventId = Guid.NewGuid(), EventType = "notifications.notification-created", SchemaVersion = 1, Producer = "Notifications",
            AggregateType = "Notification", AggregateId = notification.Id, AggregateVersion = 1,
            CorrelationId = e.CorrelationId, CausationId = e.EventId,
            TraceParent = operation.Id ?? e.TraceParent, TraceState = operation.TraceState ?? e.TraceState,
            OccurredAt = now, NextAttemptAt = now,
            Payload = JsonSerializer.Serialize(new { notificationId = notification.Id, notification.RecipientUserId,
                notification.SourceEventId, notification.Type, notification.OrderId, notification.CreatedAt }, Json)
        });
        if (e.EventType == "ordering.order-confirmed")
        {
            var recipient = await database.Users.Where(u => u.Id == order.UserId).Select(u => u.Email).SingleAsync(cancellationToken);
            var d = EmailDelivery.Confirmation(order, e.EventId, recipient, now);
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notifications."EmailDeliveries"
                    ("Id","SourceEventId","OrderId","RecipientUserId","RecipientEmail","FromEmail","Template","Subject","TextBody",
                     "Provider","ProviderKey","MessageId","RequestHash","Status","CreatedAt","UpdatedAt","NextAttemptAt",
                     "AttemptCount","AttemptsInCycle","ReviewRetryCount","OutcomeUncertain","LastErrorCode","LastFailureAt")
                VALUES ({d.Id},{d.SourceEventId},{d.OrderId},{d.RecipientUserId},{d.RecipientEmail},{d.FromEmail},{d.Template},{d.Subject},{d.TextBody},
                    {d.Provider},{d.ProviderKey},{d.MessageId},{d.RequestHash},{d.Status.ToString()},{now},{now},{now},0,0,0,false,{d.LastErrorCode},{d.LastFailureAt})
                ON CONFLICT ("OrderId","Template") DO NOTHING
                """, cancellationToken);
        }

        inbox.Status = InboxStatus.Processed; inbox.ProcessedAt = now; inbox.AttemptCount = 1;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        operation.Complete();
        // The RabbitMQ adapter ACKs only after this method returns from commit.
    }
}
