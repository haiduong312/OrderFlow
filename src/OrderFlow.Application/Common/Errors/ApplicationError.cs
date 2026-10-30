namespace OrderFlow.Application.Common.Errors;

// Application failures have no dependency on HTTP status codes.
public enum ApplicationError
{
    ValidationFailed,
    NotFound,
    Forbidden,
    StateConflict,
    IdempotencyConflict,
    InsufficientStock,
    VersionConflict,
    OrderExpired
}
