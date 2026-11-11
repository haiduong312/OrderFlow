using System.Text;
using System.Text.Json;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class PaymentSettlementDomainTests
{
    private static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Consumption_decrements_both_quantities_and_terminal_reservation_cannot_release_or_consume_again()
    {
        var actor = Guid.NewGuid(); var product = Guid.NewGuid();
        var balance = InventoryBalance.Zero(product);
        balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 10, "Receipt", actor, Now);
        var reservation = InventoryReservation.Create(Guid.NewGuid(), product, 3, Now);
        balance.Reserve(reservation, actor);
        var movement = balance.Consume(reservation, Now.AddMinutes(1));
        Assert.Equal(7, balance.OnHand); Assert.Equal(0, balance.Reserved); Assert.Equal(7, balance.Available);
        Assert.Equal(-3, movement.OnHandDelta); Assert.Equal(-3, movement.ReservedDelta);
        Assert.Equal(StockMovement.ConsumptionOperation, movement.Operation); Assert.Equal(reservation.Id, movement.OperationId);
        Assert.Equal(ReservationStatus.Consumed, reservation.Status); Assert.Equal(Now.AddMinutes(1), reservation.ConsumedAt);
        Assert.Throws<InvalidOperationException>(() => balance.Consume(reservation, Now.AddMinutes(2)));
        Assert.Throws<OrderFlow.Application.Common.Errors.ApplicationFailureException>(() => balance.Release(reservation, ReservationReleaseReason.Expired, null, Now.AddMinutes(15)));
        Assert.Equal(7, balance.OnHand); Assert.Equal(0, balance.Reserved);
    }
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Confirmation_has_an_exclusive_deadline_and_system_history(int microseconds, bool eligible)
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), "PAY", "Product", UsdMoney.FromDecimal(10m), 1)], Now);
        var decision = order.ExpiresAt.AddTicks(microseconds * 10);
        if (eligible)
        {
            var history = order.ConfirmPayment(decision);
            Assert.Equal(OrderStatus.Confirmed, order.Status); Assert.Equal(OrderActorKind.System, history.ActorKind);
            Assert.Null(history.ActorUserId); Assert.Equal(OrderStatus.PendingPayment, history.FromStatus);
        }
        else Assert.Throws<OrderFlow.Application.Common.Errors.ApplicationFailureException>(() => order.ConfirmPayment(decision));
    }
    [Fact]
    public void Captured_callback_fences_submission_and_failure_or_pending_cannot_regress_it()
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), "PAY", "Product", UsdMoney.FromDecimal(10m), 1)], Now);
        var attempt = PaymentAttempt.Create(order, Now); attempt.Claim(Now);
        var capture = new GatewayPaymentResult(attempt.ProviderRequestKey, attempt.Id, order.Id, "txn", "10.00", "USD", GatewayPaymentState.Succeeded, "cap");
        attempt.ApplyCallback(capture, Now);
        attempt.Observe(capture with { State = GatewayPaymentState.Failed, CaptureId = null }, null, Now);
        attempt.ApplyCallback(capture with { State = GatewayPaymentState.Pending, CaptureId = null }, Now);
        Assert.Equal(PaymentAttemptStatus.Captured, attempt.Status); Assert.Equal(GatewayPaymentState.Succeeded, attempt.ObservedState);
        Assert.Equal("cap", attempt.ProviderCaptureId); Assert.Null(attempt.LeaseToken);
        Assert.Throws<InvalidOperationException>(() => attempt.Claim(Now));
        Assert.Equal(OrderStatus.PendingPayment, order.Status); // Payment facts do not implicitly fulfill an order.
    }
}
