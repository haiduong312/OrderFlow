using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace OrderFlow.Api.Http;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.TraceIdentifier = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = context.TraceIdentifier });
        var start = Stopwatch.GetTimestamp();
        var completed = false;
        try
        {
            await next(context);
            completed = true;
        }
        finally
        {
            var aborted = context.RequestAborted.IsCancellationRequested;
            var outcome = aborted ? "Aborted" : completed ? "Completed" : "Failed";
            int? status = aborted ? 499 : completed ? context.Response.StatusCode : null;
            var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            // Route template only: never log raw path values, query, headers, cookies or body.
            logger.LogInformation(new EventId(1000, "RequestCompleted"),
                "HTTP {Method} {Route} finished {Outcome} with {StatusCode}, code {Code}, in {ElapsedMilliseconds}ms; trace {TraceId}",
                context.Request.Method, route, outcome, status, context.Items[nameof(ApiProblemDetails.Code)],
                Stopwatch.GetElapsedTime(start).TotalMilliseconds, context.TraceIdentifier);
        }
    }
}
