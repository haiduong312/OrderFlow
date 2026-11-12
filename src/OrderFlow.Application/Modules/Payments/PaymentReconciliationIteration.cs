using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed class PaymentReconciliationIteration(IServiceScopeFactory scopes, IPaymentGateway gateway, TimeProvider clock,
    ILogger<PaymentReconciliationIteration> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        PaymentAttempt claimed; Guid token;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(db, cancellationToken);
            var now = BusinessTime.GetUtcNow(clock);
            // Share the existing lease/counter with submission, including recovery of a claimed Queued attempt.
            var rows = await db.PaymentAttempts.FromSqlInterpolated($"""
                SELECT * FROM payments."PaymentAttempts"
                WHERE "Status" IN ('Queued','Pending','Unknown') AND "DeliveryCount" BETWEEN 1 AND 7
                  AND (NOT "NeedsAttention" OR "LastErrorCode" IN ('settlement_pending','gateway_response_mismatch'))
                  AND "NextAttemptAt" <= {now} AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            if (rows.Count == 0) return false;
            claimed = rows[0]; token = claimed.ClaimReconciliation(now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        GatewayPaymentResult? result = null; string? error = null; var retryable = true;
        try { result = await gateway.LookupAsync(claimed.ProviderRequestKey, cancellationToken); }
        catch (GatewayUnavailableException failure)
        {
            error = failure.Code; retryable = failure.Retryable;
            logger.LogWarning(new EventId(1711, "PaymentLookupUncertain"), "Lookup for attempt {AttemptId}: {Code}", claimed.Id, error);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(db, cancellationToken);
            // Trust the persisted intention, never a provider-returned order/attempt as the locking target.
            var order = await db.Orders.FromSqlInterpolated($"""
                SELECT * FROM ordering."Orders" WHERE "Id" = {claimed.OrderId} FOR UPDATE
                """).SingleAsync(cancellationToken);
            var decisionAt = BusinessTime.GetUtcNow(clock);
            if (result is not null) await PaymentSettlement.LockIdentitiesAsync(db, result, cancellationToken);
            var attempt = await db.PaymentAttempts.FromSqlInterpolated($"""
                SELECT * FROM payments."PaymentAttempts" WHERE "Id" = {claimed.Id} FOR UPDATE
                """).SingleAsync(cancellationToken);
            if (attempt.LeaseToken != token || attempt.Status == PaymentAttemptStatus.Captured) return true;
            string? invalid = null;
            if (result is not null)
            {
                attempt.RememberLookup(result, decisionAt);
                invalid = await new PaymentSettlement(db).SettleAsync(order, attempt, result,
                    new(Guid.NewGuid(), null, null), decisionAt, cancellationToken);
                if (invalid is not null) attempt.ReviewLookup(invalid, decisionAt);
                else attempt.CompleteLookup(BusinessTime.GetUtcNow(clock));
            }
            else if (!retryable) attempt.ReviewLookup(error ?? "lookup_rejected", decisionAt);
            else
            {
                attempt.Observe(null, error ?? "provider_payment_not_found", decisionAt);
                attempt.CompleteLookup(decisionAt);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (invalid is not null || attempt.NeedsAttention)
                logger.LogError(new EventId(1712, "PaymentReconciliationNeedsReview"),
                    "Attempt {AttemptId} needs review: {Code}", attempt.Id, attempt.LastErrorCode);
        }
        return true;
    }
}
