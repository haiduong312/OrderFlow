using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class PaymentRecoveryTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider clock = new(Start);

    private ServiceProvider Services(IPaymentGateway gateway, DbCommandInterceptor? interceptor = null)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(gateway);
        services.AddDbContext<OrderFlowDbContext>(options =>
        {
            PersistenceRegistration.Configure(options, Database.ConnectionString);
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        services.AddSingleton<PaymentProcessingIteration>(); services.AddSingleton<PaymentReconciliationIteration>();
        services.AddSingleton<RefundProcessingIteration>();
        return services.BuildServiceProvider(validateScopes: true);
    }
    private async Task<bool> ReconcileAsync(IPaymentGateway gateway, DbCommandInterceptor? interceptor = null)
    {
        using var services = Services(gateway, interceptor);
        return await services.GetRequiredService<PaymentReconciliationIteration>().ProcessNextAsync(default);
    }
    private async Task<bool> RefundAsync(IPaymentGateway gateway, DbCommandInterceptor? interceptor = null)
    {
        using var services = Services(gateway, interceptor);
        return await services.GetRequiredService<RefundProcessingIteration>().ProcessNextAsync(default);
    }
    private async Task<bool> SettleCallbackAsync(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>(Database.Options());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        await using var db = new OrderFlowDbContext(options.Options);
        return await new PaymentSettlementProcessor(db, clock, NullLogger<PaymentSettlementProcessor>.Instance).ProcessNextAsync(default);
    }
    private async Task<(HttpClient Owner, PaymentAttempt Attempt)> ArrangeAsync(AuthApiFactory host, SimulatorTestHost simulator,
        string scenario = "Succeeded", bool cancelled = true)
    {
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer");
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        using (var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id)) await PaymentInitiationTests.AcceptedAsync(response);
        await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.AsNoTracking().SingleAsync();
        await simulator.ScenarioAsync(attempt.ProviderRequestKey, scenario);
        using (var gateway = simulator.Gateway(TimeSpan.FromSeconds(1)))
        using (var services = Services(gateway))
            Assert.True(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        if (cancelled)
        {
            using var cancel = await OrderManagementTestData.CancelAsync(owner, order.Id);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }
        clock.Advance(TimeSpan.FromSeconds(5));
        return (owner, attempt);
    }
    private async Task<PaymentRefund> CreateLateRefundAsync(IPaymentGateway gateway)
    {
        Assert.True(await ReconcileAsync(gateway));
        await using var read = Database.CreateContext();
        var capture = await read.PaymentCaptures.SingleAsync(); Assert.False(capture.Allocated);
        Assert.Equal("ProviderLookup", capture.Source); Assert.Null(capture.ProviderOccurredAt); Assert.Null(capture.ProviderEventId);
        Assert.Equal(OrderStatus.Cancelled, (await read.Orders.SingleAsync()).Status);
        return await read.PaymentRefunds.AsNoTracking().SingleAsync();
    }
    private async Task RefundScenarioAsync(SimulatorTestHost simulator, string key, string scenario)
    {
        using var client = simulator.Client(control: true);
        using var response = await client.PutAsJsonAsync("/control/refunds/" + key + "/scenario", new { scenario });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private async Task AssertCompensatedAsync(PostgresDatabase providerDatabase, Guid refundId)
    {
        await using var db = Database.CreateContext(); await using var provider = providerDatabase.CreateContext();
        var refund = await db.PaymentRefunds.SingleAsync(); var actual = await provider.ProviderRefunds.SingleAsync();
        Assert.Equal(refundId, refund.Id); Assert.Equal(PaymentRefundStatus.Succeeded, refund.Status);
        Assert.Equal(actual.RefundId, refund.ProviderRefundId); Assert.Equal(refund.ProviderRefundKey, actual.RequestKey);
        Assert.Equal(refund.ProviderCaptureId, actual.CaptureId); Assert.Equal(refund.Amount, actual.Amount);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(e => e.EventType == "payments.refund-succeeded"));
        Assert.Equal(1, await db.PaymentCaptures.CountAsync()); Assert.Equal(1, await db.PaymentRefunds.CountAsync());
        var balance = await db.InventoryBalances.SingleAsync(); Assert.Equal(10, balance.OnHand); Assert.Equal(0, balance.Reserved);
        Assert.Equal(ReservationStatus.Released, (await db.InventoryReservations.SingleAsync()).Status);
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Release));
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Consumption));
        var ledger = await db.StockMovements.ToListAsync(); Assert.Equal(balance.OnHand, ledger.Sum(m => m.OnHandDelta));
        Assert.Equal(balance.Reserved, ledger.Sum(m => m.ReservedDelta));
    }
    private static async Task DeliverAsync(AuthApiFactory host, SimulatorTestHost simulator, string key)
    {
        using var control = simulator.Client(control: true);
        using var response = await control.PostAsync("/control/payments/" + key + "/callback", null);
        var envelope = (await response.Content.ReadFromJsonAsync<SignedSimulatorCallback>())!;
        using var merchant = host.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/simulator/callback")
        { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(envelope.Body)) };
        request.Headers.Add("X-Payment-Key-Id", envelope.KeyId); request.Headers.Add("X-Payment-Timestamp", envelope.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Payment-Signature", envelope.Signature);
        // AuthApiFactory's dedicated test key ID; simulator defaults to development-v1.
        request.Headers.Remove("X-Payment-Key-Id"); request.Headers.Add("X-Payment-Key-Id", "test-v1");
        using var accepted = await merchant.SendAsync(request); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("TimeoutAfterCapture")]
    public async Task Lost_refund_response_recovers_by_lookup_after_real_provider_restart_with_same_identity(string paymentScenario)
    {
        await using var provider = await Server.CreateDatabaseAsync();
        PaymentRefund intention; string providerRefundId;
        await using (var simulator = await SimulatorTestHost.StartAsync(provider, clock))
        await using (var host = new AuthApiFactory(Database, clock: clock))
        {
            var (owner, _) = await ArrangeAsync(host, simulator, paymentScenario); using var browser = owner;
            using var gateway = simulator.Gateway(TimeSpan.FromSeconds(1));
            intention = await CreateLateRefundAsync(gateway);
            await RefundScenarioAsync(simulator, intention.ProviderRefundKey, "TimeoutAfterSuccess");
            Assert.True(await RefundAsync(gateway));
            await using var read = Database.CreateContext(); var pending = await read.PaymentRefunds.SingleAsync();
            Assert.Equal(PaymentRefundStatus.Pending, pending.Status); Assert.Equal("gateway_timeout", pending.LastErrorCode);
            Assert.Equal(1, pending.DeliveryCount); Assert.Equal(intention.ProviderRefundKey, pending.ProviderRefundKey);
            await using var independent = provider.CreateContext(); providerRefundId = (await independent.ProviderRefunds.SingleAsync()).RefundId;
            Assert.False(await RefundAsync(gateway)); // Backoff is a durable due-time predicate.
        }
        var recovery = FailureEvidence.Start();
        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var restarted = await SimulatorTestHost.StartAsync(provider, clock))
        {
            using var gateway = restarted.Gateway(); Assert.True(await RefundAsync(gateway)); Assert.False(await RefundAsync(gateway));
        }
        await AssertCompensatedAsync(provider, intention.Id);
        await using var final = Database.CreateContext(); Assert.Equal(providerRefundId, (await final.PaymentRefunds.SingleAsync()).ProviderRefundId);
        FailureEvidence.Record(recovery, "refund-timeout-" + paymentScenario, new { logical_backoff_advance_seconds = 5,
            provider_refunds = 1, local_succeeded_refunds = 1, original_identity_preserved = true });
    }

    [Fact]
    public async Task Provider_commit_before_local_ack_failure_survives_new_worker_and_preserves_refund_key()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        await using var setup = Database.CreateContext();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION payments.reject_refund_ack_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status"='Succeeded' THEN RAISE EXCEPTION 'Injected acknowledgement loss'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_TestRefundAck" BEFORE UPDATE ON payments."Refunds" FOR EACH ROW EXECUTE FUNCTION payments.reject_refund_ack_test();
            """);
        try { await Assert.ThrowsAsync<DbUpdateException>(() => RefundAsync(gateway)); }
        finally { await setup.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_TestRefundAck\" ON payments.\"Refunds\"; DROP FUNCTION payments.reject_refund_ack_test();"); }
        await using (var read = Database.CreateContext())
        {
            var claimed = await read.PaymentRefunds.SingleAsync(); Assert.NotNull(claimed.LeaseToken);
            Assert.Equal(PaymentRefundStatus.Pending, claimed.Status); Assert.Null(claimed.ProviderRefundId);
            Assert.Equal(0, await read.OutboxMessages.CountAsync(e => e.EventType == "payments.refund-succeeded"));
        }
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(await RefundAsync(gateway)); await AssertCompensatedAsync(provider, intention.Id);
    }

    [Fact]
    public async Task Two_workers_reach_refund_claim_but_only_one_owns_it_and_network_has_no_merchant_transaction()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        var probe = new RefundTransactionProbe(gateway, Database);
        var gate = new CommandRaceGate(sql => sql.Contains("FROM payments.\"Refunds\"", StringComparison.Ordinal)
            && sql.Contains("SKIP LOCKED", StringComparison.Ordinal)) { Enabled = true };
        var results = await Task.WhenAll(RefundAsync(probe, gate), RefundAsync(probe, gate));
        Assert.Equal(2, gate.Participants); Assert.Single(results, r => r); Assert.Equal(2, probe.NetworkCalls);
        await AssertCompensatedAsync(provider, intention.Id);
    }

    [Fact]
    public async Task Expired_lease_reclaim_fences_old_worker_after_same_effect_was_already_refunded()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        var pause = new PauseRefundResult(gateway); var old = RefundAsync(pause);
        var recovery = FailureEvidence.Start();
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); clock.Advance(TimeSpan.FromSeconds(60));
            Assert.True(await RefundAsync(gateway));
        }
        finally { pause.Release.TrySetResult(); }
        Assert.True(await old); await AssertCompensatedAsync(provider, intention.Id);
        await using var read = Database.CreateContext(); Assert.Equal(2, (await read.PaymentRefunds.SingleAsync()).DeliveryCount);
        FailureEvidence.Record(recovery, "two-worker-refund-lease", new { workers = 2, logical_lease_advance_seconds = 60,
            provider_refunds = 1, delivery_count = 2, stale_owner_fenced = true });
    }

    [Fact]
    public async Task Callback_and_lookup_rendezvous_before_order_lock_and_settle_capture_only_once()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock, callbackSecret: simulator.SigningKey);
        var (owner, attempt) = await ArrangeAsync(host, simulator, cancelled: false); using var browser = owner;
        await DeliverAsync(host, simulator, attempt.ProviderRequestKey);
        var gate = new OrderMutationGate { Enabled = true }; using var gateway = simulator.Gateway();
        await Task.WhenAll(ReconcileAsync(gateway, gate), SettleCallbackAsync(gate));
        Assert.Equal(2, gate.Participants); Assert.Equal(2, gate.Contexts.Distinct().Count());
        await using var read = Database.CreateContext(); Assert.Equal(OrderStatus.Confirmed, (await read.Orders.SingleAsync()).Status);
        Assert.Equal(1, await read.PaymentCaptures.CountAsync()); Assert.Empty(await read.PaymentRefunds.ToListAsync());
        Assert.Equal(1, await read.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Consumption));
        Assert.Equal(8, (await read.InventoryBalances.SingleAsync()).OnHand); Assert.Equal(0, (await read.InventoryBalances.SingleAsync()).Reserved);
        Assert.Equal(2, await read.OrderStatusHistory.CountAsync());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Failed")]
    public async Task Pending_and_failed_lookup_are_correlated_without_inventing_capture(string state)
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, attempt) = await ArrangeAsync(host, simulator, "Pending", cancelled: false); using var browser = owner;
        if (state == "Failed")
        {
            using var control = simulator.Client(control: true);
            using var resolved = await control.PostAsJsonAsync("/control/payments/" + attempt.ProviderRequestKey + "/resolve", new { state });
            Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);
        }
        using var gateway = simulator.Gateway(); Assert.True(await ReconcileAsync(gateway)); Assert.False(await ReconcileAsync(gateway));
        await using var read = Database.CreateContext(); var saved = await read.PaymentAttempts.SingleAsync();
        Assert.Equal(state == "Failed" ? PaymentAttemptStatus.Failed : PaymentAttemptStatus.Pending, saved.Status);
        Assert.Equal(2, saved.DeliveryCount); Assert.Null(saved.LeaseToken); Assert.NotNull(saved.ReconciledAt);
        Assert.Empty(await read.PaymentCaptures.ToListAsync()); Assert.Empty(await read.PaymentRefunds.ToListAsync());
        Assert.Equal(2, (await read.InventoryBalances.SingleAsync()).Reserved);
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("attempt")]
    [InlineData("key")]
    [InlineData("currency")]
    public async Task Lookup_cannot_bypass_reference_and_currency_validation(string wrong)
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, attempt) = await ArrangeAsync(host, simulator, cancelled: false); using var browser = owner;
        using var gateway = simulator.Gateway(); var result = (await gateway.LookupAsync(attempt.ProviderRequestKey, default))!;
        result = wrong switch
        {
            "order" => result with { OrderId=Guid.NewGuid() }, "attempt" => result with { AttemptId=Guid.NewGuid() },
            "key" => result with { RequestKey="foreign-key" }, _ => result with { Currency="EUR" }
        };
        var mismatch = new LookupResultGateway(result);
        Assert.True(await ReconcileAsync(mismatch)); Assert.False(await ReconcileAsync(mismatch));
        await using var read = Database.CreateContext(); var saved = await read.PaymentAttempts.SingleAsync();
        Assert.True(saved.NeedsAttention); Assert.NotNull(saved.LastErrorCode); Assert.Null(saved.LeaseToken);
        Assert.Empty(await read.PaymentCaptures.ToListAsync()); Assert.Empty(await read.PaymentRefunds.ToListAsync());
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        Assert.Equal(2, (await read.InventoryBalances.SingleAsync()).Reserved);
    }

    [Fact]
    public async Task Known_USD_amount_mismatch_is_full_compensation_and_repeated_late_callback_cannot_refund_again()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock, callbackSecret: simulator.SigningKey);
        var (owner, attempt) = await ArrangeAsync(host, simulator, "MismatchedAmount"); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        Assert.Equal("24.99", intention.Amount.ToString()); Assert.True(await RefundAsync(gateway));
        await DeliverAsync(host, simulator, attempt.ProviderRequestKey); Assert.True(await SettleCallbackAsync());
        await DeliverAsync(host, simulator, attempt.ProviderRequestKey); Assert.False(await SettleCallbackAsync());
        Assert.False(await ReconcileAsync(gateway)); Assert.False(await RefundAsync(gateway));
        await AssertCompensatedAsync(provider, intention.Id);
    }

    [Fact]
    public async Task Missing_lookup_retries_without_resubmitting_then_exhausted_uncertainty_needs_manual_review()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator, "TimeoutBeforeAccept", cancelled: false); using var browser = owner;
        using var gateway = simulator.Gateway();
        for (var delivery = 2; delivery <= 8; delivery++)
        {
            Assert.True(await ReconcileAsync(gateway)); Assert.False(await ReconcileAsync(gateway));
            await using var read = Database.CreateContext(); var saved = await read.PaymentAttempts.SingleAsync();
            Assert.Equal(PaymentAttemptStatus.Unknown, saved.Status); Assert.Equal(delivery, saved.DeliveryCount);
            if (delivery < 8) clock.Advance(saved.NextAttemptAt - clock.GetUtcNow());
            else Assert.Equal("reconciliation_exhausted", saved.LastErrorCode);
        }
        await using var merchant = Database.CreateContext(); Assert.True((await merchant.PaymentAttempts.SingleAsync()).NeedsAttention);
        await using var providerRead = provider.CreateContext(); Assert.Empty(await providerRead.ProviderPayments.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permanent_rejection_or_retry_exhaustion_leaves_durable_refund_review(bool exhaust)
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        if (!exhaust) await RefundScenarioAsync(simulator, intention.ProviderRefundKey, "Rejected");
        var uncertain = new UnavailableRefundGateway();
        for (var delivery = 1; delivery <= (exhaust ? 8 : 1); delivery++)
        {
            Assert.True(await RefundAsync(exhaust ? uncertain : gateway));
            await using var read = Database.CreateContext(); var saved = await read.PaymentRefunds.SingleAsync();
            Assert.Equal(delivery, saved.DeliveryCount); Assert.Equal(intention.ProviderRefundKey, saved.ProviderRefundKey);
            if (exhaust && delivery < 8) clock.Advance(saved.NextAttemptAt - clock.GetUtcNow());
        }
        Assert.False(await RefundAsync(gateway));
        await using var final = Database.CreateContext(); var review = await final.PaymentRefunds.SingleAsync();
        Assert.Equal(PaymentRefundStatus.NeedsReview, review.Status); Assert.NotNull(review.LastErrorCode); Assert.Null(review.LeaseToken);
        await using var providerRead = provider.CreateContext(); Assert.Empty(await providerRead.ProviderRefunds.ToListAsync());
    }

    [Fact]
    public async Task Shutdown_after_provider_effect_cancels_wait_and_next_worker_recovers_expired_lease()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator); using var browser = owner;
        using var gateway = simulator.Gateway(); var intention = await CreateLateRefundAsync(gateway);
        var pause = new PauseRefundResult(gateway); using var services = Services(pause);
        using var shutdown = new CancellationTokenSource();
        var pending = services.GetRequiredService<RefundProcessingIteration>().ProcessNextAsync(shutdown.Token);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); shutdown.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally { pause.Release.TrySetResult(); }
        await using (var read = Database.CreateContext())
        {
            var claimed = await read.PaymentRefunds.SingleAsync(); Assert.NotNull(claimed.LeaseToken);
            Assert.Equal(PaymentRefundStatus.Pending, claimed.Status); Assert.Null(claimed.ProviderRefundId);
        }
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(await RefundAsync(gateway)); await AssertCompensatedAsync(provider, intention.Id);
    }

    [Fact]
    public async Task Reconciliation_at_exact_deadline_expires_releases_and_compensates_without_callback()
    {
        await using var provider = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await ArrangeAsync(host, simulator, cancelled: false); using var browser = owner;
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(5));
        using var gateway = simulator.Gateway(); Assert.True(await ReconcileAsync(gateway));
        await using var read = Database.CreateContext(); Assert.Equal(OrderStatus.Expired, (await read.Orders.SingleAsync()).Status);
        var intention = await read.PaymentRefunds.SingleAsync(); Assert.True(await RefundAsync(gateway));
        await AssertCompensatedAsync(provider, intention.Id);
    }

    [Fact]
    public async Task Upgrade_preserves_existing_compensation_identity_amount_and_authenticated_capture()
    {
        await using var legacy = await Server.CreateDatabaseAsync("20261111040724_AddSignedPaymentSettlement");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "upgrade@example.invalid", CreatedAt = Start };
        var claim = IdempotencyRecord.Create(user.Id, CheckoutRequest.Operation, "legacy-refund", "{}", Start);
        claim.Complete(201, "application/json", "{}"u8.ToArray(), "/api/orders/legacy", Start);
        var category = Category.Create("Legacy", Start);
        var product = Product.Create("LEGACY-REFUND", "Legacy", "", UsdMoney.FromDecimal(25m), category.Id, Start);
        var order = Order.Create(user.Id, claim.Id, [new(product.Id, product.Sku, product.Name, product.Price, 1)], Start);
        var attemptId = Guid.NewGuid(); var eventId = Guid.NewGuid(); var captureId = Guid.NewGuid(); var refundId = Guid.NewGuid();
        var key = "refund-" + refundId.ToString("N"); var paymentKey = "payment-" + attemptId.ToString("N");
        await using (var db = legacy.CreateContext())
        {
            db.AddRange(user, claim, category, product, order); await db.SaveChangesAsync();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO payments."PaymentAttempts"
                        ("Id","OrderId","Provider","ProviderRequestKey","Amount","Currency","Status","CreatedAt","UpdatedAt","NextAttemptAt","DeliveryCount","NeedsAttention","ProviderTransactionId","ProviderCaptureId","ObservedState")
                    VALUES ({attemptId},{order.Id},'Simulator',{paymentKey},25,'USD','Captured',{Start},{Start},{Start},0,false,'legacy-txn','legacy-capture','Succeeded');
                    INSERT INTO payments."PaymentEvents"
                        ("Provider","ProviderEventId","RawBody","PayloadHash","ReferencedOrderId","OccurredAt","ReceivedAt","DecisionAt","Status")
                    VALUES ('Simulator',{eventId},{"{}"u8.ToArray()},{SHA256.HashData("{}"u8)},{order.Id},{Start},{Start},{Start},'Processed');
                    INSERT INTO payments."PaymentCaptures"
                        ("Id","Provider","ProviderCaptureId","ProviderTransactionId","AttemptId","OrderId","ProviderEventId","Amount","Currency","Allocated","ProviderOccurredAt","RecordedAt")
                    VALUES ({captureId},'Simulator','legacy-capture','legacy-txn',{attemptId},{order.Id},{eventId},25,'USD',false,{Start},{Start});
                    INSERT INTO payments."Refunds"
                        ("Id","CaptureId","Provider","ProviderCaptureId","Purpose","Status","ProviderRefundKey","Amount","Currency","Reason","CreatedAt")
                    VALUES ({refundId},{captureId},'Simulator','legacy-capture','FullCompensation','RefundPending',{key},25,'USD','legacy-compensation',{Start});
                    """);
                await transaction.CommitAsync();
            }
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        }
        await using var read = legacy.CreateContext(); var refund = await read.PaymentRefunds.SingleAsync();
        Assert.Equal(refundId, refund.Id); Assert.Equal(key, refund.ProviderRefundKey); Assert.Equal("25.00", refund.Amount.ToString());
        Assert.Equal(PaymentRefundStatus.Pending, refund.Status); Assert.Equal(Start, refund.NextAttemptAt); Assert.Equal(Start, refund.UpdatedAt);
        var capture = await read.PaymentCaptures.SingleAsync(); Assert.Equal(captureId, capture.Id); Assert.Equal(eventId, capture.ProviderEventId);
        Assert.Equal("Callback", capture.Source); Assert.Equal(Start, capture.ProviderOccurredAt);
        Assert.Equal(0, refund.DeliveryCount); Assert.NotEqual(Guid.Empty, refund.Claim(Start));
        await read.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => read.Database.ExecuteSqlRawAsync("UPDATE payments.\"Refunds\" SET \"Amount\"=24"));
        Assert.Equal("55000", error.SqlState);
    }
}

