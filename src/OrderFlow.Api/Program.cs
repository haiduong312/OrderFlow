using OrderFlow.Api.Http;

namespace OrderFlow.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpFoundations();
        var app = builder.Build();
        app.UseHttpFoundations();

        // Liveness describes this process only, not database or worker readiness.
        app.MapGet("/api/health/live", () => new LivenessResponse("Healthy"))
            .WithName("GetLiveness")
            .WithSummary("Check API process liveness.")
            .WithDescription("Does not check PostgreSQL or worker readiness.")
            .Produces<ApiProblemDetails>(500, "application/problem+json");

        await app.RunAsync();
    }
}

public sealed record LivenessResponse(string Status);
