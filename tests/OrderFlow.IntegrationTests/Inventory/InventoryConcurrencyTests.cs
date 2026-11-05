using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Catalog;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Inventory;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class InventoryConcurrencyTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Two_api_hosts_cannot_both_remove_the_same_available_stock()
    {
        var gate = InventoryRaceGate.BalanceLocks();
        await using var firstHost = new AuthApiFactory(Database, catalogInterceptor: gate);
        await using var secondHost = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (firstClient, _) = await AuthenticatedBrowser.CreateAsync(firstHost, "Admin");
        var (secondClient, _) = await AuthenticatedBrowser.CreateAsync(secondHost, "Admin");
        using var first = firstClient;
        using var second = secondClient;
        var product = await InventoryHttpTests.Product(first);
        using var opening = await CatalogHttpTests.Send(first, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 10, reason = "Opening receipt" });
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var firstToken = await BrowserRequests.TokenAsync(first);
        var secondToken = await BrowserRequests.TokenAsync(second);
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            BrowserRequests.SendAsync(first, HttpMethod.Post, InventoryHttpTests.Adjustment(product.Id), new { operationId = firstOperation, delta = -7, reason = "First removal" }, firstToken),
            BrowserRequests.SendAsync(second, HttpMethod.Post, InventoryHttpTests.Adjustment(product.Id), new { operationId = secondOperation, delta = -7, reason = "Second removal" }, secondToken));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(2, gate.LockTrace.Select(entry => entry.ContextId).Distinct().Count());
        Assert.Equal(new[] { 201, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        var winningOperation = responses[0].StatusCode == HttpStatusCode.Created ? firstOperation : secondOperation;
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "insufficient_stock");
            response.Dispose();
        }
        var balance = (await first.GetFromJsonAsync<InventoryResponse>(InventoryHttpTests.Inventory(product.Id)))!;
        Assert.Equal(3, balance.OnHand);
        Assert.Equal(2, balance.Revision);
        var history = (await first.GetFromJsonAsync<StockHistoryResponse>(InventoryHttpTests.History(product.Id)))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(balance.OnHand, history.Items.Sum(movement => movement.OnHandDelta));

        // Inspect durable state through a fresh context, independently of HTTP serializers/queries.
        await using var read = Database.CreateContext();
        var saved = await read.InventoryBalances.AsNoTracking().SingleAsync(row => row.ProductId == product.Id);
        var ledger = await read.StockMovements.AsNoTracking().Where(row => row.ProductId == product.Id)
            .OrderBy(row => row.Revision).ToListAsync();
        Assert.Equal(3, saved.OnHand);
        Assert.Equal(0, saved.Reserved);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(new long[] { 0, 1, 2 }, ledger.Select(row => row.Revision));
        Assert.Equal(saved.OnHand, ledger.Sum(row => row.OnHandDelta));
        Assert.Equal(saved.Reserved, ledger.Sum(row => row.ReservedDelta));
        var removal = Assert.Single(ledger, row => row.Kind == StockMovementKind.Adjustment);
        Assert.Equal(winningOperation, removal.OperationId);
        Assert.Equal(10, removal.OnHandBefore);
        Assert.Equal(3, removal.OnHandAfter);
        Assert.False(await read.StockMovements.AnyAsync(row => row.OperationId ==
            (winningOperation == firstOperation ? secondOperation : firstOperation)));
    }

    [Fact]
    public async Task Concurrent_receipts_do_not_lose_increments()
    {
        var gate = InventoryRaceGate.BalanceLocks();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 8, reason = "First" }),
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), new { operationId = Guid.NewGuid(), quantity = 9, reason = "Second" }));
        Assert.Equal(2, gate.Participants);
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.Created, response.StatusCode); response.Dispose(); }
        var balance = (await admin.GetFromJsonAsync<InventoryResponse>(InventoryHttpTests.Inventory(product.Id)))!;
        Assert.Equal(17, balance.Available);
        Assert.Equal(2, balance.Revision);
    }

    [Fact]
    public async Task Concurrent_same_operation_replays_one_movement()
    {
        var gate = InventoryRaceGate.BalanceLocks();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        var body = new { operationId = Guid.NewGuid(), quantity = 5, reason = "Same command" };
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), body),
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id), body));
        Assert.Equal(2, gate.Participants);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        foreach (var response in responses) response.Dispose();
        Assert.Equal(5, (await admin.GetFromJsonAsync<InventoryResponse>(InventoryHttpTests.Inventory(product.Id)))!.OnHand);
        Assert.Equal(2, (await admin.GetFromJsonAsync<StockHistoryResponse>(InventoryHttpTests.History(product.Id)))!.TotalCount);
    }

    [Fact]
    public async Task Cross_product_operation_collision_rolls_back_the_loser_balance()
    {
        var gate = new InventoryRaceGate(command => command.Contains("INSERT INTO inventory.\"StockMovements\"", StringComparison.Ordinal));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var one = await InventoryHttpTests.Product(admin);
        var two = await InventoryHttpTests.Product(admin);
        var operationId = Guid.NewGuid();
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(one.Id), new { operationId, quantity = 6, reason = "Colliding command" }),
            CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(two.Id), new { operationId, quantity = 6, reason = "Colliding command" }));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(new[] { 201, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "idempotency_conflict");
            response.Dispose();
        }
        await using var context = Database.CreateContext();
        var balances = await context.InventoryBalances.AsNoTracking().Where(balance => balance.ProductId == one.Id || balance.ProductId == two.Id).ToListAsync();
        Assert.Equal(new[] { 0, 6 }, balances.Select(balance => balance.OnHand).Order());
        Assert.Equal(1, await context.StockMovements.CountAsync(movement => movement.Operation == StockMovement.StockChangeOperation && movement.OperationId == operationId));
    }

    [Fact]
    public async Task Opposite_input_orders_lock_multiple_rows_in_the_same_product_id_order()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, actor) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var one = await InventoryHttpTests.Product(admin);
        var two = await InventoryHttpTests.Product(admin);
        var ids = new[] { one.Id, two.Id };
        var gate = InventoryRaceGate.BalanceLocks();
        gate.Enabled = true;
        async Task Run(IEnumerable<Guid> input)
        {
            var options = new DbContextOptionsBuilder<OrderFlowDbContext>();
            PersistenceRegistration.Configure(options, Database.ConnectionString);
            options.AddInterceptors(gate);
            await using var context = new OrderFlowDbContext(options.Options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var locked = await InventoryLocking.LockAsync(context, input, default);
            Assert.Equal(InventoryLocking.OrderProductIds(ids), locked.Select(balance => balance.ProductId));
            foreach (var balance in locked)
                context.StockMovements.Add(balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 1, "Multi-row unit", actor, DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await Task.WhenAll(Run(ids), Run(ids.Reverse()));
        Assert.Equal(2, gate.Participants);
        foreach (var contextTrace in gate.LockTrace.GroupBy(entry => entry.ContextId))
            Assert.Equal(InventoryLocking.OrderProductIds(ids), contextTrace.Select(entry => entry.ProductId));
        await using var read = Database.CreateContext();
        var rows = await read.InventoryBalances.AsNoTracking().Where(balance => ids.Contains(balance.ProductId)).ToListAsync();
        Assert.All(rows, balance => { Assert.Equal(2, balance.OnHand); Assert.Equal(2, balance.Revision); });
    }
}

public sealed class InventoryRaceGate(Func<string, bool> matches) : CommandRaceGate(matches)
{
    public ConcurrentQueue<(Guid ContextId, Guid ProductId)> LockTrace { get; } = new();
    public static InventoryRaceGate BalanceLocks() => new(command => command.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal)
        && command.Contains("FOR UPDATE", StringComparison.Ordinal));

    protected override void Observe(DbCommand command, CommandEventData eventData)
    {
        if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            var productId = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<Guid>().First();
            LockTrace.Enqueue((eventData.Context!.ContextId.InstanceId, productId));
        }
    }
}
