using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Api.Http;

public static class ApiProblems
{
    public static ApiProblemDetails FromFailure(HttpContext context, ApplicationFailureException failure)
    {
        var (status, code, title) = failure.Error switch
        {
            ApplicationError.ValidationFailed => (400, "validation_failed", "One or more fields are invalid."),
            ApplicationError.NotFound => (404, "not_found", "The requested resource was not found."),
            ApplicationError.Forbidden => (403, "forbidden", "This operation is not permitted."),
            ApplicationError.StateConflict => (409, "state_conflict", "The operation conflicts with the current state."),
            ApplicationError.IdempotencyConflict => (409, "idempotency_conflict", "The idempotency key was used for a different request."),
            ApplicationError.InsufficientStock => (409, "insufficient_stock", "Insufficient stock is available."),
            ApplicationError.VersionConflict => (409, "version_conflict", "The resource version has changed."),
            ApplicationError.OrderExpired => (409, "order_expired", "The order has expired."),
            _ => (500, "internal_error", "An unexpected error occurred.")
        };

        return Create(context, status, code, title,
            failure.Error == ApplicationError.ValidationFailed ? failure.Errors : null);
    }

    public static ApiProblemDetails FromStatus(HttpContext context, int status)
    {
        var (code, title) = status switch
        {
            400 => ("validation_failed", "The request is invalid."),
            401 => ("unauthenticated", "Authentication is required."),
            403 => ("forbidden", "This operation is not permitted."),
            404 => ("not_found", "The requested resource was not found."),
            405 => ("method_not_allowed", "The HTTP method is not supported for this endpoint."),
            409 => ("state_conflict", "The operation conflicts with the current state."),
            413 => ("request_too_large", "The request body is too large."),
            415 => ("unsupported_media_type", "The request media type is not supported."),
            429 => ("rate_limited", "Too many requests."),
            500 => ("internal_error", "An unexpected error occurred."),
            503 => ("service_unavailable", "The service is temporarily unavailable."),
            _ => ("http_error", "The HTTP request failed.")
        };

        return Create(context, status, code, title);
    }

    public static ApiProblemDetails Create(
        HttpContext context, int status, string code, string title,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        context.Items[nameof(ApiProblemDetails.Code)] = code;
        return new()
        {
            Status = status,
            Type = $"urn:orderflow:error:{code}",
            Title = title,
            Code = code,
            TraceId = context.TraceIdentifier,
            Errors = errors ?? new Dictionary<string, string[]>()
        };
    }

    public static IActionResult InvalidModelState(ActionContext actionContext)
    {
        // Framework binding messages can echo attempted values; keep those out of responses.
        var errors = actionContext.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, _ => new[] { "The field is invalid or missing." });
        var problem = Create(actionContext.HttpContext, 400, "validation_failed",
            "One or more fields are invalid.", errors);
        actionContext.HttpContext.Response.Headers.CacheControl = "no-store";
        var result = new ObjectResult(problem) { StatusCode = 400 };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    public static Task WriteAsync(HttpContext context, ApiProblemDetails problem, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = problem.Status!.Value;
        context.Response.Headers.CacheControl = "no-store";
        // A fixed JSON error contract is also used when Accept asks for HTML/text.
        return context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
    }
}
