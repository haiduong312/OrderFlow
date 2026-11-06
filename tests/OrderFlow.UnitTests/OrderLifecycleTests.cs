using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class OrderLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void System_expiry_requires_pending_and_due_and_appends_one_system_fact(int microseconds, bool due)
    {
        var order = Create();
        var history = order.Expire(order.ExpiresAt.AddTicks(microseconds * 10));
        Assert.Equal(due, history is not null);
        Assert.Equal(due ? 2 : 1, order.Version);
        if (due)
        {
            Assert.Equal(OrderActorKind.System, history!.ActorKind); Assert.Null(history.ActorUserId);
            Assert.Equal(OrderStatus.PendingPayment, history.FromStatus); Assert.Equal(OrderStatus.Expired, history.Status);
            Assert.Null(order.Expire(order.ExpiresAt.AddHours(1)));
            Assert.Equal(2, order.Version);
        }
        foreach (var status in Enum.GetValues<OrderStatus>().Where(status => status != OrderStatus.PendingPayment))
            Assert.False(OrderTransitions.CanExpire(status, order.ExpiresAt, order.ExpiresAt.AddDays(1)));
    }

    [Fact]
    public void System_can_release_expired_stock_but_cannot_impersonate_customer_cancellation()
    {
        var actor = Guid.NewGuid(); var product = Guid.NewGuid();
        var balance = InventoryBalance.Zero(product);
        balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 10, "Opening", actor, Now);
        var reservation = InventoryReservation.Create(Guid.NewGuid(), product, 3, Now);
        balance.Reserve(reservation, actor);
        Assert.Throws<ArgumentException>(() => balance.Release(reservation, ReservationReleaseReason.Cancelled, null, Now));
        var movement = balance.Release(reservation, ReservationReleaseReason.Expired, null, Now.AddMinutes(15))!;
        Assert.Equal(StockActorKind.System, movement.ActorKind); Assert.Null(movement.ActorUserId);
        Assert.Equal(10, balance.OnHand); Assert.Equal(0, balance.Reserved);
        Assert.Null(balance.Release(reservation, ReservationReleaseReason.Expired, null, Now.AddHours(1)));
    }

    [Fact]
    public void Only_the_three_forward_fulfillment_edges_are_legal()
    {
        var expected = new[] { (OrderStatus.Confirmed, OrderStatus.Processing), (OrderStatus.Processing, OrderStatus.Shipped),
            (OrderStatus.Shipped, OrderStatus.Completed) };
        foreach (var from in Enum.GetValues<OrderStatus>())
        foreach (var to in Enum.GetValues<OrderStatus>())
            Assert.Equal(expected.Contains((from, to)), OrderTransitions.CanAdvanceFulfillment(from, to));
    }

    [Theory]
    [InlineData(-1, OrderStatus.Cancelled)]
    [InlineData(0, OrderStatus.Expired)]
    [InlineData(1, OrderStatus.Expired)]
    public void Cancellation_obeys_the_microsecond_expiry_boundary(int microseconds, OrderStatus expected)
    {
        var order = Create();
        var time = order.ExpiresAt.AddTicks(microseconds * 10);
        var history = order.Cancel(order.UserId, time)!;
        Assert.Equal(expected, order.Status);
        Assert.Equal(2, order.Version);
        Assert.Equal(OrderStatus.PendingPayment, history.FromStatus);
        Assert.Equal(expected, history.Status);
        Assert.Equal(order.UserId, history.ActorUserId);
        Assert.Equal(OrderActorKind.Customer, history.ActorKind);
        Assert.Equal(time, history.OccurredAt);
        if (expected == OrderStatus.Cancelled)
        {
            Assert.Null(order.Cancel(order.UserId, order.ExpiresAt.AddHours(1)));
            Assert.Equal(2, order.Version);
        }
        else AssertFailure(() => order.Cancel(order.UserId, time), ApplicationError.OrderExpired);
    }

    [Fact]
    public void Owner_and_expected_version_are_server_rules_and_pending_cannot_be_fulfilled()
    {
        var order = Create();
        AssertFailure(() => order.Cancel(Guid.NewGuid(), Now), ApplicationError.NotFound);
        AssertFailure(() => order.AdvanceFulfillment(OrderStatus.Processing, 2, Guid.NewGuid(), Now, null), ApplicationError.VersionConflict);
        AssertFailure(() => order.AdvanceFulfillment(OrderStatus.Processing, 1, Guid.NewGuid(), Now, null), ApplicationError.StateConflict);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(1, order.Version);
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("1")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void Public_status_names_do_not_accept_enum_numbers_or_case_variants(string status) =>
        AssertFailure(() => OrderTransitions.ParseStatus(status), ApplicationError.ValidationFailed);

    [Fact]
    public void Release_changes_only_reserved_once_and_preserves_reason_time_and_links()
    {
        var product = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var balance = InventoryBalance.Zero(product);
        balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 10, "Opening", actor, Now);
        var reservation = InventoryReservation.Create(Guid.NewGuid(), product, 3, Now);
        balance.Reserve(reservation, actor);
        var time = Now.AddMinutes(1);
        var movement = balance.Release(reservation, ReservationReleaseReason.Cancelled, actor, time)!;
        Assert.Equal(10, balance.OnHand);
        Assert.Equal(0, balance.Reserved);
        Assert.Equal(3, balance.Revision);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(ReservationReleaseReason.Cancelled, reservation.ReleaseReason);
        Assert.Equal(time, reservation.ReleasedAt);
        Assert.Equal(StockMovementKind.Release, movement.Kind);
        Assert.Equal(reservation.Id, movement.OperationId);
        Assert.Equal(reservation.OrderId, movement.OrderId);
        Assert.Equal(-3, movement.ReservedDelta);
        Assert.Equal(0, movement.OnHandDelta);
        Assert.Null(balance.Release(reservation, ReservationReleaseReason.Cancelled, actor, time.AddHours(1)));
        Assert.Equal(3, balance.Revision);
        Assert.Equal(time, reservation.ReleasedAt);
    }

    private static Order Create() => Order.Create(Guid.NewGuid(), Guid.NewGuid(),
        [new(Guid.NewGuid(), "TEST", "Product", UsdMoney.FromDecimal(1m), 1)], Now);
    private static void AssertFailure(Action action, ApplicationError expected) =>
        Assert.Equal(expected, Assert.Throws<ApplicationFailureException>(action).Error);
}
