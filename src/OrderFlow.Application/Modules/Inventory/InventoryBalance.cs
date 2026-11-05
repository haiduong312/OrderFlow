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
