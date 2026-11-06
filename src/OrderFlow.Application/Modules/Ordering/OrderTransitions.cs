using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Ordering;

public static class OrderTransitions
{
    public static bool CanAdvanceFulfillment(OrderStatus from, OrderStatus to) => (from, to) is
        (OrderStatus.Confirmed, OrderStatus.Processing) or
        (OrderStatus.Processing, OrderStatus.Shipped) or
        (OrderStatus.Shipped, OrderStatus.Completed);

    public static OrderStatus? CancellationTarget(OrderStatus current, DateTimeOffset expiresAt, DateTimeOffset now) => current switch
    {
        OrderStatus.Cancelled => null,
        OrderStatus.Expired => throw new ApplicationFailureException(ApplicationError.OrderExpired),
        OrderStatus.PendingPayment => BusinessTime.HasExpired(now, expiresAt) ? OrderStatus.Expired : OrderStatus.Cancelled,
        _ => throw new ApplicationFailureException(ApplicationError.StateConflict)
    };

    public static OrderStatus ParseStatus(string value)
    {
        // Enum.TryParse alone also accepts numeric/case variants that are not public enum names.
        if (!Enum.GetNames<OrderStatus>().Contains(value, StringComparer.Ordinal))
            throw CheckoutRequest.Invalid("status", "Use an exact supported order status name.");
        return Enum.Parse<OrderStatus>(value);
    }

    internal static string FulfillmentReason(string? reason)
    {
        if (reason?.Length > 500) throw CheckoutRequest.Invalid("reason", "Reason must not exceed 500 characters.");
        return string.IsNullOrWhiteSpace(reason) ? "Admin advanced fulfillment" : reason.Trim();
    }
}
