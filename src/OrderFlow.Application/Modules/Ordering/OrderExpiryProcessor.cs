using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

/// <summary>One scoped unit of work; one order per transaction. No scheduling or network calls.</summary>
public sealed class OrderExpiryProcessor(OrderFlowDbContext database, TimeProvider clock)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var cutoff = BusinessTime.GetUtcNow(clock);
        // Materialize this exact SQL without LINQ composition: claim only ONE order, never a
        // batch of orders whose locks would be held while waiting for shared inventory rows.
        var candidates = await database.Orders.FromSqlInterpolated($"""
            SELECT * FROM ordering."Orders"
            WHERE "Status" = 'PendingPayment' AND "ExpiresAt" <= {cutoff}
            ORDER BY "ExpiresAt", "Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        if (candidates.Count == 0) return false;
        var order = candidates[0];
        database.Attach(order);
        // Recheck the fresh locked state using decision time AFTER acquiring the order lock.
        var now = BusinessTime.GetUtcNow(clock);
        var history = order.Expire(now);
        if (history is null) return false;
        await OrderReservationRelease.ReleaseAsync(database, order, null, now, cancellationToken);
        database.OrderStatusHistory.Add(history);
        database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-expired", now, Guid.NewGuid()));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
