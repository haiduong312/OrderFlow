using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrderFlow.Application.Persistence;

namespace OrderFlow.IntegrationTests.Persistence;

/// <summary>Owns only a newly generated database, never the configured administrator database.</summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly string databaseName = "orderflow_test_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection;
    private bool creationAttempted;
    public string ConnectionString { get; }

    internal PostgresDatabase(string connectionString)
    {
        // Avoid retaining one connection pool for every short-lived test database.
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false, PersistSecurityInfo = false, Pooling = false,
            Timeout = 5, CommandTimeout = 20, CancellationTimeout = 1000,
            Options = "-c lock_timeout=10000 -c statement_timeout=20000"
        };
        adminConnection = builder.ConnectionString;
        builder.Database = databaseName;
        ConnectionString = builder.ConnectionString;
    }

    public OrderFlowDbContext CreateContext() => new(Options());

    public DbContextOptions<OrderFlowDbContext> Options()
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>();
        PersistenceRegistration.Configure(options, ConnectionString);
        return options.Options;
    }

    internal async Task InitializeAsync(string? targetMigration)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await using (var admin = new NpgsqlConnection(adminConnection))
            {
                await admin.OpenAsync(timeout.Token);
                // This identifier is generated here, never taken from configuration or a request.
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
                creationAttempted = true;
                await command.ExecuteNonQueryAsync(timeout.Token);
            }
            await using var context = CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(targetMigration, timeout.Token);
        }
        catch (Exception initializationFailure)
        {
            // xUnit does not guarantee disposal after a failed InitializeAsync.
            try { await DisposeAsync(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Test database initialization and cleanup both failed.", initializationFailure, cleanupFailure);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!creationAttempted) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync(timeout.Token);
        creationAttempted = false;
    }
}
