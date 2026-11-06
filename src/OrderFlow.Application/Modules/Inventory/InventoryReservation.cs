using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Application.Modules.Inventory;

public enum ReservationStatus { Active, Consumed, Released }
public enum ReservationReleaseReason { Cancelled, Expired }

public sealed class InventoryReservation
{
    private InventoryReservation() { }
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public ReservationReleaseReason? ReleaseReason { get; private set; }

    internal bool Release(ReservationReleaseReason reason, DateTimeOffset now)
    {
        if (Status == ReservationStatus.Released) return false;
        if (Status != ReservationStatus.Active) throw new ApplicationFailureException(ApplicationError.StateConflict);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        var time = BusinessTime.ToUtcMicroseconds(now);
        if (time < CreatedAt) throw new InvalidOperationException("A release cannot precede reservation creation.");
        Status = ReservationStatus.Released;
        ReleasedAt = time;
        ReleaseReason = reason;
        return true;
    }

    public static InventoryReservation Create(Guid orderId, Guid productId, int quantity, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || productId == Guid.Empty || quantity is < 1 or > 1000)
            throw new ArgumentException("A reservation requires order/product IDs and quantity 1–1000.");
        var created = BusinessTime.ToUtcMicroseconds(now);
        return new()
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId, Quantity = quantity,
            Status = ReservationStatus.Active, CreatedAt = created, ExpiresAt = created.AddMinutes(15)
        };
    }
}
