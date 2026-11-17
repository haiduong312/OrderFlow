using System.Diagnostics;
using OpenTelemetry;

namespace OrderFlow.Application.Observability;

// Run before the batch exporter. SQL, URLs/query/path, user-agent, headers, baggage,
// exception details and arbitrary enrichment are deliberately outside this contract.
public sealed class TraceRedaction : BaseProcessor<Activity>
{
    private readonly ActivityListener exceptions = new()
    {
        ShouldListenTo = source => source.Name is "Npgsql" or "System.Net.Http" or "Microsoft.AspNetCore" or OrderFlowTelemetry.SourceName,
        // .NET 10 invokes this before AddException fills default exception fields.
        ExceptionRecorder = (Activity _, Exception error, ref TagList tags) =>
        {
            tags.Clear();
            tags.Add("exception.type", error.GetType().FullName);
            tags.Add("exception.message", "[redacted]");
            tags.Add("exception.stacktrace", "[redacted]");
        }
    };
    public TraceRedaction() => ActivitySource.AddActivityListener(exceptions);
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "http.request.method", "http.response.status_code", "http.route", "network.protocol.version",
        "db.system", "db.system.name", "db.operation.name", "db.operation", "error.type",
        "messaging.system", "messaging.message.id", "orderflow.correlation_id"
    };
    public override void OnEnd(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToArray())
            if (!Allowed.Contains(tag.Key)) activity.SetTag(tag.Key, null);
        // Npgsql may use database/query names; HttpClient may include a remote URI.
        if (activity.Source.Name == "Npgsql") activity.DisplayName = "PostgreSQL command";
        else if (activity.Source.Name == "System.Net.Http") activity.DisplayName = "HTTP dependency";
        else if (activity.Kind == ActivityKind.Server)
            activity.DisplayName = $"{activity.GetTagItem("http.request.method") ?? "HTTP"} {activity.GetTagItem("http.route") ?? "(unmatched)"}";
        activity.SetStatus(activity.Status); // Remove any exception-derived status description.
        activity.TraceStateString = null;
    }
    protected override void Dispose(bool disposing) { if (disposing) exceptions.Dispose(); base.Dispose(disposing); }
}
