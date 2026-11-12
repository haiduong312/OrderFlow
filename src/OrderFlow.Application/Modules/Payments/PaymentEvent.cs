using OrderFlow.Application.Common.Money;

namespace OrderFlow.Application.Modules.Payments;

public enum PaymentEventStatus { Pending, Processed, Quarantined }

// Authenticated receipt; references deliberately have no FK so unknown references survive quarantine.
public sealed class PaymentEvent
{
    public string Provider { get; set; } = "Simulator";
    public Guid ProviderEventId { get; set; }
    public required byte[] RawBody { get; set; }
    public required byte[] PayloadHash { get; set; }
    public Guid ReferencedOrderId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public PaymentEventStatus Status { get; set; }
    public string? ErrorCode { get; set; }
}

// Captured funds are independent of order fulfillment. One capture either wins allocation or needs compensation.
public sealed class PaymentCapture
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "Simulator";
    public required string ProviderCaptureId { get; set; }
    public required string ProviderTransactionId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? ProviderEventId { get; set; }
    public string Source { get; set; } = "Callback";
    public UsdMoney Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Allocated { get; set; }
    public DateTimeOffset? ProviderOccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
