using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Inventory;
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
        // Bind provider identities across different orders as well. All settlement writers take
        // transaction identity before capture identity; these transaction locks never cross HTTP.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"merchant-transaction/" + payment.TransactionId}, 0))", cancellationToken);
        if (payment.CaptureId is { } captureIdentity)
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"merchant-capture/" + captureIdentity}, 0))", cancellationToken);
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

        string? invalid = order is null || attempt is null ? "unknown_payment_reference"
            : attempt.OrderId != order.Id || attempt.Provider != callback.Provider || attempt.ProviderRequestKey != payment.RequestKey
                ? "payment_reference_mismatch"
            : payment.Currency != attempt.Currency || payment.Currency != order.Currency ? "unsupported_payment_currency"
            : attempt.Amount != order.Total ? "stored_payment_amount_mismatch"
            : attempt.ProviderTransactionId is not null && attempt.ProviderTransactionId != payment.TransactionId
                ? "provider_transaction_mismatch" : null;
        if (invalid is null && await database.PaymentAttempts.AnyAsync(a => a.Provider == callback.Provider
            && a.ProviderTransactionId == payment.TransactionId && a.Id != payment.AttemptId, cancellationToken))
            invalid = "provider_transaction_reused";
        if (invalid is null && payment.State != GatewayPaymentState.Succeeded
            && (!UsdMoney.TryParse(payment.Amount, out var nonCaptureAmount) || nonCaptureAmount != attempt!.Amount))
            invalid = "payment_amount_mismatch";
        var priorCapture = payment.CaptureId is null ? null : await database.PaymentCaptures.SingleOrDefaultAsync(
            c => c.Provider == callback.Provider && c.ProviderCaptureId == payment.CaptureId, cancellationToken);
        var amount = UsdMoney.TryParse(payment.Amount, out var parsed) ? parsed : throw new InvalidOperationException("Invalid persisted callback.");
        if (priorCapture is not null && (priorCapture.AttemptId != payment.AttemptId || priorCapture.OrderId != payment.OrderId
            || priorCapture.ProviderTransactionId != payment.TransactionId || priorCapture.Amount != amount || priorCapture.Currency != payment.Currency))
            invalid = "provider_capture_mismatch";
        if (invalid is not null)
        {
            receipt.Status = PaymentEventStatus.Quarantined; receipt.ErrorCode = invalid; receipt.DecisionAt = now;
            inbox.Status = InboxStatus.Failed; inbox.LastErrorCode = invalid;
            logger.LogError(new EventId(1602, "PaymentCallbackQuarantined"),
                "Payment event {ProviderEventId} quarantined: {Code}", callback.ProviderEventId, invalid);
        }
        else
        {
            await ApplyAsync(order!, attempt!, callback, priorCapture, amount, now, cancellationToken);
            receipt.Status = PaymentEventStatus.Processed; receipt.DecisionAt = now;
            inbox.Status = InboxStatus.Processed; inbox.ProcessedAt = now; inbox.LastErrorCode = null;
        }
        inbox.AttemptCount++;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ApplyAsync(Order order, PaymentAttempt attempt, PaymentCallback callback, PaymentCapture? priorCapture,
        UsdMoney amount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payment = callback.Payment;
        if (payment.State != GatewayPaymentState.Succeeded)
        {
            if (amount != attempt.Amount)
                throw new InvalidOperationException("Non-capture amount mismatch must be quarantined before applying.");
            attempt.ApplyCallback(payment, now);
            return;
        }
        var expiredHistory = order.Expire(now);
        if (expiredHistory is not null)
        {
            database.OrderStatusHistory.Add(expiredHistory);
            database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-expired", now, callback.ProviderEventId));
        }
        if (order.Status == OrderStatus.Expired)
            await OrderReservationRelease.ReleaseAsync(database, order, null, now, cancellationToken);
        if (priorCapture is not null) return; // No second capture/refund/consumption; overdue expiry may still finalize.
        var allocated = amount == attempt.Amount && order.Status == OrderStatus.PendingPayment
            && !await database.PaymentCaptures.AnyAsync(c => c.OrderId == order.Id && c.Allocated, cancellationToken);
        var capture = new PaymentCapture
        {
            Id = Guid.NewGuid(), ProviderCaptureId = payment.CaptureId!, ProviderTransactionId = payment.TransactionId,
            AttemptId = attempt.Id, OrderId = order.Id, ProviderEventId = callback.ProviderEventId,
            Amount = amount, Allocated = allocated, ProviderOccurredAt = callback.OccurredAt, RecordedAt = now
        };
        database.PaymentCaptures.Add(capture);
        attempt.ApplyCallback(payment, now);
        if (allocated)
        {
            await ConsumeAsync(order, now, cancellationToken);
            database.OrderStatusHistory.Add(order.ConfirmPayment(now));
            database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-confirmed", now, callback.ProviderEventId));
        }
        else
        {
            var refundId = Guid.NewGuid();
            database.PaymentRefunds.Add(new()
            {
                Id = refundId, CaptureId = capture.Id, ProviderCaptureId = capture.ProviderCaptureId,
                ProviderRefundKey = "refund-" + refundId.ToString("N"), Amount = capture.Amount,
                Reason = amount != attempt.Amount ? "amount_mismatch" : "order_ineligible_or_surplus", CreatedAt = now
            });
            database.OutboxMessages.Add(FinancialEvent(capture, "payments.refund-pending", callback.ProviderEventId, now));
        }
        database.OutboxMessages.Add(FinancialEvent(capture, "payments.payment-captured", callback.ProviderEventId, now));
    }

    private async Task ConsumeAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var items = await database.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).ToListAsync(cancellationToken);
        var balances = await InventoryLocking.LockAsync(database, items.Select(i => i.ProductId), cancellationToken);
        foreach (var balance in balances)
        {
            var reservation = await database.InventoryReservations.FromSqlInterpolated($"""
                SELECT * FROM inventory."InventoryReservations"
                WHERE "OrderId" = {order.Id} AND "ProductId" = {balance.ProductId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("A pending order has no reservation.");
            if (reservation.Quantity != items.Single(i => i.ProductId == balance.ProductId).Quantity
                || reservation.CreatedAt != order.CreatedAt || reservation.ExpiresAt != order.ExpiresAt)
                throw new InvalidOperationException("Reservation does not match the order snapshot.");
            database.StockMovements.Add(balance.Consume(reservation, now));
            // Real earlier SQL is still rolled back if a later item or mandatory outbox fails.
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    private static OutboxMessage FinancialEvent(PaymentCapture capture, string type, Guid cause, DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(), EventType = type, SchemaVersion = 1, Producer = "Payments",
        AggregateType = "PaymentCapture", AggregateId = capture.Id, AggregateVersion = 1,
        CorrelationId = capture.OrderId, CausationId = cause, OccurredAt = now, NextAttemptAt = now,
        Payload = JsonSerializer.Serialize(new { captureId = capture.Id, capture.OrderId, capture.AttemptId,
            capture.ProviderCaptureId, amount = capture.Amount.ToString(), capture.Currency, capture.Allocated,
            providerOccurredAt = capture.ProviderOccurredAt, recordedAt = now }, GatewayJson.Options)
    };
}
