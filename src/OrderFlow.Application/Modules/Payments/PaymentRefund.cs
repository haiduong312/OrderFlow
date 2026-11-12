using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Payments;

public enum PaymentRefundStatus { Pending, Succeeded, NeedsReview }

// Only full compensation for an unallocated capture. No customer-return/refund command exists.
public sealed class PaymentRefund
{
    public Guid Id { get; set; }
    public Guid CaptureId { get; set; }
    public string Provider { get; set; } = "Simulator";
    public required string ProviderCaptureId { get; set; }
    public string Purpose { get; set; } = "FullCompensation";
    public PaymentRefundStatus Status { get; private set; }
    public required string ProviderRefundKey { get; set; }
    public UsdMoney Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int DeliveryCount { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    public static PaymentRefund Create(PaymentCapture capture, string reason, DateTimeOffset now)
    {
        if (capture.Allocated || capture.Currency != "USD" || capture.Amount.Value <= 0)
            throw new InvalidOperationException("Only unallocated captured USD funds can be compensated.");
        var time = BusinessTime.ToUtcMicroseconds(now); var id = Guid.NewGuid();
        return new() { Id = id, CaptureId = capture.Id, ProviderCaptureId = capture.ProviderCaptureId,
            ProviderRefundKey = "refund-" + id.ToString("N"), Amount = capture.Amount, Reason = reason,
            CreatedAt = time, UpdatedAt = time, NextAttemptAt = time };
    }

    public GatewayRefundRequest Request() => new(ProviderRefundKey, ProviderCaptureId, Amount.ToString(), Currency);

    public Guid Claim(DateTimeOffset now)
    {
        if (Status != PaymentRefundStatus.Pending || DeliveryCount >= 8 || NextAttemptAt > now || LeaseExpiresAt > now)
            throw new InvalidOperationException("Refund is not claimable.");
        DeliveryCount++; UpdatedAt = now; LeaseToken = Guid.NewGuid(); LeaseExpiresAt = now.AddSeconds(60);
        // Persist review at the last claim too: a crash during the final HTTP call cannot hide exhaustion.
        if (DeliveryCount == 8) { Status = PaymentRefundStatus.NeedsReview; LastErrorCode = "refund_retries_exhausted"; }
        return LeaseToken.Value;
    }

    public void Observe(GatewayRefundResult? result, string? errorCode, bool retryable, DateTimeOffset now)
    {
        if (Status == PaymentRefundStatus.Succeeded) return;
        if (LeaseToken is null) throw new InvalidOperationException("Refund observation requires a current lease.");
        if (result is not null)
        {
            if (result.RequestKey != ProviderRefundKey || result.CaptureId != ProviderCaptureId || result.Currency != Currency
                || !UsdMoney.TryParse(result.Amount, out var money) || money != Amount
                || string.IsNullOrWhiteSpace(result.RefundId) || result.RefundId.Length > 100)
            { Status = PaymentRefundStatus.NeedsReview; LastErrorCode = "refund_response_mismatch"; }
            else
            {
                ProviderRefundId = result.RefundId; Status = PaymentRefundStatus.Succeeded;
                SucceededAt = now; LastErrorCode = null;
            }
        }
        else
        {
            Status = retryable && DeliveryCount < 8 ? PaymentRefundStatus.Pending : PaymentRefundStatus.NeedsReview;
            LastErrorCode = DeliveryCount >= 8 ? "refund_retries_exhausted" : errorCode ?? "refund_uncertain";
        }
        UpdatedAt = now; NextAttemptAt = now.Add(PaymentRetry.Delay(DeliveryCount));
        LeaseToken = null; LeaseExpiresAt = null;
    }
}

internal static class PaymentRetry
{
    public static TimeSpan Delay(int deliveryCount)
    {
        int[] seconds = [5, 15, 30, 60, 120, 300, 600, 600];
        return TimeSpan.FromSeconds(seconds[Math.Clamp(deliveryCount - 1, 0, 7)]);
    }
}
