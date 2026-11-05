using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Inventory;

public enum ReservationStatus { Active, Consumed, Released }

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
