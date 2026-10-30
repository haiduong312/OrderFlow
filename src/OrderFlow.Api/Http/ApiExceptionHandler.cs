using Microsoft.AspNetCore.Diagnostics;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Api.Http;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // ExceptionHandlerMiddleware handles RequestAborted cancellations before calling us.
        var problem = exception switch
        {
            ApplicationFailureException failure => ApiProblems.FromFailure(httpContext, failure),
            BadHttpRequestException badRequest when badRequest.StatusCode is >= 400 and < 500 =>
                ApiProblems.FromStatus(httpContext, badRequest.StatusCode),
            _ => ApiProblems.FromStatus(httpContext, 500)
        };

        if (problem.Status == 500)
        {
            // Exception.Message/ToString/inner exceptions can contain SQL, credentials or payloads.
            logger.LogError(new EventId(1001, "UnhandledFailure"),
                "Unhandled HTTP failure {ExceptionType} with code {Code} and trace {TraceId}",
                exception.GetType().FullName, problem.Code, problem.TraceId);
        }

        await ApiProblems.WriteAsync(httpContext, problem, cancellationToken);
        return true;
    }
}
