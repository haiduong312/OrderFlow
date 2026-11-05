using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Catalog;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class InventoryPersistenceTests(PostgresDatabase database)
{
    [Fact]
    public async Task Postgres_enforces_balance_bounds_and_history_is_append_only_even_for_raw_sql()
    {
        await using var factory = new AuthApiFactory(database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        foreach (var expression in new[] { "\"OnHand\" = -1", "\"Reserved\" = -1", "\"Reserved\" = 1", "\"Revision\" = -1" })
        {
            await using var command = new NpgsqlCommand($"UPDATE inventory.\"Balances\" SET {expression} WHERE \"ProductId\" = @id", connection);
            command.Parameters.AddWithValue("id", product.Id);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        }
        foreach (var sql in new[]
        {
            "UPDATE inventory.\"StockMovements\" SET \"Reason\" = 'Changed' WHERE \"ProductId\" = @id",
            "DELETE FROM inventory.\"StockMovements\" WHERE \"ProductId\" = @id",
            "TRUNCATE inventory.\"StockMovements\""
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            if (sql.Contains("@id", StringComparison.Ordinal)) command.Parameters.AddWithValue("id", product.Id);
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
        }
        await using var deleteBalance = new NpgsqlCommand("DELETE FROM inventory.\"Balances\" WHERE \"ProductId\" = @id", connection);
        deleteBalance.Parameters.AddWithValue("id", product.Id);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, (await Assert.ThrowsAsync<PostgresException>(() => deleteBalance.ExecuteNonQueryAsync())).SqlState);
        Assert.Equal(1, (await admin.GetFromJsonAsync<StockHistoryResponse>(InventoryHttpTests.History(product.Id)))!.TotalCount);
    }

    [Fact]
    public async Task A_stale_tracked_balance_is_refreshed_under_lock_and_pending_changes_are_rejected()
    {
        await using var factory = new AuthApiFactory(database);
        var (client, actorId) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        await using var first = database.CreateContext();
        var tracked = await first.InventoryBalances.SingleAsync(balance => balance.ProductId == product.Id);
        Assert.Equal(0, tracked.OnHand);
        using var receipt = await CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 10, reason = "Different process" });
        Assert.Equal(0, tracked.OnHand);
        var service = new InventoryService(first, new Caller(actorId), TimeProvider.System);
        var result = await service.AdjustAsync(product.Id, new StockAdjustmentRequest { OperationId = Guid.NewGuid(), Delta = 2, Reason = "Fresh snapshot" }, default);
        Assert.Equal(10, result.OnHandBefore);
        Assert.Equal(12, tracked.OnHand);
        Assert.Equal(EntityState.Unchanged, first.Entry(tracked).State);
        await using (var read = database.CreateContext())
            Assert.Equal(12, (await read.InventoryBalances.AsNoTracking().SingleAsync(balance => balance.ProductId == product.Id)).OnHand);
        first.Entry(tracked).Property(balance => balance.OnHand).CurrentValue = 13;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(product.Id,
            new StockAdjustmentRequest { OperationId = Guid.NewGuid(), Delta = 1, Reason = "Cannot discard pending edits" }, default));
        await using var final = database.CreateContext();
        Assert.Equal(12, (await final.InventoryBalances.AsNoTracking().SingleAsync(balance => balance.ProductId == product.Id)).OnHand);
    }

    [Fact]
    public async Task Movement_write_failure_rolls_back_balance_and_returns_sanitized_500()
    {
        await using var factory = new AuthApiFactory(database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        var operation = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");
        var function = "reject_test_movement_" + suffix;
        var trigger = "TR_TestMovement_" + suffix;
        await using var setup = new NpgsqlConnection(database.ConnectionString);
        await setup.OpenAsync();
        // DDL identifiers/literal below are generated UUIDs, never request values.
        await using var install = new NpgsqlCommand($"""
            CREATE FUNCTION inventory.{function}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."OperationId" = '{operation}'::uuid THEN
                    RAISE EXCEPTION 'deliberate inventory audit failure';
                END IF;
                RETURN NEW;
            END;
            $body$;
            CREATE TRIGGER "{trigger}" BEFORE INSERT ON inventory."StockMovements"
            FOR EACH ROW EXECUTE FUNCTION inventory.{function}();
            """, setup);
        await install.ExecuteNonQueryAsync();
        try
        {
            using var failure = await CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = operation, quantity = 5, reason = "Must roll back" });
            var problem = await AuthenticationTests.Problem(failure, 500, "internal_error");
            Assert.DoesNotContain("deliberate inventory audit failure", problem.GetRawText());
            Assert.Equal(0, (await admin.GetFromJsonAsync<InventoryResponse>(InventoryHttpTests.Inventory(product.Id)))!.OnHand);
            Assert.Equal(1, (await admin.GetFromJsonAsync<StockHistoryResponse>(InventoryHttpTests.History(product.Id)))!.TotalCount);
            var logged = Assert.Single(factory.Logs.Entries, entry => entry.Category == "OrderFlow.Api.Http.ApiExceptionHandler" && entry.Event.Id == 1001);
            Assert.Null(logged.Exception);
            Assert.DoesNotContain("deliberate inventory audit failure", logged.Message);
            Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains("Must roll back", StringComparison.Ordinal));
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER \"{trigger}\" ON inventory.\"StockMovements\"; DROP FUNCTION inventory.{function}();", setup);
            await remove.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Product_and_opening_inventory_share_the_same_transaction()
    {
        var category = Category.Create("Atomic creation", DateTimeOffset.UnixEpoch);
        var product = Product.Create("ATOMIC-" + Guid.NewGuid().ToString("N"), "Product", "", UsdMoney.FromDecimal(1m), category.Id, DateTimeOffset.UnixEpoch);
        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Add(category);
            context.Add(product);
            await context.SaveChangesAsync();
            Assert.True(await context.InventoryBalances.AnyAsync(balance => balance.ProductId == product.Id));
            Assert.True(await context.StockMovements.AnyAsync(movement => movement.ProductId == product.Id));
            await transaction.RollbackAsync();
        }
        await using var read = database.CreateContext();
        Assert.False(await read.Products.AnyAsync(row => row.Id == product.Id));
        Assert.False(await read.InventoryBalances.AnyAsync(row => row.ProductId == product.Id));
        Assert.False(await read.StockMovements.AnyAsync(row => row.ProductId == product.Id));
    }

    [Fact]
    public async Task Upgrade_backfills_existing_active_and_archived_products_once()
    {
        var name = "orderflow_inventory_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(database.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = name };
            var options = new DbContextOptionsBuilder<OrderFlowDbContext>();
            PersistenceRegistration.Configure(options, connection.ConnectionString);
            await using var context = new OrderFlowDbContext(options.Options);
            await context.GetService<IMigrator>().MigrateAsync("20261103093358_AddCatalog");
            var category = Category.Create("Legacy", DateTimeOffset.UnixEpoch);
            var one = Product.Create("LEGACY-ONE", "One", "", UsdMoney.FromDecimal(1m), category.Id, DateTimeOffset.UnixEpoch);
            var two = Product.Create("LEGACY-TWO", "Two", "", UsdMoney.FromDecimal(2m), category.Id, DateTimeOffset.UnixEpoch);
            two.Archive(1);
            context.AddRange(category, one, two);
            await context.SaveChangesAsync();
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
            var balances = await context.InventoryBalances.AsNoTracking().ToListAsync();
            Assert.Equal(2, balances.Count);
            Assert.All(balances, balance => { Assert.Equal(0, balance.OnHand); Assert.Equal(0, balance.Reserved); Assert.Equal(0, balance.Revision); });
            var movements = await context.StockMovements.AsNoTracking().ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.All(movements, movement => { Assert.Equal(StockActorKind.System, movement.ActorKind); Assert.Equal(StockMovementKind.Initialization, movement.Kind); });
            Assert.Equal(2, await context.Products.CountAsync());
            Assert.False((await context.Products.AsNoTracking().SingleAsync(product => product.Id == two.Id)).IsActive);
        }
        finally
        {
            // This test owns only the uniquely generated DB it created above.
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Locks_require_a_transaction_and_revision_exhaustion_has_no_effect()
    {
        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryLocking.LockAsync(context, [Guid.NewGuid()], default));
        await using var factory = new AuthApiFactory(database);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory.\"Balances\" SET \"Revision\" = {long.MaxValue} WHERE \"ProductId\" = {product.Id}");
        using var failure = await CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 1, reason = "No revision overflow" });
        await AuthenticationTests.Problem(failure, 409, "stock_limit_exceeded");
        Assert.Equal(0, (await admin.GetFromJsonAsync<InventoryResponse>(InventoryHttpTests.Inventory(product.Id)))!.OnHand);
        Assert.Equal(1, (await admin.GetFromJsonAsync<StockHistoryResponse>(InventoryHttpTests.History(product.Id)))!.TotalCount);
    }

    private sealed record Caller(Guid? UserId) : ICurrentUser;
}
