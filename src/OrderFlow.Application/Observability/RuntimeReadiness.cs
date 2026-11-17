using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Observability;

public sealed record ReadinessReport(bool Ready, IReadOnlyDictionary<string, bool> Dependencies);

public sealed class DatabaseReadiness(IServiceScopeFactory scopes, ILogger<DatabaseReadiness> logger)
{
    public async Task<bool> CheckAsync(CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(2));
        using var suppress = SuppressInstrumentationScope.Begin();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            database.Database.SetCommandTimeout(2);
            // Check connectivity, migration history and this binary's expected schema, without DDL.
            return !(await database.Database.GetPendingMigrationsAsync(budget.Token)).Any();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Unavailable("Timeout"); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException)
        { return Unavailable(error.GetType().Name); }
    }
    private bool Unavailable(string type)
    {
        logger.LogWarning(new EventId(2800, "DatabaseNotReady"), "Database readiness failed with {ExceptionType}", type);
        return false;
    }
}

// API needs only PostgreSQL/schema. Worker registers its own policy for enabled delivery dependencies.
public interface IRuntimeReadiness
{
    Task<ReadinessReport> CheckAsync(CancellationToken token);
}
public sealed class ApiReadiness(DatabaseReadiness database) : IRuntimeReadiness
{
    public async Task<ReadinessReport> CheckAsync(CancellationToken token)
    {
        var ready = await database.CheckAsync(token);
        return new(ready, new Dictionary<string, bool> { ["postgresql"] = ready });
    }
}
