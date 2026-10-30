namespace OrderFlow.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        // Liveness describes this process only, not database or worker readiness.
        app.MapGet("/api/health/live", () => Results.Ok(new { status = "Healthy" }));

        await app.RunAsync();
    }
}
