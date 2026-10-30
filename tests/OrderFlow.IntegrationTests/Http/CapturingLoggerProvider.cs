using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OrderFlow.IntegrationTests.Http;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();
    public TaskCompletionSource<LogEntry> RequestCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }

    public sealed record LogEntry(string Category, LogLevel Level, EventId Event, string Message,
        Exception? Exception, IReadOnlyDictionary<string, object?> Fields);

    private sealed class CaptureLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            var entry = new LogEntry(category, logLevel, eventId, formatter(state, exception), exception, fields);
            owner.Entries.Enqueue(entry);
            if (category == "OrderFlow.Api.Http.RequestLoggingMiddleware" && eventId.Id == 1000)
            {
                owner.RequestCompleted.TrySetResult(entry);
            }
        }
    }
}
