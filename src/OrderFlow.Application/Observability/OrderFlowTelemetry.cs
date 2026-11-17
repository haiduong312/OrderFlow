using System.Diagnostics;
using System.Diagnostics.Metrics;
using OrderFlow.Application.Messaging;

namespace OrderFlow.Application.Observability;

public static class OrderFlowTelemetry
{
    public const string SourceName = "OrderFlow";
    public const string MeterName = "OrderFlow.Operations";
    public static readonly ActivitySource Activities = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> CheckoutDuration = Meter.CreateHistogram<double>("orderflow_checkout_duration", "s");
    private static readonly Counter<long> CheckoutRequests = Meter.CreateCounter<long>("orderflow_checkout_requests");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("orderflow_processing_failures");
    private static readonly Counter<long> Publications = Meter.CreateCounter<long>("orderflow_outbox_publications");
    private static readonly Histogram<double> ExpiryLag = Meter.CreateHistogram<double>("orderflow_reservation_expiry_lag", "s");

    public static void CheckoutCompleted(long start, int status, bool aborted)
    {
        var outcome = aborted ? "cancelled" : status >= 500 ? "server_error" : status >= 400 ? "rejected" : "success";
        CheckoutDuration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome));
        CheckoutRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }
    // Callers choose constants, never exception messages, event types, IDs or configured queue names.
    public static void ProcessingFailed(ProcessingComponent component, FailureKind kind) => Failures.Add(1,
        new("component", component.ToString()), new KeyValuePair<string, object?>("kind", kind.ToString()));
    public static void PublicationCompleted(bool success) => Publications.Add(1, new KeyValuePair<string, object?>("outcome", success ? "confirmed" : "failed"));
    public static void ReservationExpired(DateTimeOffset expiresAt, DateTimeOffset decisionTime) =>
        ExpiryLag.Record(Math.Max(0, (decisionTime - expiresAt).TotalSeconds));

    public static Operation Start(string name, ActivityKind kind = ActivityKind.Internal) => new(Activities.StartActivity(name, kind));
    public static Operation StartMessage(string name, ActivityKind kind, string? parent, string? state, Guid eventId, Guid correlationId)
    {
        var activity = ActivityContext.TryParse(parent, state, isRemote: true, out var context)
            ? Activities.StartActivity(name, kind, context)
            : Activities.StartActivity(name, kind, parentId: "");
        activity?.SetTag("messaging.message.id", eventId.ToString("D"));
        activity?.SetTag("orderflow.correlation_id", correlationId.ToString("D"));
        activity?.SetTag("messaging.system", "rabbitmq");
        return new(activity);
    }
    public static Operation StartPublication(OutboxMessage message) => StartMessage("outbox.publish", ActivityKind.Producer,
        message.TraceParent, message.TraceState, message.EventId, message.CorrelationId);
}

public enum ProcessingComponent { Outbox, Notifications, Realtime, Email }
public enum FailureKind { Dependency, InvalidMessage, Unexpected, LeaseLost }

// An uncompleted meaningful operation is an error, including exceptions that must propagate.
// Do not record exception messages/stack traces or arbitrary payload tags.
public sealed class Operation(Activity? activity) : IDisposable
{
    private bool completed;
    public string? Id => activity?.Id;
    public string? TraceState => activity?.TraceStateString;
    public void Complete() { completed = true; activity?.SetStatus(ActivityStatusCode.Ok); }
    public void Dispose()
    {
        if (!completed) activity?.SetStatus(ActivityStatusCode.Error);
        activity?.Dispose();
    }
}
