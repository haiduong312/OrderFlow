using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CheckoutPersistenceTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Snapshot_history_and_reservation_facts_cannot_be_rewritten_through_raw_SQL()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        using var created = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "immutable");
        await CheckoutTestData.CreatedAsync(created);
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE ordering.\"OrderItems\" SET \"Name\" = 'Rewritten'",
            "DELETE FROM ordering.\"OrderItems\"",
            "TRUNCATE ordering.\"OrderItems\" CASCADE",
            "UPDATE ordering.\"OrderHistory\" SET \"Reason\" = 'Rewritten'",
            "DELETE FROM ordering.\"OrderHistory\"",
            "TRUNCATE ordering.\"OrderHistory\"",
            "UPDATE ordering.\"Orders\" SET \"Total\" = 1.00",
            "UPDATE ordering.\"Orders\" SET \"ExpiresAt\" = \"ExpiresAt\" + interval '1 minute'",
            "UPDATE inventory.\"InventoryReservations\" SET \"Quantity\" = 2"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", failure.SqlState);
        }
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Fact]
    public async Task PostgreSQL_enforces_quantity_money_deadline_reservation_uniqueness_and_audit_links()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        using var created = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "constraints");
        await CheckoutTestData.CreatedAsync(created);
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        const string reservationCopy = """
            INSERT INTO inventory."InventoryReservations" ("Id","OrderId","ProductId","Quantity","Status","CreatedAt","ExpiresAt")
            SELECT gen_random_uuid(),"OrderId","ProductId",{0},{1},"CreatedAt",{2} FROM inventory."InventoryReservations" LIMIT 1
            """;
        foreach (var (quantity, status, expiry, constraint) in new[]
        {
            ("0", "'Active'", "\"ExpiresAt\"", "CK_Reservations_Quantity"),
            ("1", "'Unknown'", "\"ExpiresAt\"", "CK_Reservations_State"),
            ("1", "'Active'", "\"ExpiresAt\" + interval '1 minute'", "CK_Reservations_Deadline")
        })
        {
            await using var command = new NpgsqlCommand(string.Format(reservationCopy, quantity, status, expiry), connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal(constraint, failure.ConstraintName);
        }
        await using (var duplicate = new NpgsqlCommand(string.Format(reservationCopy, "\"Quantity\"", "\"Status\"", "\"ExpiresAt\""), connection))
            Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync())).SqlState);
        await using (var invalidMoney = new NpgsqlCommand("""
            INSERT INTO ordering."OrderItems" ("OrderId","ProductId","Sku","Name","UnitPrice","Quantity","LineTotal")
            SELECT "OrderId","ProductId","Sku","Name",'NaN'::numeric,1,'NaN'::numeric FROM ordering."OrderItems"
            """, connection))
            Assert.Equal("CK_OrderItems_Money", (await Assert.ThrowsAsync<PostgresException>(() => invalidMoney.ExecuteNonQueryAsync())).ConstraintName);
        await using (var wrongLink = new NpgsqlCommand("""
            INSERT INTO inventory."StockMovements"
                ("Id","ProductId","Operation","OperationId","OrderId","ReservationId","Kind","ActorKind","ActorUserId","Reason",
                 "OnHandDelta","ReservedDelta","OnHandBefore","ReservedBefore","OnHandAfter","ReservedAfter","Revision","OccurredAt")
            SELECT gen_random_uuid(),"ProductId",'ReserveStock:v1',missing.id,"OrderId",missing.id,'Reservation','Customer',"ActorUserId",'Wrong link',
                0,1,10,0,10,1,999,"OccurredAt"
            FROM inventory."StockMovements" CROSS JOIN (SELECT gen_random_uuid() AS id) missing WHERE "Kind" = 'Reservation'
            """, connection))
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => wrongLink.ExecuteNonQueryAsync())).SqlState);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Theory]
    [InlineData("P0001", 500, "internal_error")]
    [InlineData("55P03", 503, "service_unavailable")]
    public async Task Failure_writing_outbox_rolls_back_all_previously_written_order_and_inventory_rows(string sqlState, int status, string code)
    {
        var probe = new CheckoutWriteProbe();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: probe);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var suffix = Guid.NewGuid().ToString("N");
        var function = "reject_checkout_" + suffix;
        var trigger = "TR_RejectCheckout_" + suffix;
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION messaging.{function}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."EventType" = 'ordering.order-created' THEN
                    RAISE EXCEPTION 'deliberate checkout event failure' USING ERRCODE = '{sqlState}';
                END IF;
                RETURN NEW;
            END;
            $body$;
            CREATE TRIGGER "{trigger}" BEFORE INSERT ON messaging."OutboxMessages"
            FOR EACH ROW EXECUTE FUNCTION messaging.{function}();
            """, connection)) await install.ExecuteNonQueryAsync();
        try
        {
            using var failure = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "outbox-rollback");
            var problem = await AuthenticationTests.Problem(failure, status, code);
            Assert.DoesNotContain("deliberate checkout event failure", problem.GetRawText());
            Assert.Equal(2, probe.ReservationWrites);
            await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
            Assert.Contains(factory.Logs.Entries, entry => entry.Event.Id == (status == 500 ? 1001 : 1201));
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER \"{trigger}\" ON messaging.\"OutboxMessages\"; DROP FUNCTION messaging.{function}();", connection);
            await remove.ExecuteNonQueryAsync();
        }
        using var corrected = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "outbox-rollback");
        await CheckoutTestData.CreatedAsync(corrected);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 2);
    }
}
