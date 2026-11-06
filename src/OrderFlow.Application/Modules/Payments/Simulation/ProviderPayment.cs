using OrderFlow.Application.Common.Money;

namespace OrderFlow.Application.Modules.Payments.Simulation;

public enum SimulatorScenario { Pending, Succeeded, Failed, TimeoutBeforeAccept, TimeoutAfterCapture, MismatchedAmount }
public sealed class ProviderScenario
{
    public required string RequestKey { get; set; }
    public SimulatorScenario Scenario { get; set; }
}

public sealed class ProviderPayment
{
    private ProviderPayment() { }
    public string RequestKey { get; private set; } = "";
    public Guid AttemptId { get; private set; }
    public Guid OrderId { get; private set; }
    public string TransactionId { get; private set; } = "";
    public UsdMoney RequestedAmount { get; private set; }
    public UsdMoney Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public GatewayPaymentState State { get; private set; }
    public string? CaptureId { get; private set; }
    public Guid EventId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProviderPayment Create(GatewayPaymentRequest request, UsdMoney amount, SimulatorScenario scenario, DateTimeOffset now)
    {
        var payment = new ProviderPayment { RequestKey = request.RequestKey, AttemptId = request.AttemptId, OrderId = request.OrderId,
            RequestedAmount = amount, Amount = scenario == SimulatorScenario.MismatchedAmount
                ? UsdMoney.FromDecimal(amount.Value == 0.01m ? 0.02m : amount.Value - 0.01m) : amount,
            TransactionId = "txn_" + Guid.NewGuid().ToString("N"), CreatedAt = now, UpdatedAt = now, EventId = Guid.NewGuid() };
        if (scenario is SimulatorScenario.Succeeded or SimulatorScenario.TimeoutAfterCapture or SimulatorScenario.MismatchedAmount)
            payment.Resolve(GatewayPaymentState.Succeeded, now);
        else if (scenario == SimulatorScenario.Failed) payment.Resolve(GatewayPaymentState.Failed, now);
        return payment;
    }

    public void Resolve(GatewayPaymentState state, DateTimeOffset now)
    {
        if (!Enum.IsDefined(state) || (State == GatewayPaymentState.Succeeded && state != State)
            || (State == GatewayPaymentState.Failed && state == GatewayPaymentState.Pending))
            throw new SimulatorConflictException();
        if (State == state) return;
        State = state; UpdatedAt = now; EventId = Guid.NewGuid();
        if (state == GatewayPaymentState.Succeeded) CaptureId = "cap_" + Guid.NewGuid().ToString("N");
    }

    public bool Matches(GatewayPaymentRequest request, UsdMoney amount) => request.AttemptId == AttemptId
        && request.OrderId == OrderId && amount == RequestedAmount && request.Currency == Currency;
    public GatewayPaymentResult View() => new(RequestKey, AttemptId, OrderId, TransactionId, Amount.ToString(), Currency, State, CaptureId);
}

public sealed class ProviderRefund
{
    public required string RequestKey { get; set; }
    public required string PaymentRequestKey { get; set; }
    public required string RefundId { get; set; }
    public required string CaptureId { get; set; }
    public UsdMoney Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; }
    public GatewayRefundResult View() => new(RequestKey, RefundId, CaptureId, Amount.ToString(), Currency);
}

public sealed class SimulatorConflictException : Exception;
