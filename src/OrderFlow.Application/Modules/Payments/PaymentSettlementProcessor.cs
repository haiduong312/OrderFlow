using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

/// <summary>Database-only settlement. Fresh scoped context per call; no network and no lease across I/O.</summary>
public sealed class PaymentSettlementProcessor(OrderFlowDbContext database, TimeProvider clock,
    ILogger<PaymentSettlementProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var due = BusinessTime.GetUtcNow(clock);
        var candidate = await database.PaymentEvents.AsNoTracking().Where(e => e.Status == PaymentEventStatus.Pending
            && database.ProcessedMessages.Any(m => m.ConsumerName == PaymentCallback.Consumer && m.MessageId == e.ProviderEventId
                && m.Status == InboxStatus.Pending && m.AttemptCount < 8 && m.NextAttemptAt <= due))
            .OrderBy(e => e.ReceivedAt).ThenBy(e => e.ProviderEventId).FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return false;
        try { return await ProcessAsync(candidate, cancellationToken); }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            // The business transaction has been disposed/rolled back. Persist bounded retry metadata
            // separately; never let a failed contender overwrite an already processed inbox.
            database.ChangeTracker.Clear();
            var next = BusinessTime.GetUtcNow(clock).AddSeconds(5);
            var code = "postgres_" + OrderDatabase.Contention(failure)!.SqlState;
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE messaging."ProcessedMessages"
                SET "AttemptCount" = "AttemptCount" + 1, "NextAttemptAt" = {next}, "LastErrorCode" = {code},
                    "Status" = CASE WHEN "AttemptCount" + 1 >= 8 THEN 'Failed' ELSE 'Pending' END
                WHERE "ConsumerName" = {PaymentCallback.Consumer} AND "MessageId" = {candidate.ProviderEventId}
                    AND "Status" = 'Pending' AND "AttemptCount" < 8
                """, cancellationToken);
            logger.LogWarning(new EventId(1604, "PaymentSettlementContention"),
                "Payment event {ProviderEventId} postponed: {Code}", candidate.ProviderEventId, code);
            return true;
        }
    }

    private async Task<bool> ProcessAsync(PaymentEvent candidate, CancellationToken cancellationToken)
    {
        var callback = PaymentCallback.Parse(candidate.RawBody);
        var payment = callback.Payment;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        // This is the first business lock, shared with cancellation/expiry/initiation.
        var order = await database.Orders.FromSqlInterpolated($"""
            SELECT * FROM ordering."Orders" WHERE "Id" = {payment.OrderId} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        // Receipt/provider occurrence is NOT the allocation time. Freeze this decision even if inventory waits.
        var now = BusinessTime.GetUtcNow(clock);
        await PaymentSettlement.LockIdentitiesAsync(database, payment, cancellationToken);
        var attempt = await database.PaymentAttempts.FromSqlInterpolated($"""
            SELECT * FROM payments."PaymentAttempts" WHERE "Id" = {payment.AttemptId} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var receipt = await database.PaymentEvents.FromSqlInterpolated($"""
            SELECT * FROM payments."PaymentEvents"
            WHERE "Provider" = {callback.Provider} AND "ProviderEventId" = {callback.ProviderEventId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        if (receipt.Status != PaymentEventStatus.Pending) return true;
        var inbox = await database.ProcessedMessages.FromSqlInterpolated($"""
            SELECT * FROM messaging."ProcessedMessages"
            WHERE "ConsumerName" = {PaymentCallback.Consumer} AND "MessageId" = {callback.ProviderEventId} FOR UPDATE
            """).SingleAsync(cancellationToken);
        if (inbox.Status != InboxStatus.Pending) throw new InvalidOperationException("Payment receipt/inbox state disagrees.");

        var invalid = await new PaymentSettlement(database).SettleAsync(order, attempt, payment,
            new(callback.ProviderEventId, callback.ProviderEventId, callback.OccurredAt), now, cancellationToken);
        if (invalid is not null)
        {
            receipt.Status = PaymentEventStatus.Quarantined; receipt.ErrorCode = invalid; receipt.DecisionAt = now;
            inbox.Status = InboxStatus.Failed; inbox.LastErrorCode = invalid;
            logger.LogError(new EventId(1602, "PaymentCallbackQuarantined"),
                "Payment event {ProviderEventId} quarantined: {Code}", callback.ProviderEventId, invalid);
        }
        else
        {
            receipt.Status = PaymentEventStatus.Processed; receipt.DecisionAt = now;
            inbox.Status = InboxStatus.Processed; inbox.ProcessedAt = now; inbox.LastErrorCode = null;
        }
        inbox.AttemptCount++;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
