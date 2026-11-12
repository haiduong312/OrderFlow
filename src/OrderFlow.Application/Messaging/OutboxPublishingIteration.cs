using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Messaging;

public sealed class OutboxPublishingIteration(IServiceScopeFactory scopes, IEventPublisher publisher, TimeProvider clock,
    ILogger<OutboxPublishingIteration> logger)
{
    private readonly string owner = "publisher-" + Guid.NewGuid().ToString("N");

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        OutboxMessage claimed; Guid token;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(db, cancellationToken);
            var now = BusinessTime.GetUtcNow(clock);
            // Bound cleanup too. A crash on the eighth delivery must become visible after lease expiry.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH exhausted AS (
                    SELECT "EventId" FROM messaging."OutboxMessages"
                    WHERE "AttemptCount" >= 8 AND ("Status"='Pending' OR ("Status"='InFlight' AND "LeaseExpiresAt" <= {now}))
                    ORDER BY "NextAttemptAt", "EventId" LIMIT 20 FOR UPDATE SKIP LOCKED
                )
                UPDATE messaging."OutboxMessages" m SET "Status"='Failed', "LastErrorCode"='publisher_attempts_exhausted',
                    "LastFailureAt"={now}, "LeaseOwner"=NULL, "LeaseToken"=NULL, "LeaseExpiresAt"=NULL
                FROM exhausted e WHERE m."EventId"=e."EventId"
                """, cancellationToken);
            var rows = await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM messaging."OutboxMessages"
                WHERE "AttemptCount" < 8 AND (("Status"='Pending' AND "NextAttemptAt" <= {now})
                    OR ("Status"='InFlight' AND "LeaseExpiresAt" <= {now}))
                ORDER BY "NextAttemptAt", "EventId" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            if (rows.Count == 0) { await transaction.CommitAsync(cancellationToken); return false; }
            claimed = rows[0]; token = Guid.NewGuid();
            claimed.Status = OutboxStatus.InFlight; claimed.AttemptCount++;
            claimed.LeaseOwner = owner; claimed.LeaseToken = token; claimed.LeaseExpiresAt = now.AddSeconds(60);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        EventPublishException? failure = null;
        try { await publisher.PublishAsync(claimed, cancellationToken); }
        catch (EventPublishException error)
        {
            failure = error;
            logger.LogWarning(new EventId(1801, "OutboxPublishFailed"),
                "Event {EventId} attempt {AttemptCount} publish failure {Code}; uncertain {Uncertain}",
                claimed.EventId, claimed.AttemptCount, error.Code, error.Uncertain);
        }
        // No business transaction exists during network I/O. Completion itself is a guarded SQL write.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            var now = BusinessTime.GetUtcNow(clock);
            int changed;
            if (failure is null)
                changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE messaging."OutboxMessages" SET "Status"='Published', "PublishedAt"={now}, "LastErrorCode"=NULL,
                        "LeaseOwner"=NULL, "LeaseToken"=NULL, "LeaseExpiresAt"=NULL
                    WHERE "EventId"={claimed.EventId} AND "Status"='InFlight' AND "LeaseOwner"={owner}
                        AND "LeaseToken"={token} AND "LeaseExpiresAt" > {now}
                    """, cancellationToken);
            else
            {
                var status = !failure.Retryable || claimed.AttemptCount >= 8 ? "Failed" : "Pending";
                int[] delays = [5, 15, 30, 60, 120, 300, 600, 600];
                var next = now.AddSeconds(delays[Math.Clamp(claimed.AttemptCount - 1, 0, 7)]);
                changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE messaging."OutboxMessages" SET "Status"={status}, "NextAttemptAt"={next}, "LastErrorCode"={failure.Code},
                        "LastFailureAt"={now}, "LeaseOwner"=NULL, "LeaseToken"=NULL, "LeaseExpiresAt"=NULL
                    WHERE "EventId"={claimed.EventId} AND "Status"='InFlight' AND "LeaseOwner"={owner}
                        AND "LeaseToken"={token} AND "LeaseExpiresAt" > {now}
                    """, cancellationToken);
            }
            if (changed == 0)
                logger.LogWarning(new EventId(1802, "OutboxLeaseLost"), "Event {EventId} completion ignored after lease ownership expired/changed", claimed.EventId);
        }
        return true;
    }
}
