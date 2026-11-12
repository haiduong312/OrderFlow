using System.Text.Json;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Modules.Payments;

public enum PaymentAttemptStatus { Queued, Pending, Unknown, Failed, Captured }

public sealed class PaymentAttempt
{
    private PaymentAttempt() { }
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Provider { get; private set; } = "Simulator";
    public string ProviderRequestKey { get; private set; } = "";
    public UsdMoney Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentAttemptStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int DeliveryCount { get; private set; }
    public bool NeedsAttention { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public string? ProviderCaptureId { get; private set; }
    public GatewayPaymentState? ObservedState { get; private set; }
    public string? LastObservation { get; private set; }
    public DateTimeOffset? ReconciledAt { get; private set; }

    public Guid ClaimReconciliation(DateTimeOffset now)
    {
        if (NeedsAttention && LastErrorCode is not ("settlement_pending" or "gateway_response_mismatch"))
            throw new InvalidOperationException("A manual-review attempt cannot be automatically reconciled.");
        if (Status is PaymentAttemptStatus.Failed or PaymentAttemptStatus.Captured || DeliveryCount >= 8
            || NextAttemptAt > now || LeaseExpiresAt > now)
            throw new InvalidOperationException("The payment attempt is not reconcilable.");
        LeaseToken = Guid.NewGuid(); LeaseExpiresAt = now.AddSeconds(60); DeliveryCount++; UpdatedAt = now;
        if (DeliveryCount == 8) { NeedsAttention = true; LastErrorCode = "reconciliation_exhausted"; }
        return LeaseToken.Value;
    }

    public void RememberLookup(GatewayPaymentResult result, DateTimeOffset now)
    {
        if (LeaseToken is null) throw new InvalidOperationException("Lookup observation requires the current lease.");
        LastObservation = JsonSerializer.Serialize(result, GatewayJson.Options);
        ReconciledAt = now;
    }

    public void CompleteLookup(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        NextAttemptAt = UpdatedAt.Add(PaymentRetry.Delay(DeliveryCount));
        LeaseToken = null; LeaseExpiresAt = null;
        if (Status is not (PaymentAttemptStatus.Captured or PaymentAttemptStatus.Failed) && DeliveryCount >= 8)
        { NeedsAttention = true; LastErrorCode = "reconciliation_exhausted"; }
    }

    public void ReviewLookup(string code, DateTimeOffset now)
    {
        RequireReview(code, now);
        ReconciledAt = now;
    }

    public void RequireReview(string code, DateTimeOffset now)
    {
        if (Status == PaymentAttemptStatus.Captured) return;
        NeedsAttention = true; LastErrorCode = code;
        if (ObservedState != GatewayPaymentState.Succeeded) Status = PaymentAttemptStatus.Unknown;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        LeaseToken = null; LeaseExpiresAt = null;
    }

    public static PaymentAttempt Create(Order order, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.PendingPayment || BusinessTime.HasExpired(now, order.ExpiresAt))
            throw new InvalidOperationException("Only an eligible locked pending order may create an attempt.");
        var id = Guid.NewGuid(); var time = BusinessTime.ToUtcMicroseconds(now);
        return new() { Id = id, OrderId = order.Id, ProviderRequestKey = "payment-" + id.ToString("N"),
            Amount = order.Total, CreatedAt = time, UpdatedAt = time, NextAttemptAt = time };
    }

    public GatewayPaymentRequest Request() => new(ProviderRequestKey, Id, OrderId, Amount.ToString(), Currency);

    public Guid Claim(DateTimeOffset now)
    {
        if (Status is PaymentAttemptStatus.Failed or PaymentAttemptStatus.Captured || NeedsAttention || DeliveryCount >= 8 || NextAttemptAt > now
            || LeaseExpiresAt > now) throw new InvalidOperationException("The payment attempt is not claimable.");
        LeaseToken = Guid.NewGuid(); LeaseExpiresAt = now.AddSeconds(60); DeliveryCount++; UpdatedAt = now;
        // Persist exhaustion in the claim too: a crash during the eighth network call must
        // leave visible attention work, not an unclaimable row that still looks healthy.
        if (DeliveryCount == 8) { NeedsAttention = true; LastErrorCode = "reconciliation_exhausted"; }
        return LeaseToken.Value;
    }

