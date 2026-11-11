using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed class PaymentCallbackReceipt(OrderFlowDbContext database, TimeProvider clock, ILogger<PaymentCallbackReceipt> logger)
{
    // Signature and schema must be verified by the transport before entering this service.
    public async Task AcceptAsync(PaymentCallback callback, byte[] raw, CancellationToken cancellationToken)
    {
        var now = BusinessTime.GetUtcNow(clock);
        var hash = SHA256.HashData(raw);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var inserted = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments."PaymentEvents"
                ("Provider", "ProviderEventId", "RawBody", "PayloadHash", "ReferencedOrderId", "OccurredAt", "ReceivedAt", "Status")
            VALUES ({callback.Provider}, {callback.ProviderEventId}, {raw}, {hash}, {callback.Payment.OrderId}, {callback.OccurredAt}, {now}, 'Pending')
            ON CONFLICT ("Provider", "ProviderEventId") DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            var previous = await database.PaymentEvents.AsNoTracking().SingleAsync(e => e.Provider == callback.Provider
                && e.ProviderEventId == callback.ProviderEventId, cancellationToken);
            if (!previous.PayloadHash.AsSpan().SequenceEqual(hash) || !previous.RawBody.AsSpan().SequenceEqual(raw))
            {
                logger.LogError(new EventId(1601, "PaymentEventIdentityConflict"),
                    "Authenticated callback reused event {ProviderEventId} with different bytes", callback.ProviderEventId);
                throw new ApplicationFailureException(ApplicationError.IdempotencyConflict);
            }
        }
        else
        {
            database.ProcessedMessages.Add(new()
            {
                ConsumerName = PaymentCallback.Consumer, MessageId = callback.ProviderEventId,
                MessageType = "payments.simulator-callback", SchemaVersion = 1,
                Payload = Encoding.UTF8.GetString(raw), PayloadHash = hash,
                ReceivedAt = now, NextAttemptAt = now
            });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
