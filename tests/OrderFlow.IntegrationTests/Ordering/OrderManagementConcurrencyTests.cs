using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Api.Auth;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class OrderManagementConcurrencyTests(PostgresServer server) : PostgresTest(server)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_hosts_cancelling_the_same_order_release_every_reservation_only_once(bool expired)
    {
        var gate = new OrderMutationGate();
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var firstHost = new AuthApiFactory(Database, catalogInterceptor: gate, clock: clock);
        await using var secondHost = new AuthApiFactory(Database, catalogInterceptor: gate, clock: clock);
        var products = await CheckoutTestData.StockAsync(Database, firstHost);
        using var first = firstHost.Browser(); using var second = secondHost.Browser();
        var credentials = new { email = AuthenticationTests.Email(), password = AuthenticationTests.Password() };
        using (var registered = await BrowserRequests.PostAuthAsync(first, "register", credentials, await BrowserRequests.TokenAsync(first)))
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        foreach (var browser in new[] { first, second })
        {
            using var login = await BrowserRequests.PostAuthAsync(browser, "login", credentials, await BrowserRequests.TokenAsync(browser));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
        var order = await OrderManagementTestData.CreateAsync(first, CheckoutTestData.Body(products.Reverse(), 2));
        var tokenOne = await BrowserRequests.TokenAsync(first); var tokenTwo = await BrowserRequests.TokenAsync(second);
        if (expired) clock.Advance(TimeSpan.FromMinutes(15));
        gate.Enabled = true;
        var responses = await Task.WhenAll(OrderManagementTestData.CancelAsync(first, order.Id, tokenOne),
            OrderManagementTestData.CancelAsync(second, order.Id, tokenTwo));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(2, gate.Contexts.Distinct().Count());
        foreach (var response in responses)
        {
            if (expired) await AuthenticationTests.Problem(response, 409, "order_expired");
            else Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }
        await using var read = Database.CreateContext();
        var saved = await read.Orders.SingleAsync();
        Assert.Equal(expired ? OrderStatus.Expired : OrderStatus.Cancelled, saved.Status); Assert.Equal(2, saved.Version);
        Assert.Equal(2, await read.OrderStatusHistory.CountAsync()); Assert.Equal(2, await read.OutboxMessages.CountAsync());
        Assert.Equal(2, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(0, row.Reserved); Assert.Equal(10, row.OnHand); Assert.Equal(3, row.Revision); });
        Assert.All(await read.InventoryReservations.ToListAsync(), row => Assert.Equal(ReservationStatus.Released, row.Status));
        Assert.Single(gate.InventoryContexts.Distinct()); // Only the winner reaches stock locks.
    }

    [Fact]
    public async Task Concurrent_admin_transitions_with_the_same_version_cannot_advance_twice()
    {
        var gate = new OrderMutationGate();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer");
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var customer = client; using var admin = adminClient;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        await OrderManagementTestData.SeedStatusAsync(Database, order.Id, "Confirmed");
        var token = await BrowserRequests.TokenAsync(admin);
        gate.Enabled = true;
        var responses = await Task.WhenAll(OrderManagementTestData.AdvanceAsync(admin, order.Id, "Processing", 2, token),
            OrderManagementTestData.AdvanceAsync(admin, order.Id, "Processing", 2, token));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(new[] { 200, 409 }, responses.Select(response => (int)response.StatusCode).Order());
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict) await AuthenticationTests.Problem(response, 409, "version_conflict");
            response.Dispose();
        }
        await using var read = Database.CreateContext();
        var final = await read.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Processing, final.Status); Assert.Equal(3, final.Version);
        Assert.Equal(3, await read.OrderStatusHistory.CountAsync()); Assert.Equal(2, await read.OutboxMessages.CountAsync());
        Assert.Empty(gate.InventoryContexts);
    }

    [Fact]
    public async Task Distinct_orders_sharing_stock_cancel_in_canonical_product_order_without_losing_releases()
    {
        var gate = new OrderMutationGate();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var one = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        var two = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Reverse(), 3));
        var token = await BrowserRequests.TokenAsync(customer);
        gate.Enabled = true;
        var responses = await Task.WhenAll(OrderManagementTestData.CancelAsync(customer, one.Id, token), OrderManagementTestData.CancelAsync(customer, two.Id, token));
        Assert.Equal(2, gate.Participants);
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose(); }
        foreach (var trace in gate.InventoryTrace.GroupBy(row => row.ContextId))
            Assert.Equal(products.Select(product => product.Id), trace.Select(row => row.ProductId));
        Assert.Equal(2, gate.InventoryTrace.Select(row => row.ContextId).Distinct().Count());
        await using var read = Database.CreateContext();
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(0, row.Reserved); Assert.Equal(10, row.OnHand); Assert.Equal(5, row.Revision); });
        Assert.Equal(4, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        Assert.Equal(4, await read.OrderStatusHistory.CountAsync()); Assert.Equal(4, await read.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Cancellation_uses_time_after_waiting_for_the_order_lock()
    {
        var probe = new OrderLockArrival();
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: probe, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 1);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        var token = await BrowserRequests.TokenAsync(customer);
        await using var blocker = Database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await OrderLocking.LockAsync(blocker, order.Id, null, default);
        probe.Enabled = true;
        var pending = OrderManagementTestData.CancelAsync(customer, order.Id, token);
        try
        {
            await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            clock.Advance(TimeSpan.FromMinutes(15));
        }
        finally { await transaction.CommitAsync(); }
        using var response = await pending;
        await AuthenticationTests.Problem(response, 409, "order_expired");
        await using var read = Database.CreateContext();
        Assert.Equal(OrderStatus.Expired, (await read.Orders.SingleAsync()).Status);
        Assert.Equal(0, (await read.InventoryBalances.SingleAsync()).Reserved);
        Assert.Equal(clock.GetUtcNow(), (await read.InventoryReservations.SingleAsync()).ReleasedAt);
    }
}

public sealed class OrderMutationGate : DbCommandInterceptor
{
    private readonly RaceRendezvous rendezvous = new();
    public bool Enabled { get; set; }
    public int Participants => rendezvous.Participants;
    public ConcurrentQueue<Guid> Contexts { get; } = new();
    public ConcurrentQueue<Guid> InventoryContexts { get; } = new();
    public ConcurrentQueue<(Guid ContextId, Guid ProductId)> InventoryTrace { get; } = new();
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return result;
        var context = eventData.Context!.ContextId.InstanceId;
        if (command.CommandText.Contains("FROM ordering.\"Orders\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            Contexts.Enqueue(context);
            // Before the FIRST exclusive order lock, never behind it at an inventory barrier.
            await rendezvous.ArriveAsync(context, cancellationToken);
        }
        if (command.CommandText.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            InventoryContexts.Enqueue(context);
            InventoryTrace.Enqueue((context, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<Guid>().First()));
        }
        return result;
    }
}

public sealed class OrderLockArrival : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("FROM ordering.\"Orders\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Reached.TrySetResult();
        return ValueTask.FromResult(result);
    }
}
