using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace OrderFlow.Application.Observability;

public static class SafeLogFields
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "TraceId", "EventId", "CorrelationId", "AttemptCount", "Attempt", "Code", "Uncertain", "ExceptionType",
        "StatusCode", "Method", "Route", "ElapsedMilliseconds", "OrderCount", "SqlState", "Count", "Created",
        "Component", "Ready", "Dependency", "Outcome", "Kind", "CacheKind", "Operation", "Consumer"
    };
    public static KeyValuePair<string, object?>[] Select(IEnumerable<KeyValuePair<string, object?>>? fields) =>
        fields?.Where(p => Allowed.Contains(p.Key) && p.Value is string or Guid or bool or int or long or double
                && (p.Value is not double number || double.IsFinite(number)))
            .DistinctBy(p => p.Key).Take(24).Select(p => new KeyValuePair<string, object?>(p.Key,
                p.Value is string value ? value[..Math.Min(value.Length, 256)] : p.Value)).ToArray() ?? [];
}

// The standard console pipeline with a whitelist formatter: never render Exception.ToString,
// arbitrary formatted messages/scopes, SQL parameters, request bodies or provider payloads.
public sealed class SafeJsonConsoleFormatter() : ConsoleFormatter("orderflow-json")
{
    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var fields = SafeLogFields.Select(entry.State as IEnumerable<KeyValuePair<string, object?>>);
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            Timestamp = DateTimeOffset.UtcNow, Level = entry.LogLevel.ToString(), Category = entry.Category,
            EventId = entry.EventId.Id, Message = entry.EventId.Name ?? "Log",
            TraceId = Activity.Current?.TraceId.ToString() ?? fields.FirstOrDefault(p => p.Key == "TraceId").Value as string,
            SpanId = Activity.Current?.SpanId.ToString(),
            ExceptionType = entry.Exception?.GetType().FullName,
            Fields = fields.ToDictionary(p => p.Key, p => p.Value)
        }));
    }
}

public sealed class LogRedaction : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        var fields = SafeLogFields.Select(record.Attributes).ToList();
        if (record.TraceId == default && fields.FirstOrDefault(p => p.Key == "TraceId").Value is string trace
            && trace.Length == 32 && trace.All(char.IsAsciiHexDigit))
            record.TraceId = ActivityTraceId.CreateFromString(trace);
        if (record.Exception is not null) fields.Add(new("ExceptionType", record.Exception.GetType().FullName));
        record.Attributes = fields;
        record.Exception = null;
        record.FormattedMessage = null;
        record.Body = record.EventId.Name ?? "Log";
        record.TraceState = null;
    }
}
