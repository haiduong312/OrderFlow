using System.Security.Cryptography;
using System.Text;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class CheckoutDomainTests
{
    private static readonly Guid One = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Two = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Canonical_body_sorts_products_and_hashes_only_versioned_request_facts()
    {
        var one = CheckoutRequest.Normalize(Request((Two, 2), (One, 1)));
        var reordered = CheckoutRequest.Normalize(Request((One, 1), (Two, 2)));
        const string expected = "{\"version\":1,\"items\":[{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"quantity\":1},{\"productId\":\"00000000-0000-0000-0000-000000000002\",\"quantity\":2}]}";
        Assert.Equal(expected, one.CanonicalBody);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), one.Hash);
        Assert.Equal(one.Hash, reordered.Hash);
        Assert.NotEqual(one.CanonicalBody, CheckoutRequest.Normalize(Request((One, 2), (Two, 2))).CanonicalBody);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Quantity_bounds_are_enforced_before_persistence(int quantity) =>
        AssertInvalid(() => CheckoutRequest.Normalize(Request((One, quantity))));

    [Fact]
    public void Empty_invalid_excessive_and_duplicate_items_are_rejected_instead_of_merged()
    {
        AssertInvalid(() => CheckoutRequest.Normalize(new()));
        AssertInvalid(() => CheckoutRequest.Normalize(Request()));
        AssertInvalid(() => CheckoutRequest.Normalize(new() { Items = [null] }));
        AssertInvalid(() => CheckoutRequest.Normalize(Request((Guid.Empty, 1))));
        AssertInvalid(() => CheckoutRequest.Normalize(Request((One, 1), (One, 1))));
        AssertInvalid(() => CheckoutRequest.Normalize(Request(Enumerable.Range(0, 101).Select(_ => (Guid.NewGuid(), 1)).ToArray())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("has space")]
    [InlineData("non-ascii-é")]
    public void Invalid_keys_are_not_trimmed_or_reinterpreted(string? key) => AssertInvalid(() => CheckoutRequest.ValidateKey(key));

    [Fact]
    public void Key_length_and_case_are_preserved()
    {
        AssertInvalid(() => CheckoutRequest.ValidateKey(new string('a', 129)));
        Assert.NotEqual(CheckoutRequest.ValidateKey("Key"), CheckoutRequest.ValidateKey("key"));
        Assert.Equal(new string('!', 128), CheckoutRequest.ValidateKey(new string('!', 128)));
    }

    [Fact]
    public void Order_stores_exact_snapshots_and_one_normalized_fifteen_minute_deadline()
    {
        var now = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.FromHours(7)).AddTicks(19);
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(),
            [Snapshot(Two, 0.10m, 3), Snapshot(One, 12.50m, 2)], now);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(25.30m, order.Total.Value);
        Assert.Equal(TimeSpan.Zero, order.CreatedAt.Offset);
        Assert.Equal(10, order.CreatedAt.Ticks % 100);
        Assert.Equal(order.CreatedAt.AddMinutes(15), order.ExpiresAt);
        Assert.Equal(new[] { One, Two }, order.Items.Select(item => item.ProductId));
        Assert.Equal(0.30m, order.Items.Single(item => item.ProductId == Two).LineTotal.Value);
    }

    [Fact]
    public void Money_overflow_rejects_the_line_or_sum_without_rounding()
    {
        AssertInvalid(() => Order.Create(Guid.NewGuid(), Guid.NewGuid(), [Snapshot(One, UsdMoney.Maximum, 2)], DateTimeOffset.UnixEpoch));
        AssertInvalid(() => Order.Create(Guid.NewGuid(), Guid.NewGuid(),
            [Snapshot(One, UsdMoney.Maximum, 1), Snapshot(Two, 0.01m, 1)], DateTimeOffset.UnixEpoch));
        AssertInvalid(() => Order.Create(Guid.NewGuid(), Guid.NewGuid(), [Snapshot(One, 0m, 1)], DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Reservation_changes_only_reserved_and_emits_linked_audit_without_partial_underflow()
    {
        var actor = Guid.NewGuid();
        var balance = InventoryBalance.Zero(One);
        balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 2, "Opening", actor, DateTimeOffset.UnixEpoch);
        var reservation = InventoryReservation.Create(Guid.NewGuid(), One, 2, DateTimeOffset.UnixEpoch);
        var movement = balance.Reserve(reservation, actor);
        Assert.Equal(2, balance.OnHand);
        Assert.Equal(2, balance.Reserved);
        Assert.Equal(0, balance.Available);
        Assert.Equal(reservation.Id, movement.OperationId);
        Assert.Equal(reservation.Id, movement.ReservationId);
        Assert.Equal(reservation.OrderId, movement.OrderId);
        Assert.Equal(0, movement.OnHandDelta);
        Assert.Equal(2, movement.ReservedDelta);
        Assert.Equal(StockActorKind.Customer, movement.ActorKind);
        Assert.Equal(2, balance.Revision);
        var failure = Assert.Throws<ApplicationFailureException>(() => balance.Reserve(
            InventoryReservation.Create(Guid.NewGuid(), One, 1, DateTimeOffset.UnixEpoch), actor));
        Assert.Equal(ApplicationError.InsufficientStock, failure.Error);
        Assert.Equal(2, balance.Reserved);
        Assert.Equal(2, balance.Revision);
    }

    private static CreateOrderRequest Request(params (Guid Id, int Quantity)[] items) => new()
    {
        Items = items.Select(item => (CreateOrderItemRequest?)new CreateOrderItemRequest { ProductId = item.Id, Quantity = item.Quantity }).ToList()
    };
    private static OrderItemSnapshot Snapshot(Guid id, decimal price, int quantity) => new(id, "SNAPSHOT", "Product", UsdMoney.FromDecimal(price), quantity);
    private static void AssertInvalid(Action action) => Assert.Equal(ApplicationError.ValidationFailed,
        Assert.Throws<ApplicationFailureException>(action).Error);
}
