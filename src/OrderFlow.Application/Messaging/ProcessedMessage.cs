namespace OrderFlow.Application.Messaging;

public enum InboxStatus { Pending, InFlight, Processed, Failed }

// No FK to OutboxMessages: an inbox can receive independently identified external events.
public sealed class ProcessedMessage
{
    public required string ConsumerName { get; set; }
    public Guid MessageId { get; set; }
    public required string MessageType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public required byte[] PayloadHash { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public InboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastErrorCode { get; set; }
}
