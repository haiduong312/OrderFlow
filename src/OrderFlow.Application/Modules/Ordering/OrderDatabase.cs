using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

internal static class OrderDatabase
{
    public static Task SetTimeoutsAsync(OrderFlowDbContext database, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '20s'", cancellationToken);

    public static PostgresException? Contention(Exception failure)
    {
        // Npgsql's non-retrying strategy wraps classified transient failures; inspect types/state.
        if (failure is InvalidOperationException { InnerException: { } inner }) failure = inner;
        var postgres = failure as PostgresException ?? (failure as DbUpdateException)?.InnerException as PostgresException;
        return postgres?.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.QueryCanceled ? postgres : null;
    }
}
