using Testcontainers.PostgreSql;
using Xunit;

namespace OrderFlow.IntegrationTests.Persistence;

// One server per collection; xUnit creates a new test class instance per test case.
// Run database tests serially to bound DB load; races create independent requests/scopes explicitly.
[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresServer>;

public sealed class PostgresServer : IAsyncLifetime
{
    public const string Image = "postgres:18.6-bookworm@sha256:5c4783a8cc3f0074912164f94dfa1be4e043438f0f707cf968d7af9a0d31a2c8";
    private PostgreSqlContainer? container;
    private string connectionString = "";

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ORDERFLOW_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            connectionString = configured;
            return;
        }

        try
        {
            container = new PostgreSqlBuilder(Image)
                .WithUsername("orderflow_test")
                .WithPassword(Guid.NewGuid().ToString("N"))
                .Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await container.StartAsync(timeout.Token);
            connectionString = container.GetConnectionString();
        }
        catch (Exception startupFailure)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("PostgreSQL container startup and bounded cleanup failed. Check Docker daemon access/image pulls; see docs/testing.md.",
                    startupFailure, cleanupFailure);
            }
            throw new InvalidOperationException(
                "PostgreSQL integration prerequisite unavailable: Testcontainers could not start PostgreSQL. " +
                "A reachable Docker daemon and permission to pull/run images are required. " +
                "Alternatively set ORDERFLOW_TEST_POSTGRES privately to an actual PostgreSQL administrator connection with CREATEDB. " +
                "No InMemory/SQLite fallback or success skip is used. See docs/testing.md.", startupFailure);
        }
    }

    public async Task<PostgresDatabase> CreateDatabaseAsync(string? targetMigration = null)
    {
        if (string.IsNullOrEmpty(connectionString)) throw new InvalidOperationException("Initialize the PostgreSQL server fixture first.");
        var database = new PostgresDatabase(connectionString);
        await database.InitializeAsync(targetMigration);
        return database;
    }

    public async Task DisposeAsync()
    {
        if (container is null) return;
        // Testcontainers disposal has no CancellationToken. Bound the caller; leave Ryuk enabled
        // for crash/timeout recovery. Container reuse must remain disabled.
        await container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        container = null;
    }
}

public abstract class PostgresTest(PostgresServer server) : IAsyncLifetime
{
    protected PostgresServer Server { get; } = server;
    protected PostgresDatabase Database { get; private set; } = null!;
    public async Task InitializeAsync() => Database = await Server.CreateDatabaseAsync();
    public Task DisposeAsync() => Database is null ? Task.CompletedTask : Database.DisposeAsync().AsTask();
}
