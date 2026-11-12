using System.Diagnostics;

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
    public DateTimeOffset? LastFailureAt { get; set; }
    // Capture producer context once; publisher retries must not replace it with their own trace.
    public string? TraceParent { get; set; } = Activity.Current?.IdFormat == ActivityIdFormat.W3C ? Activity.Current.Id : null;
    public string? TraceState { get; set; } = Activity.Current?.IdFormat == ActivityIdFormat.W3C
        && Activity.Current.TraceStateString is { Length: <= 512 } state ? state : null;
}
