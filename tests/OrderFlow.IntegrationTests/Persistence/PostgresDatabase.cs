using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Persistence;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresDatabase>;

public sealed class PostgresDatabase : IAsyncLifetime
{
    private readonly string databaseName = "orderflow_test_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    private bool created;
    public string ConnectionString { get; private set; } = "";

    public OrderFlowDbContext CreateContext() => new(Options());

    public DbContextOptions<OrderFlowDbContext> Options()
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>();
        PersistenceRegistration.Configure(options, ConnectionString);
        return options.Options;
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ORDERFLOW_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("PostgreSQL tests require ORDERFLOW_TEST_POSTGRES with CREATE DATABASE permission. See docs/persistence.md; no in-memory fallback is used.");
        }

        var builder = new NpgsqlConnectionStringBuilder(configured) { IncludeErrorDetail = false, PersistSecurityInfo = false };
        adminConnection = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        // The identifier is generated here, never taken from user input.
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await command.ExecuteNonQueryAsync();
        }

        created = true;
        builder.Database = databaseName;
        ConnectionString = builder.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!created)
        {
            return;
        }

        // Only the unique database CREATED by this fixture is disposable. Never drop the configured DB.
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}
