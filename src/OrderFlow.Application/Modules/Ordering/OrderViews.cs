using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Ordering;

internal static class OrderViews
{
    public static OrderExpiryInfo Expiry(Order order, DateTimeOffset now, bool customerView)
    {
        var expired = order.Status == OrderStatus.Expired ||
            (order.Status == OrderStatus.PendingPayment && BusinessTime.HasExpired(now, order.ExpiresAt));
        return new(now, expired ? "Expired" : order.Status.ToString(), expired,
            customerView && order.Status == OrderStatus.PendingPayment && !expired);
    }

    public static OrderResponse Checkout(Order order) => new(order.Id, order.Status.ToString(), order.Version,
        order.CreatedAt, order.ExpiresAt, order.Total.ToString(), Items(order.Items));

    public static OrderDetailResponse Detail(Order order, IEnumerable<OrderItem> items, IEnumerable<OrderStatusHistory> history,
        DateTimeOffset now, bool customerView) => new(order.Id, order.UserId, order.Status.ToString(), order.Version,
        order.CreatedAt, order.ExpiresAt, order.Total.ToString(), Items(items), Expiry(order, now, customerView),
        history.OrderBy(entry => entry.Version).Select(entry => new OrderStatusHistoryResponse(entry.Version,
            entry.FromStatus?.ToString(), entry.Status.ToString(), entry.ActorKind.ToString(), entry.ActorUserId,
            entry.OccurredAt, entry.Reason)).ToArray());

    private static OrderItemResponse[] Items(IEnumerable<OrderItem> items) => items
        .OrderBy(item => item.ProductId.ToString("N"), StringComparer.Ordinal)
        .Select(item => new OrderItemResponse(item.ProductId, item.Sku, item.Name, item.UnitPrice.ToString(), item.Quantity, item.LineTotal.ToString())).ToArray();
}
