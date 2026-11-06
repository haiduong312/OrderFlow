using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Inventory;

public enum StockMovementKind { Initialization, Receipt, Adjustment, Reservation, Release }
public enum StockActorKind { System, Admin, Customer }

public sealed class StockMovement
{
    public const string InitializationOperation = "InitializeInventory:v1";
    public const string StockChangeOperation = "ChangeStock:v1";
    public const string ReservationOperation = "ReserveStock:v1";
    public const string ReleaseOperation = "ReleaseStock:v1";
    private StockMovement() { }
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Operation { get; private set; } = "";
    public Guid OperationId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public StockMovementKind Kind { get; private set; }
    public StockActorKind ActorKind { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Reason { get; private set; } = "";
    public int OnHandDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public int OnHandBefore { get; private set; }
    public int ReservedBefore { get; private set; }
    public int OnHandAfter { get; private set; }
    public int ReservedAfter { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static string NormalizeReason(string? reason)
    {
        var value = reason?.Trim();
        return !string.IsNullOrEmpty(value) && value.Length <= 500
            ? value : throw Invalid("reason", "A reason of 1–500 characters after trimming is required.");
    }

    public static void ValidateCommand(Guid operationId, StockMovementKind kind, int delta, Guid actorId)
    {
        if (operationId == Guid.Empty) throw Invalid("operationId", "A nonempty operation UUID is required.");
        if (actorId == Guid.Empty) throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        if (kind is not (StockMovementKind.Receipt or StockMovementKind.Adjustment))
            throw Invalid("kind", "Only receipt and adjustment are supported here.");
        if (delta == 0 || (kind == StockMovementKind.Receipt && delta < 0))
            throw Invalid(kind == StockMovementKind.Receipt ? "quantity" : "delta", "Receipt quantity must be positive; adjustment delta must be nonzero.");
    }

    internal static StockMovement Change(Guid productId, Guid operationId, StockMovementKind kind, Guid actorId,
        string reason, int delta, int beforeOnHand, int reserved, int afterOnHand, long revision, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), ProductId = productId, Operation = StockChangeOperation, OperationId = operationId,
        Kind = kind, ActorKind = StockActorKind.Admin, ActorUserId = actorId, Reason = reason,
        OnHandDelta = delta, ReservedDelta = 0, OnHandBefore = beforeOnHand, ReservedBefore = reserved,
        OnHandAfter = afterOnHand, ReservedAfter = reserved, Revision = revision,
        OccurredAt = BusinessTime.ToUtcMicroseconds(now)
    };

    internal static StockMovement Reserve(InventoryReservation reservation, Guid actorId,
        int onHand, int reserved, long revision) => new()
    {
        Id = Guid.NewGuid(), ProductId = reservation.ProductId, Operation = ReservationOperation,
        OperationId = reservation.Id, OrderId = reservation.OrderId, ReservationId = reservation.Id,
        Kind = StockMovementKind.Reservation, ActorKind = StockActorKind.Customer, ActorUserId = actorId,
        Reason = "Stock reserved for checkout", OnHandDelta = 0, ReservedDelta = reservation.Quantity,
        OnHandBefore = onHand, OnHandAfter = onHand, ReservedBefore = reserved,
        ReservedAfter = reserved + reservation.Quantity, Revision = revision, OccurredAt = reservation.CreatedAt
    };

    internal static StockMovement Release(InventoryReservation reservation, Guid actorId,
        int onHand, int reserved, long revision) => new()
    {
        Id = Guid.NewGuid(), ProductId = reservation.ProductId, Operation = ReleaseOperation,
        OperationId = reservation.Id, OrderId = reservation.OrderId, ReservationId = reservation.Id,
        Kind = StockMovementKind.Release, ActorKind = StockActorKind.Customer, ActorUserId = actorId,
        Reason = reservation.ReleaseReason == ReservationReleaseReason.Expired ? "Reservation released: Expired" : "Reservation released: Cancelled",
        OnHandDelta = 0, ReservedDelta = -reservation.Quantity, OnHandBefore = onHand, OnHandAfter = onHand,
        ReservedBefore = reserved, ReservedAfter = reserved - reservation.Quantity, Revision = revision,
        OccurredAt = reservation.ReleasedAt!.Value
    };

    private static ApplicationFailureException Invalid(string field, string message) =>
        new(ApplicationError.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });
}
