using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

internal static class OrderReservationRelease
{
    // Caller must already own the order lock. Never acquire another order lock after inventory.
    public static async Task ReleaseAsync(OrderFlowDbContext database, Order order, Guid? actorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null || order.Status is not (OrderStatus.Cancelled or OrderStatus.Expired))
            throw new InvalidOperationException("Reservation release requires a terminal order in the locking transaction.");
        var items = await database.OrderItems.AsNoTracking().Where(item => item.OrderId == order.Id).ToListAsync(cancellationToken);
        var balances = await InventoryLocking.LockAsync(database, items.Select(item => item.ProductId), cancellationToken);
        var reason = order.Status == OrderStatus.Expired ? ReservationReleaseReason.Expired : ReservationReleaseReason.Cancelled;
        foreach (var balance in balances)
        {
            var reservation = await database.InventoryReservations.FromSqlInterpolated($"""
                SELECT * FROM inventory."InventoryReservations"
                WHERE "OrderId" = {order.Id} AND "ProductId" = {balance.ProductId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("A pending order item has no reservation.");
            var item = items.Single(item => item.ProductId == balance.ProductId);
            if (reservation.Quantity != item.Quantity || reservation.CreatedAt != order.CreatedAt || reservation.ExpiresAt != order.ExpiresAt)
                throw new InvalidOperationException("A pending order has inconsistent reservation facts.");
            // A terminal Released reservation has no remaining stock to release. Consumed on a
            // PendingPayment order is an inconsistent payment state and must fail, never expire silently.
            if (reservation.Status == ReservationStatus.Released) continue;
            var movement = balance.Release(reservation, reason, actorId, now)
                ?? throw new InvalidOperationException("An active reservation was not released.");
            database.StockMovements.Add(movement);
            // Flush actual SQL without committing: later history/outbox failure rolls this back.
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
