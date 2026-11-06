using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

public sealed class OrderLifecycleService(OrderFlowDbContext database, ICurrentUser currentUser, TimeProvider clock,
    ILogger<OrderLifecycleService> logger)
{
    public async Task<OrderDetailResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = UserId();
        try
        {
            var result = await CancelLockedAsync(id, userId, cancellationToken);
            // Expiry is a committed business outcome, not an exception that rolls back release.
            if (result.Expired) throw new ApplicationFailureException(ApplicationError.OrderExpired);
            return result.Response;
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            LogContention(failure);
            throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        }
    }

    public async Task<OrderDetailResponse> AdvanceAsync(Guid id, OrderTransitionRequest request, CancellationToken cancellationToken)
    {
        var actorId = UserId();
        var target = OrderTransitions.ParseStatus(request.TargetStatus);
        if (request.ExpectedVersion is not (>= 1)) throw CheckoutRequest.Invalid("expectedVersion", "A positive expected version is required.");
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
            var order = await OrderLocking.LockAsync(database, id, null, cancellationToken);
            var now = BusinessTime.GetUtcNow(clock);
            var history = order.AdvanceFulfillment(target, request.ExpectedVersion.Value, actorId, now, request.Reason);
            database.OrderStatusHistory.Add(history);
            var eventType = target switch
            {
                OrderStatus.Processing => "ordering.order-processing",
                OrderStatus.Shipped => "ordering.order-shipped",
                OrderStatus.Completed => "ordering.order-completed",
                _ => throw new InvalidOperationException("An invalid fulfillment target escaped the transition rules.")
            };
            database.OutboxMessages.Add(OrderEvents.Create(order, eventType, now, Guid.NewGuid()));
            await database.SaveChangesAsync(cancellationToken);
            var response = await DetailAsync(order, now, customerView: false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            LogContention(failure);
            throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        }
    }

    private async Task<(OrderDetailResponse Response, bool Expired)> CancelLockedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        // Ownership is in the locking SQL. A non-owner cannot lock/release someone else's stock.
        var order = await OrderLocking.LockAsync(database, id, ownerId, cancellationToken);
        var now = BusinessTime.GetUtcNow(clock); // Decision time AFTER the order lock, before inventory waits.
        var history = order.Cancel(ownerId, now);
        if (history is not null)
        {
            var items = await database.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
            var balances = await InventoryLocking.LockAsync(database, items.Select(item => item.ProductId), cancellationToken);
            var reason = order.Status == OrderStatus.Expired ? ReservationReleaseReason.Expired : ReservationReleaseReason.Cancelled;
            // Reuse the same product order for balance and reservation locks. Never acquire a
            // catalog/order lock later while holding inventory locks.
            foreach (var balance in balances)
            {
                var reservation = await database.InventoryReservations.FromSqlInterpolated($"""
                    SELECT * FROM inventory."InventoryReservations"
                    WHERE "OrderId" = {id} AND "ProductId" = {balance.ProductId} FOR UPDATE
                    """).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("A pending order item has no reservation.");
                var item = items.Single(item => item.ProductId == balance.ProductId);
                if (reservation.Status != ReservationStatus.Active || reservation.Quantity != item.Quantity
                    || reservation.CreatedAt != order.CreatedAt || reservation.ExpiresAt != order.ExpiresAt)
                    throw new InvalidOperationException("A pending order has inconsistent reservation facts.");
                var movement = balance.Release(reservation, reason, ownerId, now)
                    ?? throw new InvalidOperationException("An active reservation was not released.");
                database.StockMovements.Add(movement);
                // Each release is real SQL, still inside the same transaction. A later
                // reservation/history/outbox failure rolls every earlier effect back.
                await database.SaveChangesAsync(cancellationToken);
            }
            database.OrderStatusHistory.Add(history);
            database.OutboxMessages.Add(OrderEvents.Create(order,
                order.Status == OrderStatus.Expired ? "ordering.order-expired" : "ordering.order-cancelled", now, Guid.NewGuid()));
            await database.SaveChangesAsync(cancellationToken);
        }
        var response = await DetailAsync(order, now, customerView: true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (response, order.Status == OrderStatus.Expired);
    }

    private async Task<OrderDetailResponse> DetailAsync(Order order, DateTimeOffset now, bool customerView, CancellationToken cancellationToken)
    {
        var items = await database.OrderItems.AsNoTracking().Where(item => item.OrderId == order.Id).ToListAsync(cancellationToken);
        var history = await database.OrderStatusHistory.AsNoTracking().Where(entry => entry.OrderId == order.Id)
            .OrderBy(entry => entry.Version).ToListAsync(cancellationToken);
        return OrderViews.Detail(order, items, history, now, customerView);
    }

    private Guid UserId() => currentUser.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
    private void LogContention(Exception failure) => logger.LogWarning(new EventId(1301, "OrderTransitionContention"),
        "Order transition failed with PostgreSQL state {SqlState}", OrderDatabase.Contention(failure)!.SqlState);
}
