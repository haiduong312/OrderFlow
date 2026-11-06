using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Application.Modules.Inventory;

public sealed class InventoryBalance
{
    private InventoryBalance() { }
    public Guid ProductId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public long Revision { get; private set; }
    public int Available => OnHand - Reserved;

    public static InventoryBalance Zero(Guid productId) => productId != Guid.Empty
        ? new() { ProductId = productId }
        : throw new ArgumentException("A product ID is required.", nameof(productId));

    public StockMovement Reserve(InventoryReservation reservation, Guid actorId)
    {
        if (reservation.ProductId != ProductId || reservation.Status != ReservationStatus.Active || actorId == Guid.Empty)
            throw new ArgumentException("An active reservation for this product and a customer actor are required.");
        if (reservation.Quantity > Available) throw new ApplicationFailureException(ApplicationError.InsufficientStock);
        if (Revision == long.MaxValue) throw new ApplicationFailureException(ApplicationError.StockLimitExceeded);
        var movement = StockMovement.Reserve(reservation, actorId, OnHand, Reserved, Revision + 1);
        Reserved += reservation.Quantity;
        Revision++;
        return movement;
    }

    public StockMovement? Release(InventoryReservation reservation, ReservationReleaseReason reason, Guid? actorId, DateTimeOffset now)
    {
        if (reservation.ProductId != ProductId || actorId == Guid.Empty
            || (actorId is null && reason != ReservationReleaseReason.Expired))
            throw new ArgumentException("A matching reservation and customer actor, or system expiry, are required.");
        if (reservation.Status == ReservationStatus.Released) return null;
        if (reservation.Status != ReservationStatus.Active) throw new ApplicationFailureException(ApplicationError.StateConflict);
        if (Reserved < reservation.Quantity) throw new InvalidOperationException("Reserved stock does not cover its active reservation.");
        if (Revision == long.MaxValue) throw new ApplicationFailureException(ApplicationError.StockLimitExceeded);
        reservation.Release(reason, now);
        var movement = StockMovement.Release(reservation, actorId, OnHand, Reserved, Revision + 1);
        Reserved -= reservation.Quantity;
        Revision++;
        return movement;
    }

    public StockMovement Change(Guid operationId, StockMovementKind kind, int delta, string? reason, Guid actorId, DateTimeOffset now)
    {
        StockMovement.ValidateCommand(operationId, kind, delta, actorId);
        var normalizedReason = StockMovement.NormalizeReason(reason);
        var next = (long)OnHand + delta;
        if (next < Reserved) throw new ApplicationFailureException(ApplicationError.InsufficientStock);
        if (next > int.MaxValue || Revision == long.MaxValue)
            throw new ApplicationFailureException(ApplicationError.StockLimitExceeded);
        var movement = StockMovement.Change(ProductId, operationId, kind, actorId, normalizedReason, delta,
            OnHand, Reserved, (int)next, Revision + 1, now);
        OnHand = (int)next;
        Revision++;
        return movement;
    }
}
