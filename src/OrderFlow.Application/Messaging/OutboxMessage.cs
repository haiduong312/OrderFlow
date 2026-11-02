namespace OrderFlow.Application.Messaging;

public enum OutboxStatus { Pending, InFlight, Published, Failed }

public sealed class OutboxMessage
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Producer { get; set; }
    public required string AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid CausationId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public OutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? LastErrorCode { get; set; }
}
