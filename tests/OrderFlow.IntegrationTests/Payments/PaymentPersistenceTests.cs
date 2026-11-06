using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class PaymentPersistenceTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Database_enforces_active_attempt_uniqueness_and_immutable_amount_provider_identity()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id); await PaymentInitiationTests.AcceptedAsync(response);
        await using (var bypass = Database.CreateContext())
        {
            var savedOrder = await bypass.Orders.SingleAsync();
            bypass.PaymentAttempts.Add(PaymentAttempt.Create(savedOrder, clock.GetUtcNow()));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => bypass.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal("23505", postgres.SqlState); Assert.Equal("UX_PaymentAttempts_ActiveOrder", postgres.ConstraintName);
        }
        await using var connection = new NpgsqlConnection(Database.ConnectionString); await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE payments.\"PaymentAttempts\" SET \"Amount\" = \"Amount\" + 1",
            "UPDATE payments.\"PaymentAttempts\" SET \"ProviderRequestKey\" = 'changed'" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
        }
        await using var read = Database.CreateContext(); Assert.Equal(1, await read.PaymentAttempts.CountAsync());
        Assert.Equal(1, await read.IdempotencyRecords.CountAsync(row => row.Operation.StartsWith("StartPayment:v1/")));
    }

    [Fact]
    public async Task Failed_intention_transaction_leaves_no_attempt_or_client_key_and_same_key_can_retry()
    {
        await using var factory = new AuthApiFactory(Database);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        await using var connection = new NpgsqlConnection(Database.ConnectionString); await connection.OpenAsync();
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION payments.reject_test_intent() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'deliberate intention failure'; END; $body$;
            CREATE TRIGGER "TR_TestIntent" BEFORE INSERT ON payments."PaymentAttempts"
            FOR EACH ROW EXECUTE FUNCTION payments.reject_test_intent();
            """, connection)) await install.ExecuteNonQueryAsync();
        try
        {
            using var failure = await PaymentInitiationTests.InitiateAsync(owner, order.Id);
            await AuthenticationTests.Problem(failure, 500, "internal_error");
            await using var read = Database.CreateContext();
            Assert.Equal(0, await read.PaymentAttempts.CountAsync());
            Assert.Equal(0, await read.IdempotencyRecords.CountAsync(row => row.Operation.StartsWith("StartPayment:v1/")));
            Assert.Equal(1, await read.OutboxMessages.CountAsync()); Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER \"TR_TestIntent\" ON payments.\"PaymentAttempts\"; DROP FUNCTION payments.reject_test_intent();", connection);
            await remove.ExecuteNonQueryAsync();
        }
        using var retry = await PaymentInitiationTests.InitiateAsync(owner, order.Id); await PaymentInitiationTests.AcceptedAsync(retry);
    }

    [Fact]
    public async Task Lost_merchant_observation_recovers_after_lease_expiry_using_the_same_provider_capture()
    {
        var clock = new ManualTimeProvider(Start);
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id); await PaymentInitiationTests.AcceptedAsync(response);
        await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.SingleAsync();
        await simulator.ScenarioAsync(attempt.ProviderRequestKey, "Succeeded");
        await using var connection = new NpgsqlConnection(Database.ConnectionString); await connection.OpenAsync();
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION payments.reject_test_observation() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."LeaseToken" IS NULL AND NEW."DeliveryCount" > 0 THEN RAISE EXCEPTION 'deliberate observation failure'; END IF;
                RETURN NEW;
            END; $body$;
            CREATE TRIGGER "TR_TestObservation" BEFORE UPDATE ON payments."PaymentAttempts"
            FOR EACH ROW EXECUTE FUNCTION payments.reject_test_observation();
            """, connection)) await install.ExecuteNonQueryAsync();
        try
        {
            using var gateway = simulator.Gateway(); using var first = PaymentInitiationTests.Processing(Database, clock, gateway);
            await Assert.ThrowsAsync<DbUpdateException>(() => first.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER \"TR_TestObservation\" ON payments.\"PaymentAttempts\"; DROP FUNCTION payments.reject_test_observation();", connection);
            await remove.ExecuteNonQueryAsync();
        }
        await read.Entry(attempt).ReloadAsync(); Assert.Equal(PaymentAttemptStatus.Queued, attempt.Status); Assert.NotNull(attempt.LeaseToken);
        await using var providerRead = providerDatabase.CreateContext(); var captured = await providerRead.ProviderPayments.SingleAsync(); Assert.NotNull(captured.CaptureId);
        clock.Advance(TimeSpan.FromSeconds(60));
        using var retryGateway = simulator.Gateway(); using var restarted = PaymentInitiationTests.Processing(Database, clock, retryGateway);
        Assert.True(await restarted.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        await read.Entry(attempt).ReloadAsync();
        Assert.Equal(captured.CaptureId, attempt.ProviderCaptureId); Assert.Equal(captured.TransactionId, attempt.ProviderTransactionId);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status); Assert.Equal(1, await providerRead.ProviderPayments.CountAsync());
        Assert.Equal(1, await read.PaymentAttempts.CountAsync()); Assert.Equal(1, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(1, await read.OutboxMessages.CountAsync()); Assert.Equal(0, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Confirmed")]
    public async Task Nonpending_orders_cannot_create_payment_intentions(string status)
    {
        await using var factory = new AuthApiFactory(Database);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        if (status == "Cancelled") { using var cancel = await OrderManagementTestData.CancelAsync(owner, order.Id); }
        else await OrderManagementTestData.SeedStatusAsync(Database, order.Id, status);
        using var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id);
        await AuthenticationTests.Problem(response, 409, "state_conflict");
        await using var read = Database.CreateContext(); Assert.Equal(0, await read.PaymentAttempts.CountAsync());
    }
}
