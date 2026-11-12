using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed record PaymentEvidence(Guid CauseId, Guid? ProviderEventId, DateTimeOffset? ProviderOccurredAt);

// Both authenticated callbacks and authenticated provider lookups use this exact validation/allocation path.
// Caller holds the Order, provider identity and attempt locks, in that order, in the same scoped transaction.
public sealed class PaymentSettlement(OrderFlowDbContext database)
{
    public static string? ShapeError(GatewayPaymentResult p) =>
        p.AttemptId == Guid.Empty || p.OrderId == Guid.Empty || !Identity(p.RequestKey, 80)
        || !Identity(p.TransactionId, 100) || !Identity(p.Currency, 3) || p.Currency.Length != 3
        || !UsdMoney.TryParse(p.Amount, out var amount) || amount.Value <= 0 || !Enum.IsDefined(p.State)
        || (p.State == GatewayPaymentState.Succeeded ? !Identity(p.CaptureId, 100) : p.CaptureId is not null)
            ? "gateway_response_malformed" : null;

    private static bool Identity(string? value, int length) => !string.IsNullOrEmpty(value)
        && value.Length <= length && value.All(c => c is >= '!' and <= '~');

    public static async Task LockIdentitiesAsync(OrderFlowDbContext database, GatewayPaymentResult payment, CancellationToken cancellationToken)
    {
        if (ShapeError(payment) is not null) return;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"merchant-transaction/" + payment.TransactionId}, 0))", cancellationToken);
        if (payment.CaptureId is { } capture)
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"merchant-capture/" + capture}, 0))", cancellationToken);
    }

    public async Task<string?> SettleAsync(Order? order, PaymentAttempt? attempt, GatewayPaymentResult payment,
        PaymentEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Settlement requires a locked transaction.");
        string? invalid = ShapeError(payment);
        if (invalid is not null) return invalid;
        invalid = order is null || attempt is null ? "unknown_payment_reference"
            : attempt.OrderId != order.Id || payment.OrderId != order.Id || payment.AttemptId != attempt.Id || attempt.Provider != "Simulator" || attempt.ProviderRequestKey != payment.RequestKey
                ? "payment_reference_mismatch"
            : payment.Currency != attempt.Currency || payment.Currency != order.Currency ? "unsupported_payment_currency"
            : attempt.Amount != order.Total ? "stored_payment_amount_mismatch"
            : attempt.ProviderTransactionId is not null && attempt.ProviderTransactionId != payment.TransactionId
                ? "provider_transaction_mismatch" : null;
        if (invalid is null && await database.PaymentAttempts.AnyAsync(a => a.Provider == "Simulator"
            && a.ProviderTransactionId == payment.TransactionId && a.Id != payment.AttemptId, cancellationToken))
            invalid = "provider_transaction_reused";
        if (invalid is null && payment.State != GatewayPaymentState.Succeeded
            && (!UsdMoney.TryParse(payment.Amount, out var nonCaptureAmount) || nonCaptureAmount != attempt!.Amount))
            invalid = "payment_amount_mismatch";
        var priorCapture = payment.CaptureId is null ? null : await database.PaymentCaptures.SingleOrDefaultAsync(
            c => c.Provider == "Simulator" && c.ProviderCaptureId == payment.CaptureId, cancellationToken);
        var amount = UsdMoney.TryParse(payment.Amount, out var parsed) ? parsed : throw new InvalidOperationException("Invalid persisted callback.");
        if (priorCapture is not null && (priorCapture.AttemptId != payment.AttemptId || priorCapture.OrderId != payment.OrderId
            || priorCapture.ProviderTransactionId != payment.TransactionId || priorCapture.Amount != amount || priorCapture.Currency != payment.Currency))
            invalid = "provider_capture_mismatch";
        if (invalid is not null) return invalid;
        await ApplyAsync(order!, attempt!, payment, evidence, priorCapture, amount, now, cancellationToken);
        return null;
    }

    private async Task ApplyAsync(Order order, PaymentAttempt attempt, GatewayPaymentResult payment, PaymentEvidence evidence, PaymentCapture? priorCapture,
        UsdMoney amount, DateTimeOffset now, CancellationToken cancellationToken)
    {
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
            database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-expired", now, evidence.CauseId));
        }
        if (order.Status == OrderStatus.Expired)
            await OrderReservationRelease.ReleaseAsync(database, order, null, now, cancellationToken);
        if (priorCapture is not null) return; // No second capture/refund/consumption; overdue expiry may still finalize.
        var allocated = amount == attempt.Amount && order.Status == OrderStatus.PendingPayment
            && !await database.PaymentCaptures.AnyAsync(c => c.OrderId == order.Id && c.Allocated, cancellationToken);
        var capture = new PaymentCapture
        {
            Id = Guid.NewGuid(), ProviderCaptureId = payment.CaptureId!, ProviderTransactionId = payment.TransactionId,
            AttemptId = attempt.Id, OrderId = order.Id, ProviderEventId = evidence.ProviderEventId, Source = evidence.ProviderEventId is null ? "ProviderLookup" : "Callback",
            Amount = amount, Allocated = allocated, ProviderOccurredAt = evidence.ProviderOccurredAt, RecordedAt = now
        };
        database.PaymentCaptures.Add(capture);
        attempt.ApplyCallback(payment, now);
        if (allocated)
        {
            await ConsumeAsync(order, now, cancellationToken);
            database.OrderStatusHistory.Add(order.ConfirmPayment(now));
            database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-confirmed", now, evidence.CauseId));
        }
        else
        {
            database.PaymentRefunds.Add(PaymentRefund.Create(capture,
                amount != attempt.Amount ? "amount_mismatch" : "order_ineligible_or_surplus", now));
            database.OutboxMessages.Add(FinancialEvent(capture, "payments.refund-pending", evidence.CauseId, now));
        }
        database.OutboxMessages.Add(FinancialEvent(capture, "payments.payment-captured", evidence.CauseId, now));
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
