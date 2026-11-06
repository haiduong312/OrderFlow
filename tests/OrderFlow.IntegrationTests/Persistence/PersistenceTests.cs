using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.Worker;
using Xunit;

namespace OrderFlow.IntegrationTests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class PersistenceTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initial_migration_and_important_database_objects_exist()
    {
        await using var context = Database.CreateContext();
        Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT indexname FROM pg_indexes WHERE schemaname IN ('identity','messaging')
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        foreach (var name in new[] { "UX_Idempotency_User_Operation_Key", "UX_Outbox_ProducerEffect", "PK_ProcessedMessages", "IX_Outbox_Due", "IX_Inbox_Due", "IX_Outbox_ExpiredLease", "IX_Inbox_ExpiredLease", "UserNameIndex", "RoleNameIndex" })
        {
            Assert.Contains(name, indexes);
        }
    }

    [Fact]
    public async Task User_operation_key_is_unique_and_response_bytes_are_original()
    {
        var user = await AddUser();
        var other = await AddUser();
        var body = Encoding.UTF8.GetBytes("{\n  \"id\":\"snapshot\", \"amount\":\"12.50\"\n}");
        var record = Completed(user.Id, "CreateOrder:v1", "same-key", body);
        await using (var context = Database.CreateContext())
        {
            context.AddRange(record, Completed(user.Id, "Other:v1", "same-key"),
                Completed(other.Id, "CreateOrder:v1", "same-key"), Completed(user.Id, "CreateOrder:v1", "SAME-KEY"));
            await context.SaveChangesAsync();
        }

        await using (var duplicate = Database.CreateContext())
        {
            duplicate.Add(Completed(user.Id, "CreateOrder:v1", "same-key"));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            AssertPostgres(failure, PostgresErrorCodes.UniqueViolation, "UX_Idempotency_User_Operation_Key");
        }

        await using var read = Database.CreateContext();
        var saved = await read.IdempotencyRecords.AsNoTracking().SingleAsync(row => row.Id == record.Id);
        Assert.Equal(body, saved.ResponseBody);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(saved.CanonicalRequest)), saved.RequestHash);
        Assert.Equal(201, saved.ResponseStatusCode);
        Assert.Equal("application/json", saved.ResponseContentType);
        Assert.Equal("/api/orders/snapshot", saved.ResponseLocation);
    }

    [Fact]
    public async Task Invalid_snapshot_hash_and_foreign_user_are_rejected_by_postgresql()
    {
        var user = await AddUser();
        var hash = Completed(user.Id, "hash", "key");
        hash.RequestHash = [1];
        await ExpectInsertFailure(hash, PostgresErrorCodes.CheckViolation, "CK_Idempotency_Request");
        var snapshot = Completed(user.Id, "snapshot", "key");
        snapshot.ResponseBody = null;
        await ExpectInsertFailure(snapshot, PostgresErrorCodes.CheckViolation, "CK_Idempotency_Snapshot");
        await ExpectInsertFailure(Completed(Guid.NewGuid(), "foreign", "key"), PostgresErrorCodes.ForeignKeyViolation,
            "FK_IdempotencyRecords_Users_UserId");
        await using var context = Database.CreateContext();
        context.Add(Completed(user.Id, "retained", "key"));
        await context.SaveChangesAsync();
        // Clear dependents so this assertion reaches PostgreSQL rather than EF's client-side restrict guard.
        context.ChangeTracker.Clear();
        context.Users.Remove(await context.Users.SingleAsync(row => row.Id == user.Id));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        AssertPostgres(failure, PostgresErrorCodes.RestrictViolation, "FK_IdempotencyRecords_Users_UserId");
    }

    [Fact]
    public async Task Request_identity_and_completed_snapshot_cannot_be_rewritten()
    {
        var user = await AddUser();
        var record = IdempotencyRecord.Create(user.Id, "immutable", "key", "{}", Now);
        await using var context = Database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Add(record);
        await context.SaveChangesAsync();
        record.Complete(201, "application/json", Encoding.UTF8.GetBytes("{\"original\":true}"), null, Now);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        record.ResponseBody = Encoding.UTF8.GetBytes("{\"changed\":true}");
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        AssertPostgres(failure, PostgresErrorCodes.CheckViolation, "CK_Idempotency_Immutable");
    }

    [Fact]
    public async Task Outbox_event_producer_effect_and_inbox_consumer_identity_are_independent()
    {
        var message = Outbox();
        await using (var context = Database.CreateContext())
        {
            context.AddRange(message, Inbox("one", message.EventId), Inbox("two", message.EventId), Inbox("external", Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        var duplicate = Outbox();
        duplicate.EventId = message.EventId;
        await ExpectInsertFailure(duplicate, PostgresErrorCodes.UniqueViolation, "PK_OutboxMessages");
        var repeatedEffect = Outbox();
        repeatedEffect.AggregateId = message.AggregateId;
        await ExpectInsertFailure(repeatedEffect, PostgresErrorCodes.UniqueViolation, "UX_Outbox_ProducerEffect");
        await ExpectInsertFailure(Inbox("one", message.EventId), PostgresErrorCodes.UniqueViolation, "PK_ProcessedMessages");
    }

    [Fact]
    public async Task Invalid_lease_status_attempts_and_schema_versions_fail_checks()
    {
        var message = Outbox();
        message.Status = OutboxStatus.InFlight;
        await ExpectInsertFailure(message, PostgresErrorCodes.CheckViolation, "CK_Outbox_Lease");
        var version = Outbox();
        version.SchemaVersion = 0;
        await ExpectInsertFailure(version, PostgresErrorCodes.CheckViolation, "CK_Outbox_Metadata");
        var attempts = Outbox();
        attempts.AttemptCount = -1;
        await ExpectInsertFailure(attempts, PostgresErrorCodes.CheckViolation, "CK_Outbox_Attempts");
        var published = Outbox();
        published.Status = OutboxStatus.Published;
        await ExpectInsertFailure(published, PostgresErrorCodes.CheckViolation, "CK_Outbox_Published");
        var incoming = Inbox("invalid", Guid.NewGuid());
        incoming.Status = InboxStatus.Processed;
        await ExpectInsertFailure(incoming, PostgresErrorCodes.CheckViolation, "CK_Inbox_Processed");
        var lease = Inbox("invalid-lease", Guid.NewGuid());
        lease.Status = InboxStatus.InFlight;
        await ExpectInsertFailure(lease, PostgresErrorCodes.CheckViolation, "CK_Inbox_Lease");
    }

    [Fact]
    public async Task Identity_change_request_snapshot_outbox_and_inbox_share_commit_or_rollback()
    {
        var user = await AddUser();
        var outbox = Outbox();
        var incoming = Inbox("transaction", Guid.NewGuid());
        await using (var context = Database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            (await context.Users.SingleAsync(row => row.Id == user.Id)).PhoneNumberConfirmed = true;
            context.AddRange(Completed(user.Id, "rollback", "key"), outbox, incoming);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var read = Database.CreateContext())
        {
            Assert.False((await read.Users.SingleAsync(row => row.Id == user.Id)).PhoneNumberConfirmed);
            Assert.False(await read.OutboxMessages.AnyAsync(row => row.EventId == outbox.EventId));
            Assert.False(await read.IdempotencyRecords.AnyAsync(row => row.UserId == user.Id));
            Assert.False(await read.ProcessedMessages.AnyAsync(row => row.MessageId == incoming.MessageId));
        }

        await using (var context = Database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            (await context.Users.SingleAsync(row => row.Id == user.Id)).PhoneNumberConfirmed = true;
            incoming.Status = InboxStatus.Processed;
            incoming.ProcessedAt = Now;
            context.AddRange(Completed(user.Id, "commit", "key"), outbox, incoming);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var committed = Database.CreateContext();
        Assert.True((await committed.Users.SingleAsync(row => row.Id == user.Id)).PhoneNumberConfirmed);
        Assert.True(await committed.OutboxMessages.AnyAsync(row => row.EventId == outbox.EventId));
        Assert.True(await committed.IdempotencyRecords.AnyAsync(row => row.UserId == user.Id));
        Assert.Equal(InboxStatus.Processed, (await committed.ProcessedMessages.SingleAsync(row => row.MessageId == incoming.MessageId)).Status);
    }

    [Fact]
    public async Task Utc_microseconds_and_numeric_conventions_roundtrip_against_postgresql()
    {
        var value = Now.ToOffset(TimeSpan.FromHours(7)).AddTicks(19);
        var user = await AddUser(value);
        await using (var read = Database.CreateContext())
        {
            var timestamp = (await read.Users.SingleAsync(row => row.Id == user.Id)).CreatedAt;
            Assert.Equal(TimeSpan.Zero, timestamp.Offset);
            Assert.Equal(BusinessTime.ToUtcMicroseconds(value), timestamp);
        }

        // A test-only temporary table proves shared future money mappings without inventing a catalog entity.
        await using var probe = new MoneyProbeContext(Database.Options());
        var property = probe.Model.FindEntityType(typeof(MoneyProbe))!.FindProperty(nameof(MoneyProbe.Amount))!;
        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
        await probe.Database.OpenConnectionAsync();
        await probe.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE \"MoneyProbe\" (\"Id\" uuid PRIMARY KEY, \"Amount\" numeric(18,2) NOT NULL)");
        var money = new MoneyProbe { Id = Guid.NewGuid(), Amount = UsdMoney.FromDecimal(UsdMoney.Maximum) };
        probe.Add(money);
        await probe.SaveChangesAsync();
        probe.ChangeTracker.Clear();
        Assert.Equal(money.Amount, (await probe.Set<MoneyProbe>().SingleAsync()).Amount);
    }

    [Fact]
    public async Task Explicit_role_seed_is_repeatable_and_creates_no_default_accounts()
    {
        await using var context = Database.CreateContext();
        var users = await context.Users.CountAsync();
        await DevelopmentSeeder.SeedRolesAsync(context, CancellationToken.None);
        await DevelopmentSeeder.SeedRolesAsync(context, CancellationToken.None);
        Assert.Equal(2, await context.Roles.CountAsync(role => role.NormalizedName == "ADMIN" || role.NormalizedName == "CUSTOMER"));
        Assert.Equal(users, await context.Users.CountAsync());
    }

    [Fact]
    public async Task Api_worker_and_identity_stores_resolve_one_context_per_scope()
    {
        await using var api = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:OrderFlow"] = Database.ConnectionString })));
        using var client = api.CreateClient();
        await using var first = api.Services.CreateAsyncScope();
        await using var second = api.Services.CreateAsyncScope();
        var context = first.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        Assert.Same(context, first.ServiceProvider.GetRequiredService<OrderFlowDbContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<OrderFlowDbContext>());
        Assert.True(await context.Database.CanConnectAsync());
        var user = User(Now);
        var manager = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.NotNull(await context.Users.SingleOrDefaultAsync(row => row.Id == user.Id));

        using var worker = WorkerHost.Build(["--ConnectionStrings:OrderFlow", Database.ConnectionString]);
        await using var workerScope = worker.Services.CreateAsyncScope();
        var workerContext = workerScope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        Assert.NotSame(context, workerContext);
        Assert.True(await workerContext.Database.CanConnectAsync());
        Assert.NotNull(workerScope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>());
    }

    private async Task<ApplicationUser> AddUser(DateTimeOffset? created = null)
    {
        var user = User(created ?? Now);
        await using var context = Database.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static ApplicationUser User(DateTimeOffset now)
    {
        var email = "test-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        return new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, CreatedAt = now };
    }

    private static IdempotencyRecord Completed(Guid user, string operation, string key, byte[]? body = null)
    {
        var record = IdempotencyRecord.Create(user, operation, key, "{\"items\":[]}", Now);
        record.Complete(201, "application/json", body ?? Encoding.UTF8.GetBytes("{}"), "/api/orders/snapshot", Now);
        return record;
    }

    private static OutboxMessage Outbox() => new()
    {
        EventId = Guid.NewGuid(), EventType = "test.changed", SchemaVersion = 1, Producer = "Test",
        AggregateType = "Test", AggregateId = Guid.NewGuid(), AggregateVersion = 1,
        CorrelationId = Guid.NewGuid(), CausationId = Guid.NewGuid(), Payload = "{}", OccurredAt = Now, NextAttemptAt = Now
    };

    private static ProcessedMessage Inbox(string consumer, Guid message) => new()
    {
        ConsumerName = consumer, MessageId = message, MessageType = "test.changed", SchemaVersion = 1,
        Payload = "{}", PayloadHash = SHA256.HashData(Encoding.UTF8.GetBytes("{}")), ReceivedAt = Now, NextAttemptAt = Now
    };

    private async Task ExpectInsertFailure(object entity, string sqlState, string constraint)
    {
        await using var context = Database.CreateContext();
        context.Add(entity);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        AssertPostgres(failure, sqlState, constraint);
    }

    private static void AssertPostgres(DbUpdateException failure, string state, string constraint)
    {
        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(state, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
    }

    private sealed class MoneyProbe
    {
        public Guid Id { get; set; }
        public UsdMoney Amount { get; set; }
    }

    private sealed class MoneyProbeContext(DbContextOptions<OrderFlowDbContext> options) : OrderFlowDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<MoneyProbe>().ToTable("MoneyProbe", "pg_temp").HasKey(row => row.Id);
        }
    }
}
