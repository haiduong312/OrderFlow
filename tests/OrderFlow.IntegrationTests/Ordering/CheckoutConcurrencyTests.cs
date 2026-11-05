using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CheckoutConcurrencyTests(PostgresServer server) : PostgresTest(server)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_same_key_produces_one_order_or_conflicts_after_unique_recovery(bool conflicting)
    {
        var gate = new CheckoutRaceGate(claim: true);
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var token = await BrowserRequests.TokenAsync(customer);
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "concurrent-key", token),
            CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products, conflicting ? 2 : 1), "concurrent-key", token));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(2, gate.Contexts.Distinct().Count());
        Assert.Equal(conflicting ? new[] { 201, 409 } : new[] { 201, 201 }, responses.Select(response => (int)response.StatusCode).Order());
        if (!conflicting) Assert.Equal(await responses[0].Content.ReadAsByteArrayAsync(), await responses[1].Content.ReadAsByteArrayAsync());
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "idempotency_conflict");
            response.Dispose();
        }
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
        await using var read = Database.CreateContext();
        var reservation = await read.InventoryReservations.SingleAsync();
        var balance = await read.InventoryBalances.SingleAsync();
        Assert.Equal(reservation.Quantity, balance.Reserved);
        Assert.InRange(reservation.Quantity, 1, conflicting ? 2 : 1);
    }

    [Fact]
    public async Task Two_hosts_contending_for_the_last_unit_create_only_one_order_reservation_and_event()
    {
        // Both requests hold compatible catalog SHARE locks and distinct claims. Neither owns
        // the contested inventory lock yet, so both can reach this pre-FOR UPDATE rendezvous.
        var gate = new CheckoutRaceGate(claim: false);
        await using var firstHost = new AuthApiFactory(Database, catalogInterceptor: gate);
        await using var secondHost = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (firstClient, _) = await AuthenticatedBrowser.CreateAsync(firstHost, "Customer");
        var (secondClient, _) = await AuthenticatedBrowser.CreateAsync(secondHost, "Customer");
        using var first = firstClient;
        using var second = secondClient;
        var products = await CheckoutTestData.StockAsync(Database, firstHost, count: 1, stock: 1);
        var tokenOne = await BrowserRequests.TokenAsync(first);
        var tokenTwo = await BrowserRequests.TokenAsync(second);
        gate.Enabled = true;
        var responses = await Task.WhenAll(
            CheckoutTestData.PostAsync(first, CheckoutTestData.Body(products), "last-unit-one", tokenOne),
            CheckoutTestData.PostAsync(second, CheckoutTestData.Body(products), "last-unit-two", tokenTwo));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(2, gate.Contexts.Distinct().Count());
        Assert.Equal(new[] { 201, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "insufficient_stock");
            response.Dispose();
        }
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
        await using var read = Database.CreateContext();
        var order = await read.Orders.SingleAsync();
        var balance = await read.InventoryBalances.SingleAsync();
        var reservation = await read.InventoryReservations.SingleAsync();
        var audit = await read.StockMovements.SingleAsync(movement => movement.Kind == StockMovementKind.Reservation);
        Assert.Equal(1, balance.OnHand);
        Assert.Equal(1, balance.Reserved);
        Assert.Equal(0, balance.Available);
        Assert.Equal(2, balance.Revision);
        Assert.Equal(order.Id, reservation.OrderId);
        Assert.Equal(reservation.Id, audit.ReservationId);
        Assert.Equal(order.UserId, audit.ActorUserId);
        Assert.Equal(order.Id, (await read.OutboxMessages.SingleAsync()).AggregateId);
    }

    [Fact]
    public async Task Failure_on_second_product_rolls_back_the_first_SQL_reservation_and_releases_the_key()
    {
        var probe = new CheckoutWriteProbe();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: probe);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, stock: 1);
        var body = new { items = new[] { new { productId = products[0].Id, quantity = 1 }, new { productId = products[1].Id, quantity = 2 } } };
        using var insufficient = await CheckoutTestData.PostAsync(customer, body, "partial-rollback");
        await AuthenticationTests.Problem(insufficient, 409, "insufficient_stock");
        Assert.Equal(1, probe.ReservationWrites); // A prior participant really wrote SQL before the failure.
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
        await using (var read = Database.CreateContext())
        {
            Assert.All(await read.InventoryBalances.ToListAsync(), balance =>
            {
                Assert.Equal(1, balance.OnHand); Assert.Equal(0, balance.Reserved); Assert.Equal(1, balance.Revision);
            });
        }
        // A failed claim caches nothing; a corrected body may reuse the same key.
        using var corrected = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "partial-rollback");
        await CheckoutTestData.CreatedAsync(corrected);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 2);
    }

    [Fact]
    public async Task Category_reassignment_during_discovery_rolls_back_instead_of_acquiring_locks_out_of_order()
    {
        var gate = new CheckoutPause(command => command.Contains("FROM catalog.\"Categories\"", StringComparison.Ordinal)
            && command.Contains("FOR SHARE", StringComparison.Ordinal));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var token = await BrowserRequests.TokenAsync(customer);
        gate.Enabled = true;
        var pending = CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "category-reassigned", token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // Checkout holds only its private claim at this boundary. This writer need not
            // wait for a business lock held by the paused request.
            await using var edit = Database.CreateContext();
            var category = Category.Create("New category", DateTimeOffset.UnixEpoch);
            edit.Categories.Add(category);
            var product = await edit.Products.SingleAsync();
            product.Update("New snapshot", "", UsdMoney.FromDecimal(7.25m), category.Id, true, product.Version);
            await edit.SaveChangesAsync();
        }
        finally { gate.Release.TrySetResult(); }
        using var changed = await pending;
        await AuthenticationTests.Problem(changed, 409, "state_conflict");
        await CheckoutTestData.AssertCountsAsync(Database, 0, 0);
        gate.Enabled = false;
        using var retry = await CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "category-reassigned");
        var order = await CheckoutTestData.CreatedAsync(retry);
        Assert.Equal("7.25", order.Total);
        Assert.Equal("New snapshot", Assert.Single(order.Items).Name);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }

    [Fact]
    public async Task Deadline_is_sampled_after_lock_wait_not_at_request_arrival()
    {
        var gate = new CheckoutPause();
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var token = await BrowserRequests.TokenAsync(customer);
        gate.Enabled = true;
        var pending = CheckoutTestData.PostAsync(customer, CheckoutTestData.Body(products), "logical-lock-wait", token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            clock.Advance(TimeSpan.FromMinutes(20));
        }
        finally { gate.Release.TrySetResult(); }
        using var response = await pending;
        var order = await CheckoutTestData.CreatedAsync(response);
        Assert.Equal(clock.GetUtcNow(), order.CreatedAt);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15), order.ExpiresAt);
        await CheckoutTestData.AssertCountsAsync(Database, 1, 1);
    }
}

public sealed class CheckoutRaceGate(bool claim) : CommandRaceGate(command => claim
    ? command.Contains("INSERT INTO messaging.\"IdempotencyRecords\"", StringComparison.Ordinal)
    : command.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal) && command.Contains("FOR UPDATE", StringComparison.Ordinal))
{
    public ConcurrentQueue<Guid> Contexts { get; } = new();
    protected override void Observe(DbCommand command, CommandEventData eventData) => Contexts.Enqueue(eventData.Context!.ContextId.InstanceId);
}

public sealed class CheckoutWriteProbe : DbCommandInterceptor
{
    private int writes;
    public int ReservationWrites => Volatile.Read(ref writes);
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("INSERT INTO inventory.\"InventoryReservations\"", StringComparison.Ordinal)) Interlocked.Increment(ref writes);
        return ValueTask.FromResult(result);
    }
}

public sealed class CheckoutPause(Func<string, bool>? matches = null) : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && (matches?.Invoke(command.CommandText) ??
            (command.CommandText.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal)
             && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))))
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}
