using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Ordering;

public enum OrderStatus { PendingPayment, Confirmed, Processing, Shipped, Completed, Cancelled, Expired }
public sealed record OrderItemSnapshot(Guid ProductId, string Sku, string Name, UsdMoney UnitPrice, int Quantity);

public sealed class Order
{
    private Order() { }
    private readonly List<OrderItem> items = [];
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CheckoutRequestId { get; private set; }
    public OrderStatus Status { get; private set; }
    public int Version { get; private set; }
    public string Currency { get; private set; } = "USD";
    public UsdMoney Total { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => items;

    public static Order Create(Guid userId, Guid requestId, IReadOnlyList<OrderItemSnapshot> snapshots, DateTimeOffset now)
    {
        if (userId == Guid.Empty || requestId == Guid.Empty) throw new ArgumentException("User and checkout request IDs are required.");
        if (snapshots.Count is < 1 or > 100 || snapshots.Select(item => item.ProductId).Distinct().Count() != snapshots.Count)
            throw CheckoutRequest.Invalid("items", "Choose 1–100 distinct products.");
        var order = new Order
        {
            Id = Guid.NewGuid(), UserId = userId, CheckoutRequestId = requestId,
            Status = OrderStatus.PendingPayment, Version = 1, CreatedAt = BusinessTime.ToUtcMicroseconds(now)
        };
        order.ExpiresAt = order.CreatedAt.AddMinutes(15);
        decimal total = 0;
        foreach (var snapshot in snapshots.OrderBy(item => item.ProductId.ToString("N"), StringComparer.Ordinal))
        {
            var item = OrderItem.Create(order.Id, snapshot);
            total += item.LineTotal.Value;
            if (total > UsdMoney.Maximum) throw CheckoutRequest.Invalid("items", "The order total exceeds the USD limit.");
            order.items.Add(item);
        }
        order.Total = UsdMoney.FromDecimal(total);
        return order;
    }
}

public sealed class OrderItem
{
    private OrderItem() { }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public UsdMoney UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public UsdMoney LineTotal { get; private set; }

    internal static OrderItem Create(Guid orderId, OrderItemSnapshot snapshot)
    {
        if (snapshot.ProductId == Guid.Empty || snapshot.Quantity is < 1 or > 1000)
            throw CheckoutRequest.Invalid("items", "Each item requires a product ID and quantity 1–1000.");
        if (snapshot.UnitPrice.Value <= 0 || string.IsNullOrWhiteSpace(snapshot.Sku) || snapshot.Sku.Length > 64
            || string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Length > 200)
            throw CheckoutRequest.Invalid("items", "A positive price and valid catalog snapshot are required.");
        var total = snapshot.UnitPrice.Value * snapshot.Quantity;
        if (total > UsdMoney.Maximum) throw CheckoutRequest.Invalid("items", "An item total exceeds the USD limit.");
        return new()
        {
            OrderId = orderId, ProductId = snapshot.ProductId, Sku = snapshot.Sku, Name = snapshot.Name,
            UnitPrice = snapshot.UnitPrice, Quantity = snapshot.Quantity, LineTotal = UsdMoney.FromDecimal(total)
        };
    }
}

public sealed class OrderHistory
{
    private OrderHistory() { }
    public Guid OrderId { get; private set; }
    public int Version { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }

    public static OrderHistory Created(Order order) => new()
    {
        OrderId = order.Id, Version = order.Version, Status = order.Status, ActorUserId = order.UserId,
        Reason = "Order created and stock reserved", OccurredAt = order.CreatedAt
    };
}
