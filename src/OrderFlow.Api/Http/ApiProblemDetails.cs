using Microsoft.AspNetCore.Mvc;

namespace OrderFlow.Api.Http;

public sealed class ApiProblemDetails : ProblemDetails
{
    public required string Code { get; init; }
    public required string TraceId { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();
}
