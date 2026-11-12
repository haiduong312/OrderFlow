using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed class RefundProcessingIteration(IServiceScopeFactory scopes, IPaymentGateway gateway, TimeProvider clock,
    ILogger<RefundProcessingIteration> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        PaymentRefund claimed; Guid token;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(db, cancellationToken);
            var now = BusinessTime.GetUtcNow(clock);
            var rows = await db.PaymentRefunds.FromSqlInterpolated($"""
                SELECT * FROM payments."Refunds"
                WHERE "Status" = 'Pending' AND "DeliveryCount" < 8 AND "NextAttemptAt" <= {now}
                  AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            if (rows.Count == 0) return false;
            claimed = rows[0]; token = claimed.Claim(now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Both lookup and submission run after the claim scope/transaction is disposed.
        GatewayRefundResult? result = null; string? error = null; var retryable = true;
        try
        {
            result = await gateway.LookupRefundAsync(claimed.ProviderRefundKey, cancellationToken)
                ?? await gateway.RefundAsync(claimed.Request(), cancellationToken);
        }
        catch (GatewayUnavailableException failure)
        {
            error = failure.Code; retryable = failure.Retryable;
            logger.LogWarning(new EventId(1701, "RefundGatewayUncertain"), "Refund {RefundId} gateway outcome: {Code}", claimed.Id, error);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(db, cancellationToken);
            var refund = await db.PaymentRefunds.FromSqlInterpolated($"""
                SELECT * FROM payments."Refunds" WHERE "Id" = {claimed.Id} FOR UPDATE
                """).SingleAsync(cancellationToken);
            if (refund.LeaseToken != token || refund.Status == PaymentRefundStatus.Succeeded) return true;
            if (result is not null && await db.PaymentRefunds.AnyAsync(r => r.Id != refund.Id
                && r.Provider == refund.Provider && r.ProviderRefundId == result.RefundId, cancellationToken))
            { result = null; error = "provider_refund_identity_reused"; retryable = false; }
            var now = BusinessTime.GetUtcNow(clock);
            refund.Observe(result, error, retryable, now);
            if (refund.Status == PaymentRefundStatus.Succeeded)
                db.OutboxMessages.Add(new OutboxMessage
                {
                    EventId = Guid.NewGuid(), EventType = "payments.refund-succeeded", SchemaVersion = 1,
                    Producer = "Payments", AggregateType = "Refund", AggregateId = refund.Id, AggregateVersion = 1,
                    CorrelationId = refund.CaptureId, CausationId = refund.Id, OccurredAt = now, NextAttemptAt = now,
                    Payload = JsonSerializer.Serialize(new { refundId = refund.Id, refund.CaptureId, refund.ProviderCaptureId,
                        refund.ProviderRefundId, amount = refund.Amount.ToString(), refund.Currency }, GatewayJson.Options)
                });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (refund.Status == PaymentRefundStatus.NeedsReview)
                logger.LogError(new EventId(1702, "RefundNeedsReview"), "Refund {RefundId} needs review: {Code}", refund.Id, refund.LastErrorCode);
        }
        return true;
    }
}
