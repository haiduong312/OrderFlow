using System.Text.Json;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Modules.Inventory;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class InventoryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Receipts_and_adjustments_produce_audited_before_after_facts()
    {
        var balance = InventoryBalance.Zero(Guid.NewGuid());
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var receipt = balance.Change(operation, StockMovementKind.Receipt, 10, "  Supplier delivery  ", actor, Now);
        Assert.Equal(10, balance.OnHand);
        Assert.Equal(0, balance.Reserved);
        Assert.Equal(10, balance.Available);
        Assert.Equal(1, balance.Revision);
        Assert.Equal(operation, receipt.OperationId);
        Assert.Equal(actor, receipt.ActorUserId);
        Assert.Equal(StockActorKind.Admin, receipt.ActorKind);
        Assert.Equal("Supplier delivery", receipt.Reason);
        Assert.Equal(0, receipt.OnHandBefore);
        Assert.Equal(10, receipt.OnHandAfter);
        Assert.Equal(0, receipt.ReservedDelta);
        Assert.Equal(TimeSpan.Zero, receipt.OccurredAt.Offset);
        var adjustment = balance.Change(Guid.NewGuid(), StockMovementKind.Adjustment, -4, "Damaged", actor, Now);
        Assert.Equal(6, balance.Available);
        Assert.Equal(2, adjustment.Revision);
        Assert.Equal(10, adjustment.OnHandBefore);
        Assert.Equal(6, adjustment.OnHandAfter);
        var failure = Assert.Throws<ApplicationFailureException>(() => balance.Change(Guid.NewGuid(), StockMovementKind.Adjustment, -7, "Wrong count", actor, Now));
        Assert.Equal(ApplicationError.InsufficientStock, failure.Error);
        Assert.Equal(6, balance.OnHand);
        Assert.Equal(2, balance.Revision);
    }

    [Theory]
    [InlineData(StockMovementKind.Receipt, 0)]
    [InlineData(StockMovementKind.Receipt, -1)]
    [InlineData(StockMovementKind.Adjustment, 0)]
    [InlineData(StockMovementKind.Initialization, 1)]
    public void Invalid_admin_command_does_not_mutate_balance(StockMovementKind kind, int delta)
    {
        var balance = InventoryBalance.Zero(Guid.NewGuid());
        var failure = Assert.Throws<ApplicationFailureException>(() => balance.Change(Guid.NewGuid(), kind, delta, "Reason", Guid.NewGuid(), Now));
        Assert.Equal(ApplicationError.ValidationFailed, failure.Error);
        Assert.Equal(0, balance.OnHand);
        Assert.Equal(0, balance.Revision);
    }

    [Fact]
    public void Nonzero_identity_reason_and_integer_limits_are_required()
    {
        var balance = InventoryBalance.Zero(Guid.NewGuid());
        var actor = Guid.NewGuid();
        AssertFailure(() => balance.Change(Guid.Empty, StockMovementKind.Receipt, 1, "Reason", actor, Now), ApplicationError.ValidationFailed);
        AssertFailure(() => balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 1, "Reason", Guid.Empty, Now), ApplicationError.Unauthenticated);
        AssertFailure(() => balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 1, " \t ", actor, Now), ApplicationError.ValidationFailed);
        AssertFailure(() => balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 1, new string('r', 501), actor, Now), ApplicationError.ValidationFailed);
        balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, int.MaxValue, "Opening receipt", actor, Now);
        AssertFailure(() => balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 1, "Overflow", actor, Now), ApplicationError.StockLimitExceeded);
        AssertFailure(() => balance.Change(Guid.NewGuid(), StockMovementKind.Adjustment, int.MinValue, "Underflow", actor, Now), ApplicationError.InsufficientStock);
        Assert.Equal(int.MaxValue, balance.OnHand);
        Assert.Equal(1, balance.Revision);
    }

    [Fact]
    public void Multi_product_lock_order_is_canonical_deduplicated_and_bounded()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var expected = ids.OrderBy(id => id.ToString("N"), StringComparer.Ordinal);
        Assert.Equal(expected, InventoryLocking.OrderProductIds(ids.Reverse().Concat(ids)));
        Assert.Throws<ArgumentException>(() => InventoryLocking.OrderProductIds([]));
        Assert.Throws<ArgumentException>(() => InventoryLocking.OrderProductIds([Guid.Empty]));
        Assert.Throws<ArgumentException>(() => InventoryLocking.OrderProductIds(ids.Append(Guid.NewGuid())));
    }

    [Fact]
    public void Movement_json_uses_named_enums_and_utc_microseconds_without_stringifying_quantities()
    {
        var response = new StockMovementResponse(Guid.NewGuid(), Guid.NewGuid(), StockMovement.StockChangeOperation,
            Guid.NewGuid(), StockMovementKind.Receipt, StockActorKind.Admin, Guid.NewGuid(), "Reason", 1, 0, 0, 0, 1, 0, 1, Now.AddTicks(17));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(response, options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Receipt", root.GetProperty("kind").GetString());
        Assert.Equal("Admin", root.GetProperty("actorKind").GetString());
        Assert.Equal("2026-11-04T02:00:00.000001Z", root.GetProperty("occurredAt").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("onHandDelta").ValueKind);
        Assert.Equal(1, root.GetProperty("availableAfter").GetInt32());
        Assert.Equal(TimeSpan.Zero, JsonSerializer.Deserialize<StockMovementResponse>(json, options)!.OccurredAt.Offset);
    }

    private static void AssertFailure(Action action, ApplicationError expected) =>
        Assert.Equal(expected, Assert.Throws<ApplicationFailureException>(action).Error);
}
