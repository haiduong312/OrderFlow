using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class OrderExpiryTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private IHost Worker(TimeProvider clock, int batchSize = 25, DbCommandInterceptor? probe = null, bool scheduled = false) =>
        WorkerHost.Build(["--environment", "Production"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = Database.ConnectionString,
                ["OrderExpiry:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["OrderExpiry:PollIntervalSeconds"] = "300"
            });
            builder.Services.RemoveAll<TimeProvider>();
            builder.Services.AddSingleton(clock);
            if (probe is not null) builder.Services.AddDbContext<OrderFlowDbContext>(options => options.AddInterceptors(probe));
        }, runBackgroundJobs: scheduled);

    private static Task<OrderExpiryResult> RunAsync(IHost host, CancellationToken cancellationToken = default) =>
        host.Services.GetRequiredService<OrderExpiryIteration>().ProcessAsync(cancellationToken);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public async Task Deadline_uses_injected_UTC_microsecond_boundary_and_reprocessing_is_a_noop(int microseconds, int expected)
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(microseconds * 10));
        using var worker = Worker(clock);
        Assert.Equal(expected, (await RunAsync(worker)).Expired);
        Assert.Equal(0, (await RunAsync(worker)).Expired);
        await AssertStateAsync(expected, 1, expected == 0 ? 2 : 0);
        await using var read = Database.CreateContext();
        var saved = await read.Orders.SingleAsync();
        Assert.Equal(expected == 0 ? OrderStatus.PendingPayment : OrderStatus.Expired, saved.Status);
        Assert.Equal(expected + 1, saved.Version);
        if (expected == 1)
        {
            var fact = await read.OrderStatusHistory.SingleAsync(row => row.Version == 2);
            Assert.Equal(OrderActorKind.System, fact.ActorKind); Assert.Null(fact.ActorUserId);
            Assert.Equal(OrderStatus.PendingPayment, fact.FromStatus); Assert.Equal(clock.GetUtcNow(), fact.OccurredAt);
            Assert.Equal("Payment deadline elapsed", fact.Reason);
            Assert.All(await read.StockMovements.Where(row => row.Kind == StockMovementKind.Release).ToListAsync(), row =>
            { Assert.Equal(StockActorKind.System, row.ActorKind); Assert.Null(row.ActorUserId); });
            // Current HTTP reads must serialize the new nullable System actor correctly.
            using var detail = await customer.GetAsync(OrderManagementTestData.Customer(order.Id));
            Assert.Equal(System.Net.HttpStatusCode.OK, detail.StatusCode);
            var body = (await detail.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
            Assert.Equal("Expired", body.Status); Assert.Equal(2, body.Version);
            Assert.Equal("Expired", body.Expiry.EffectiveStatus); Assert.False(body.Expiry.CanCancel);
            Assert.Equal("System", body.History[^1].ActorKind); Assert.Null(body.History[^1].ActorUserId);
        }
    }

    [Fact]
    public async Task Bounded_iteration_commits_each_order_and_a_new_host_resumes_the_backlog()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        for (var i = 0; i < 3; i++) await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        clock.Advance(TimeSpan.FromMinutes(15));
        using (var first = Worker(clock, batchSize: 1)) Assert.Equal(1, (await RunAsync(first)).Expired);
        await AssertStateAsync(1, 3, 2);
        using (var restarted = Worker(clock, batchSize: 1))
        {
            Assert.Equal(1, (await RunAsync(restarted)).Expired);
            Assert.Equal(1, (await RunAsync(restarted)).Expired);
            Assert.Equal(0, (await RunAsync(restarted)).Expired);
        }
        await AssertStateAsync(3, 3, 0);
    }

    [Fact]
    public async Task Nonpending_and_newer_orders_and_unrelated_stock_are_untouched()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory, count: 3);
        var cancelled = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Take(1)));
        using var cancel = await OrderManagementTestData.CancelAsync(customer, cancelled.Id);
        var paid = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Take(1)));
        await OrderManagementTestData.SeedStatusAsync(Database, paid.Id, "Confirmed");
        var due = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Skip(1).Take(1)));
        clock.Advance(TimeSpan.FromMinutes(14));
        var future = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Skip(1).Take(1), 2));
        clock.Advance(TimeSpan.FromMinutes(1));
        using var worker = Worker(clock);
        Assert.Equal(1, (await RunAsync(worker)).Expired);
        await using var read = Database.CreateContext();
        var orders = await read.Orders.ToDictionaryAsync(row => row.Id);
        Assert.Equal(OrderStatus.Cancelled, orders[cancelled.Id].Status);
        Assert.Equal(OrderStatus.Confirmed, orders[paid.Id].Status);
        Assert.Equal(OrderStatus.PendingPayment, orders[future.Id].Status);
        Assert.Equal(OrderStatus.Expired, orders[due.Id].Status);
        Assert.Equal(2, orders[cancelled.Id].Version); Assert.Equal(2, orders[paid.Id].Version); Assert.Equal(1, orders[future.Id].Version);
        var balances = await read.InventoryBalances.ToDictionaryAsync(row => row.ProductId);
        Assert.Equal(1, balances[products[0].Id].Reserved); // Confirmed is test-only status setup, not payment consumption.
        Assert.Equal(2, balances[products[1].Id].Reserved);
        Assert.Equal(0, balances[products[2].Id].Reserved); Assert.Equal(1, balances[products[2].Id].Revision);
        Assert.Equal(1, await read.OutboxMessages.CountAsync(row => row.EventType == "ordering.order-expired"));
        await AssertReconciledAsync(read);
    }

    [Fact]
    public async Task Two_worker_hosts_claim_distinct_orders_before_contending_for_shared_inventory()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products.Reverse(), 3));
        clock.Advance(TimeSpan.FromMinutes(15));
        var gate = new ClaimedOrdersGate();
        using var one = Worker(clock, 1, gate); using var two = Worker(clock, 1, gate);
        var results = await Task.WhenAll(RunAsync(one), RunAsync(two));
        Assert.All(results, result => { Assert.Equal(1, result.Expired); Assert.Equal(0, result.Contended); });
        Assert.Equal(2, gate.Participants);
        Assert.Equal(2, gate.Contexts.Distinct().Count());
        await AssertStateAsync(2, 2, 0);
    }

    [Fact]
    public async Task Locked_due_order_is_skipped_without_waiting_and_is_discovered_on_the_next_iteration()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products));
        clock.Advance(TimeSpan.FromMinutes(15));
        await using (var blocker = Database.CreateContext())
        await using (var transaction = await blocker.Database.BeginTransactionAsync())
        {
            await OrderLocking.LockAsync(blocker, order.Id, null, default);
            using var worker = Worker(clock);
            Assert.Equal(0, (await RunAsync(worker).WaitAsync(TimeSpan.FromSeconds(10))).Expired);
            await AssertStateAsync(0, 1, 1);
            await transaction.RollbackAsync();
        }
        using var restarted = Worker(clock);
        Assert.Equal(1, (await RunAsync(restarted)).Expired);
        await AssertStateAsync(1, 1, 0);
    }

    [Fact]
    public async Task Expiry_and_customer_cancellation_reach_the_same_contested_order_and_release_once()
    {
        var clock = new ManualTimeProvider(Start);
        var gate = new OrderMutationGate();
        await using var factory = new AuthApiFactory(Database, clock: clock, catalogInterceptor: gate);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        var token = await BrowserRequests.TokenAsync(customer);
        clock.Advance(TimeSpan.FromMinutes(15));
        gate.Enabled = true;
        using var worker = Worker(clock, 1, gate);
        var expiry = RunAsync(worker);
        using var response = await OrderManagementTestData.CancelAsync(customer, order.Id, token);
        await AuthenticationTests.Problem(response, 409, "order_expired");
        Assert.InRange((await expiry).Expired, 0, 1);
        Assert.Equal(2, gate.Participants); Assert.Equal(2, gate.Contexts.Distinct().Count());
        Assert.Single(gate.InventoryContexts.Distinct());
        await AssertStateAsync(1, 1, 0);
    }

    [Fact]
    public async Task Cancellation_decided_before_deadline_keeps_its_result_while_expiry_skips_the_locked_order()
    {
        var clock = new ManualTimeProvider(Start);
        var pause = new PauseBeforeInventory();
        await using var factory = new AuthApiFactory(Database, clock: clock, catalogInterceptor: pause);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        var token = await BrowserRequests.TokenAsync(customer);
        clock.Advance(TimeSpan.FromMinutes(14)); pause.Enabled = true;
        var pending = OrderManagementTestData.CancelAsync(customer, order.Id, token);
        try
        {
            // Cancellation already owns the order lock and sampled 14:00, but has not locked stock.
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromMinutes(1));
            using var worker = Worker(clock);
            Assert.Equal(0, (await RunAsync(worker).WaitAsync(TimeSpan.FromSeconds(10))).Expired);
        }
        finally { pause.Continue.TrySetResult(); }
        using var response = await pending;
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var restarted = Worker(clock);
        Assert.Equal(0, (await RunAsync(restarted)).Expired);
        await using var read = Database.CreateContext();
        var saved = await read.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, saved.Status); Assert.Equal(2, saved.Version);
        Assert.Equal(2, await read.OrderStatusHistory.CountAsync()); Assert.Equal(2, await read.OutboxMessages.CountAsync());
        Assert.Equal(0, await read.OutboxMessages.CountAsync(row => row.EventType == "ordering.order-expired"));
        Assert.Equal(2, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        Assert.All(await read.InventoryReservations.ToListAsync(), row =>
        { Assert.Equal(ReservationReleaseReason.Cancelled, row.ReleaseReason); Assert.Equal(Start.AddMinutes(14), row.ReleasedAt); });
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(10, row.OnHand); Assert.Equal(0, row.Reserved); });
        await AssertReconciledAsync(read);
    }

    [Fact]
    public async Task Only_active_reservations_are_released_and_existing_terminal_facts_remain_unchanged()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, actor) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15));
        Guid releasedId;
        // Deliberate legacy/partial-state fixture: one reservation was already released.
        // Normal application writers always transition the entire order atomically.
        await using (var setup = Database.CreateContext())
        {
            var reservation = await setup.InventoryReservations.SingleAsync(row => row.ProductId == products[0].Id);
            var balance = await setup.InventoryBalances.SingleAsync(row => row.ProductId == products[0].Id);
            setup.StockMovements.Add(balance.Release(reservation, ReservationReleaseReason.Expired, actor, clock.GetUtcNow())!);
            await setup.SaveChangesAsync(); releasedId = reservation.Id;
        }
        using var worker = Worker(clock);
        Assert.Equal(1, (await RunAsync(worker)).Expired);
        Assert.Equal(0, (await RunAsync(worker)).Expired);
        await AssertStateAsync(1, 1, 0);
        await using var read = Database.CreateContext();
        var oldMovement = await read.StockMovements.SingleAsync(row => row.ReservationId == releasedId && row.Kind == StockMovementKind.Release);
        Assert.Equal(actor, oldMovement.ActorUserId); Assert.Equal(StockActorKind.Customer, oldMovement.ActorKind);
    }

    [Fact]
    public async Task Classified_contention_is_reported_and_the_next_iteration_uses_a_fresh_scope()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15));
        var fault = new BalanceContentionOnce();
        using var worker = Worker(clock, probe: fault);
        Assert.Equal(new OrderExpiryResult(0, 1), await RunAsync(worker));
        await AssertStateAsync(0, 1, 2);
        Assert.Equal(new OrderExpiryResult(1, 0), await RunAsync(worker));
        Assert.Equal(2, fault.Contexts.Distinct().Count());
        await AssertStateAsync(1, 1, 0);
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_real_release_writes_then_a_new_host_recovers()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15));
        var probe = new ReleaseWriteProbe();
        await using var connection = new NpgsqlConnection(Database.ConnectionString); await connection.OpenAsync();
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION messaging.reject_test_expiry() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW."EventType" = 'ordering.order-expired' THEN RAISE EXCEPTION 'deliberate expiry outbox failure'; END IF;
                RETURN NEW;
            END; $body$;
            CREATE TRIGGER "TR_TestExpiry" BEFORE INSERT ON messaging."OutboxMessages"
            FOR EACH ROW EXECUTE FUNCTION messaging.reject_test_expiry();
            """, connection)) await install.ExecuteNonQueryAsync();
        using (var worker = Worker(clock, probe: probe))
            await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(worker));
        Assert.Equal(2, probe.Writes);
        await AssertStateAsync(0, 1, 2);
        await using (var remove = new NpgsqlCommand("DROP TRIGGER \"TR_TestExpiry\" ON messaging.\"OutboxMessages\"; DROP FUNCTION messaging.reject_test_expiry();", connection))
            await remove.ExecuteNonQueryAsync();
        using var restarted = Worker(clock);
        Assert.Equal(1, (await RunAsync(restarted)).Expired);
        await AssertStateAsync(1, 1, 0);
    }

    [Fact]
    public async Task Stopping_the_scheduled_worker_mid_release_rolls_back_and_restart_finishes_once()
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (client, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var customer = client;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        await OrderManagementTestData.CreateAsync(customer, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15));
        var pause = new PauseAfterRelease();
        using (var worker = Worker(clock, probe: pause, scheduled: true))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StartAsync(timeout.Token);
            try { await pause.Reached.Task.WaitAsync(timeout.Token); }
            finally { await worker.StopAsync(timeout.Token); }
        }
        await AssertStateAsync(0, 1, 2);
        using var restarted = Worker(clock);
        Assert.Equal(1, (await RunAsync(restarted)).Expired);
        Assert.Equal(0, (await RunAsync(restarted)).Expired);
        await AssertStateAsync(1, 1, 0);
    }

    [Fact]
    public async Task Scheduled_worker_completes_a_pass_and_stop_interrupts_its_long_poll_delay()
    {
        var clock = new ObservedDelayClock(Start.AddMinutes(15));
        using var worker = Worker(clock, scheduled: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(timeout.Token);
        try { await clock.DelayScheduled.Task.WaitAsync(timeout.Token); }
        finally { await worker.StopAsync(timeout.Token); }
        Assert.True(worker.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);
    }

    private async Task AssertStateAsync(int expiredOrders, int totalOrders, int reservedPerProduct)
    {
        await using var read = Database.CreateContext();
        Assert.Equal(totalOrders, await read.Orders.CountAsync());
        Assert.Equal(expiredOrders, await read.Orders.CountAsync(row => row.Status == OrderStatus.Expired && row.Version == 2));
        Assert.Equal(totalOrders + expiredOrders, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(totalOrders + expiredOrders, await read.OutboxMessages.CountAsync());
        Assert.Equal(0, await read.Orders.CountAsync(row => row.Status != OrderStatus.Expired && (row.Status != OrderStatus.PendingPayment || row.Version != 1)));
        Assert.Equal(expiredOrders, await read.OutboxMessages.CountAsync(row => row.EventType == "ordering.order-expired" && row.AggregateVersion == 2));
        var reservations = await read.InventoryReservations.ToListAsync();
        Assert.Equal(expiredOrders * 2, reservations.Count(row => row.Status == ReservationStatus.Released));
        Assert.Equal(expiredOrders * 2, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        Assert.All(reservations.Where(row => row.Status == ReservationStatus.Released), row =>
        { Assert.NotNull(row.ReleasedAt); Assert.Equal(ReservationReleaseReason.Expired, row.ReleaseReason); });
        Assert.All(reservations.Where(row => row.Status == ReservationStatus.Active), row => Assert.Null(row.ReleasedAt));
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(10, row.OnHand); Assert.Equal(reservedPerProduct, row.Reserved); });
        await AssertReconciledAsync(read);
    }

    private static async Task AssertReconciledAsync(OrderFlowDbContext read)
    {
        var reservations = await read.InventoryReservations.AsNoTracking().ToListAsync();
        var ledger = await read.StockMovements.AsNoTracking().ToListAsync();
        foreach (var balance in await read.InventoryBalances.AsNoTracking().ToListAsync())
        {
            Assert.InRange(balance.Reserved, 0, balance.OnHand);
            Assert.Equal(reservations.Where(row => row.ProductId == balance.ProductId && row.Status == ReservationStatus.Active).Sum(row => row.Quantity), balance.Reserved);
            Assert.Equal(ledger.Where(row => row.ProductId == balance.ProductId).Sum(row => row.ReservedDelta), balance.Reserved);
            Assert.Equal(ledger.Where(row => row.ProductId == balance.ProductId).Sum(row => row.OnHandDelta), balance.OnHand);
            Assert.Equal(ledger.Where(row => row.ProductId == balance.ProductId).Max(row => row.Revision), balance.Revision);
        }
        Assert.All(ledger.Where(row => row.Kind == StockMovementKind.Release).GroupBy(row => row.ReservationId), group => Assert.Single(group));
    }
}

internal sealed class ClaimedOrdersGate : DbCommandInterceptor
{
    private readonly RaceRendezvous gate = new();
    public int Participants => gate.Participants;
    public ConcurrentQueue<Guid> Contexts { get; } = new();
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("SKIP LOCKED", StringComparison.Ordinal) && result.HasRows)
        {
            Contexts.Enqueue(eventData.Context!.ContextId.InstanceId);
            // Each owns a DIFFERENT order due to SKIP LOCKED; neither owns inventory yet.
            await gate.ArriveAsync(eventData.Context.ContextId.InstanceId, cancellationToken);
        }
        return result;
    }
}

internal sealed class PauseAfterRelease : DbCommandInterceptor
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("UPDATE inventory.\"InventoryReservations\"", StringComparison.Ordinal))
        {
            Reached.TrySetResult();
            // A one-way observation, never a barrier waiting for another lock contender.
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}

internal sealed class PauseBeforeInventory : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            Reached.TrySetResult();
            await Continue.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}

internal sealed class BalanceContentionOnce : DbCommandInterceptor
{
    private int failed;
    public ConcurrentQueue<Guid> Contexts { get; } = new();
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM inventory.\"Balances\"", StringComparison.Ordinal))
        {
            Contexts.Enqueue(eventData.Context!.ContextId.InstanceId);
            if (Interlocked.Exchange(ref failed, 1) == 0)
                throw new PostgresException("Test-only lock contention", "ERROR", "ERROR", "55P03");
        }
        return ValueTask.FromResult(result);
    }
}

internal sealed class ObservedDelayClock(DateTimeOffset now) : TimeProvider
{
    public TaskCompletionSource DelayScheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override DateTimeOffset GetUtcNow() => now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        DelayScheduled.TrySetResult();
        return timer;
    }
}