internal sealed class LookupResultGateway(GatewayPaymentResult result) : IPaymentGateway
{
    public Task<GatewayPaymentResult?> LookupAsync(string key, CancellationToken token) => Task.FromResult<GatewayPaymentResult?>(result);
    public Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task<GatewayRefundResult?> LookupRefundAsync(string key, CancellationToken token) => throw new NotSupportedException();
}
internal sealed class UnavailableRefundGateway : IPaymentGateway
{
    public Task<GatewayPaymentResult?> LookupAsync(string key, CancellationToken token) => throw new NotSupportedException();
    public Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken token) => throw new GatewayUnavailableException("gateway_timeout");
    public Task<GatewayRefundResult?> LookupRefundAsync(string key, CancellationToken token) => throw new GatewayUnavailableException("gateway_timeout");
}
internal sealed class PauseRefundResult(IPaymentGateway inner) : IPaymentGateway
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken token)
    {
        var result = await inner.RefundAsync(request, token); Reached.TrySetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        return result with { RefundId="stale-worker-must-not-overwrite" };
    }
    public Task<GatewayRefundResult?> LookupRefundAsync(string key, CancellationToken token) => inner.LookupRefundAsync(key, token);
    public Task<GatewayPaymentResult?> LookupAsync(string key, CancellationToken token) => inner.LookupAsync(key, token);
    public Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken token) => inner.SubmitAsync(request, token);
}
internal sealed class RefundTransactionProbe(IPaymentGateway inner, PostgresDatabase database) : IPaymentGateway
{
    private int calls; public int NetworkCalls => calls;
    private async Task AssertNoTransactionAsync(string key, CancellationToken token)
    {
        Interlocked.Increment(ref calls);
        await using var db = database.CreateContext(); await using var transaction = await db.Database.BeginTransactionAsync(token);
        var rows = await db.PaymentRefunds.FromSqlInterpolated($"SELECT * FROM payments.\"Refunds\" WHERE \"ProviderRefundKey\" = {key} FOR UPDATE NOWAIT").ToListAsync(token);
        var refund = Assert.Single(rows); Assert.NotNull(refund.LeaseToken); Assert.Equal(1, refund.DeliveryCount);
        await transaction.RollbackAsync(token);
    }
    public async Task<GatewayRefundResult?> LookupRefundAsync(string key, CancellationToken token)
    { await AssertNoTransactionAsync(key, token); return await inner.LookupRefundAsync(key, token); }
    public async Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken token)
    { await AssertNoTransactionAsync(request.RequestKey, token); return await inner.RefundAsync(request, token); }
    public Task<GatewayPaymentResult?> LookupAsync(string key, CancellationToken token) => inner.LookupAsync(key, token);
    public Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken token) => inner.SubmitAsync(request, token);
}
