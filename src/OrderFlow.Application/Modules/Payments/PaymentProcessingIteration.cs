using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

/// <summary>Claim, network I/O, observation: three independent units, no merchant transaction over HTTP.</summary>
public sealed class PaymentProcessingIteration(IServiceScopeFactory scopes, IPaymentGateway gateway, TimeProvider clock,
    ILogger<PaymentProcessingIteration> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        PaymentAttempt claimed; Guid token;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
            var now = BusinessTime.GetUtcNow(clock);
            var rows = await database.PaymentAttempts.FromSqlInterpolated($"""
                SELECT * FROM payments."PaymentAttempts"
                WHERE "Status" IN ('Queued','Pending','Unknown') AND NOT "NeedsAttention" AND "DeliveryCount" < 8
                  AND "NextAttemptAt" <= {now} AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            if (rows.Count == 0) return false;
            claimed = rows[0]; token = claimed.Claim(now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        GatewayPaymentResult? observation = null; string? error = null;
        try
        {
            if (claimed.Status != PaymentAttemptStatus.Queued)
                observation = await gateway.LookupAsync(claimed.ProviderRequestKey, cancellationToken);
            observation ??= await gateway.SubmitAsync(claimed.Request(), cancellationToken);
        }
        catch (GatewayUnavailableException failure)
        {
            error = failure.Code;
            logger.LogWarning(new EventId(1502, "PaymentGatewayUncertain"), "Gateway result uncertain for attempt {AttemptId}: {Code}", claimed.Id, error);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
            var attempt = await database.PaymentAttempts.FromSqlInterpolated($"""
                SELECT * FROM payments."PaymentAttempts" WHERE "Id" = {claimed.Id} FOR UPDATE
                """).SingleAsync(cancellationToken);
            // Fence completion by token, including an expired/reclaimed lease. A stale process
            // cannot overwrite observations stored by a newer owner. Provider keys fence money.
            if (attempt.LeaseToken != token) return true;
            attempt.Observe(observation, error, BusinessTime.GetUtcNow(clock));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (attempt.NeedsAttention)
                logger.LogWarning(new EventId(1504, "PaymentNeedsAttention"), "Attempt {AttemptId} requires attention: {Code}", attempt.Id, attempt.LastErrorCode);
        }
        return true;
    }
}