    public void Observe(GatewayPaymentResult? result, string? errorCode, DateTimeOffset now)
    {
        // A callback may settle while the gateway HTTP request is still in flight.
        if (Status == PaymentAttemptStatus.Captured) return;
        if (LeaseToken is null) throw new InvalidOperationException("Observation requires the current lease.");
        // Provider capture evidence is monotonic even before verified callback settlement.
        if (ObservedState == GatewayPaymentState.Succeeded)
        {
            LeaseToken = null; LeaseExpiresAt = null;
            return;
        }
        if (result is not null)
        {
            LastObservation = JsonSerializer.Serialize(result, GatewayJson.Options);
            var matches = result.RequestKey == ProviderRequestKey && result.AttemptId == Id && result.OrderId == OrderId
                && result.Currency == Currency && UsdMoney.TryParse(result.Amount, out var money) && money == Amount
                && !string.IsNullOrWhiteSpace(result.TransactionId) && result.TransactionId.Length <= 100
                && (ProviderTransactionId is null || ProviderTransactionId == result.TransactionId)
                && Enum.IsDefined(result.State)
                && (result.State == GatewayPaymentState.Succeeded
                    ? !string.IsNullOrWhiteSpace(result.CaptureId) && result.CaptureId.Length <= 100
                    : result.CaptureId is null);
            if (!matches)
            {
                Status = PaymentAttemptStatus.Unknown; NeedsAttention = true; LastErrorCode = "gateway_response_mismatch";
            }
            else
            {
                ProviderTransactionId = result.TransactionId; ProviderCaptureId = result.CaptureId; ObservedState = result.State;
                Status = result.State == GatewayPaymentState.Failed ? PaymentAttemptStatus.Failed : PaymentAttemptStatus.Pending;
                // Submission records evidence only; callback/reconciliation settles under the order lock.
                NeedsAttention = result.State == GatewayPaymentState.Succeeded;
                LastErrorCode = NeedsAttention ? "settlement_pending" : null;
            }
        }
        else { Status = PaymentAttemptStatus.Unknown; LastErrorCode = errorCode ?? "gateway_unavailable"; }
        if (DeliveryCount >= 8 && Status != PaymentAttemptStatus.Failed)
        { NeedsAttention = true; LastErrorCode ??= "reconciliation_exhausted"; }
        int[] delays = [5, 15, 30, 60, 120, 300, 600, 600];
        UpdatedAt = now; NextAttemptAt = now.AddSeconds(delays[Math.Clamp(DeliveryCount - 1, 0, 7)]);
        LeaseToken = null; LeaseExpiresAt = null;
    }

    public void ApplyCallback(GatewayPaymentResult result, DateTimeOffset decisionAt)
    {
        if (Status == PaymentAttemptStatus.Captured) return;
        ProviderTransactionId ??= result.TransactionId;
        if (result.State == GatewayPaymentState.Succeeded)
        {
            ProviderCaptureId ??= result.CaptureId;
            ObservedState = GatewayPaymentState.Succeeded;
            Status = PaymentAttemptStatus.Captured;
        }
        else if (ObservedState != GatewayPaymentState.Succeeded && Status != PaymentAttemptStatus.Failed)
        {
            ObservedState = result.State;
            Status = result.State == GatewayPaymentState.Failed ? PaymentAttemptStatus.Failed : PaymentAttemptStatus.Pending;
        }
        else return;
        UpdatedAt = decisionAt > UpdatedAt ? decisionAt : UpdatedAt;
        NeedsAttention = false; LastErrorCode = null;
        // Fence the submission response; its old lease must not overwrite this fact.
        LeaseToken = null; LeaseExpiresAt = null;
    }
}
