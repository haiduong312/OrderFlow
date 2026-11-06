using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class OrderManagementPersistenceTests(PostgresServer server) : PostgresTest(server)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outbox_failure_rolls_back_prior_release_SQL_and_allows_safe_retry(bool expired)
    {
        var probe = new ReleaseWriteProbe();
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: probe, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        if (expired) clock.Advance(TimeSpan.FromMinutes(15));
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION messaging.reject_test_order_transition() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."EventType" IN ('ordering.order-cancelled','ordering.order-expired') THEN
                    RAISE EXCEPTION 'deliberate order transition event failure';
                END IF;
                RETURN NEW;
            END;
            $body$;
            CREATE TRIGGER "TR_TestOrderTransition" BEFORE INSERT ON messaging."OutboxMessages"
            FOR EACH ROW EXECUTE FUNCTION messaging.reject_test_order_transition();
            """, connection)) await install.ExecuteNonQueryAsync();
        try
        {
            using var response = await OrderManagementTestData.CancelAsync(customer, order.Id);
            var problem = await AuthenticationTests.Problem(response, 500, "internal_error");
            Assert.DoesNotContain("deliberate order transition event failure", problem.GetRawText());
            Assert.Equal(2, probe.Writes);
            await CheckoutTestData.AssertCountsAsync(Database, 1, 2);
            await using var read = Database.CreateContext();
            var unchanged = await read.Orders.SingleAsync();
            Assert.Equal(OrderStatus.PendingPayment, unchanged.Status); Assert.Equal(1, unchanged.Version);
            Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(2, row.Reserved); Assert.Equal(2, row.Revision); });
            Assert.All(await read.InventoryReservations.ToListAsync(), row => { Assert.Equal(ReservationStatus.Active, row.Status); Assert.Null(row.ReleasedAt); });
            Assert.Equal(0, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
            Assert.Contains(factory.Logs.Entries, entry => entry.Event.Id == 1001);
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER \"TR_TestOrderTransition\" ON messaging.\"OutboxMessages\"; DROP FUNCTION messaging.reject_test_order_transition();", connection);
            await remove.ExecuteNonQueryAsync();
        }
        using var retry = await OrderManagementTestData.CancelAsync(customer, order.Id);
        Assert.Equal(expired ? HttpStatusCode.Conflict : HttpStatusCode.OK, retry.StatusCode);
        await using var final = Database.CreateContext();
        Assert.Equal(expired ? OrderStatus.Expired : OrderStatus.Cancelled, (await final.Orders.SingleAsync()).Status);
        Assert.Equal(2, await final.OrderStatusHistory.CountAsync()); Assert.Equal(2, await final.OutboxMessages.CountAsync());
        Assert.All(await final.InventoryBalances.ToListAsync(), row => Assert.Equal(0, row.Reserved));
    }

    [Fact]
    public async Task Released_reservations_cannot_reactivate_or_rewrite_terminal_metadata_and_history_remains_append_only()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        using var cancelled = await OrderManagementTestData.CancelAsync(customer, order.Id);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await using var connection = new NpgsqlConnection(Database.ConnectionString); await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE inventory.\"InventoryReservations\" SET \"Status\"='Active', \"ReleasedAt\"=NULL, \"ReleaseReason\"=NULL",
            "UPDATE inventory.\"InventoryReservations\" SET \"ReleasedAt\"=\"ReleasedAt\"+interval '1 minute'",
            "UPDATE inventory.\"InventoryReservations\" SET \"ReleaseReason\"='Expired'",
            "UPDATE ordering.\"OrderStatusHistory\" SET \"Reason\"='Rewritten'",
            "DELETE FROM ordering.\"OrderStatusHistory\"",
            "TRUNCATE ordering.\"OrderStatusHistory\""
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
        }
        await using var read = Database.CreateContext();
        Assert.Equal(2, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(1, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
    }

    [Fact]
    public async Task Upgrade_renames_existing_history_without_losing_rows_or_its_immutability_trigger()
    {
        await using var legacy = await Server.CreateDatabaseAsync("20261105070955_AddCheckout");
        var now = DateTimeOffset.UnixEpoch;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy@example.invalid", Email = "legacy@example.invalid", CreatedAt = now };
        var claim = IdempotencyRecord.Create(user.Id, CheckoutRequest.Operation, "legacy", "{}", now);
        claim.Complete(201, "application/json", "{}"u8.ToArray(), "/api/orders/legacy", now);
        var category = Category.Create("Legacy", now);
        var product = Product.Create("LEGACY-ORDER", "Legacy", "", UsdMoney.FromDecimal(1m), category.Id, now);
        var order = Order.Create(user.Id, claim.Id, [new(product.Id, product.Sku, product.Name, product.Price, 1)], now);
        await using (var context = legacy.CreateContext())
        {
            // Unchanged user/catalog/claim/order mappings can seed the old schema; history is
            // inserted with its legacy name/columns before applying the rename migration.
            context.AddRange(user, category, product, claim, order); await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ordering."OrderHistory" ("OrderId","Version","Status","ActorUserId","Reason","OccurredAt")
                VALUES ({order.Id},1,'PendingPayment',{user.Id},'Legacy creation fact',{now})
                """);
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
        }
        await using var read = legacy.CreateContext();
        var history = await read.OrderStatusHistory.SingleAsync();
        Assert.Equal(order.Id, history.OrderId); Assert.Equal(user.Id, history.ActorUserId);
        Assert.Equal("Legacy creation fact", history.Reason); Assert.Equal(now, history.OccurredAt);
        Assert.Equal(OrderActorKind.Customer, history.ActorKind); Assert.Null(history.FromStatus);
        Assert.Equal(1, await read.Orders.CountAsync());
        Assert.Equal(1, await read.OrderItems.CountAsync());
        await using var connection = new NpgsqlConnection(legacy.ConnectionString); await connection.OpenAsync();
        await using var mutation = new NpgsqlCommand("DELETE FROM ordering.\"OrderStatusHistory\"", connection);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync())).SqlState);
    }
}

public sealed class ReleaseWriteProbe : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private int writes;
    public int Writes => Volatile.Read(ref writes);
    public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData, System.Data.Common.DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("UPDATE inventory.\"InventoryReservations\"", StringComparison.Ordinal)) Interlocked.Increment(ref writes);
        return ValueTask.FromResult(result);
    }
}
