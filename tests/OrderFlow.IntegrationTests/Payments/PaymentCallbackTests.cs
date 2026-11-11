using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class PaymentCallbackTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly ManualTimeProvider clock = new(Start);
    private const string Path = "/api/payments/simulator/callback";

    private AuthApiFactory Host(DbCommandInterceptor? gate = null) => new(Database, clock: clock, callbackSecret: secret, catalogInterceptor: gate);
    private async Task<(HttpClient Owner, PaymentAttempt Attempt)> ArrangeAsync(AuthApiFactory host)
    {
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer");
        var products = await CheckoutTestData.StockAsync(Database, host, count: 2);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        using var accepted = await PaymentInitiationTests.InitiateAsync(owner, order.Id);
        await PaymentInitiationTests.AcceptedAsync(accepted);
        await using var read = Database.CreateContext();
        return (owner, await read.PaymentAttempts.SingleAsync(a => a.OrderId == order.Id));
    }
    private static GatewayPaymentResult Result(PaymentAttempt attempt, GatewayPaymentState state = GatewayPaymentState.Succeeded,
        string? amount = null, string? capture = "capture-one") => new(attempt.ProviderRequestKey, attempt.Id, attempt.OrderId,
        "transaction-one", amount ?? attempt.Amount.ToString(), "USD", state, state == GatewayPaymentState.Succeeded ? capture : null);
    private static string Body(GatewayPaymentResult result, Guid? eventId = null, DateTimeOffset? occurredAt = null) =>
        JsonSerializer.Serialize(new { schemaVersion = 1, provider = "Simulator", providerEventId = eventId ?? Guid.NewGuid(),
            occurredAt = occurredAt ?? Start, payment = result }, GatewayJson.Options);
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string body, string? signingSecret = null,
        int age = 0, string? transmitted = null, bool signed = true, bool unknownLength = false)
    {
        var envelope = CallbackSigner.Sign("test-v1", signingSecret ?? secret, body, clock.GetUtcNow().AddSeconds(-age));
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = unknownLength ? new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(transmitted ?? body)))
                : new ByteArrayContent(Encoding.UTF8.GetBytes(transmitted ?? body))
        };
        request.Content.Headers.ContentType = new("application/json");
        if (signed)
        {
            request.Headers.Add("X-Payment-Key-Id", envelope.KeyId);
            request.Headers.Add("X-Payment-Timestamp", envelope.Timestamp.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Payment-Signature", envelope.Signature);
        }
        return await client.SendAsync(request);
    }
    private async Task<bool> SettleAsync(DbCommandInterceptor? gate = null)
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>(Database.Options());
        if (gate is not null) options.AddInterceptors(gate);
        await using var context = new OrderFlowDbContext(options.Options);
        return await new PaymentSettlementProcessor(context, clock, NullLogger<PaymentSettlementProcessor>.Instance).ProcessNextAsync(default);
    }
    private async Task ReconcileAsync(bool consumed)
    {
        await using var read = Database.CreateContext();
        var balances = await read.InventoryBalances.ToListAsync();
        var reservations = await read.InventoryReservations.ToListAsync();
        var movements = await read.StockMovements.ToListAsync();
        Assert.All(balances, b =>
        {
            Assert.Equal(consumed ? 8 : 10, b.OnHand); Assert.Equal(0, b.Reserved);
            Assert.Equal(movements.Where(m => m.ProductId == b.ProductId).Sum(m => m.OnHandDelta), b.OnHand);
            Assert.Equal(movements.Where(m => m.ProductId == b.ProductId).Sum(m => m.ReservedDelta), b.Reserved);
            Assert.Equal(reservations.Where(r => r.ProductId == b.ProductId && r.Status == ReservationStatus.Active).Sum(r => r.Quantity), b.Reserved);
        });
        Assert.All(reservations, r => Assert.Equal(consumed ? ReservationStatus.Consumed : ReservationStatus.Released, r.Status));
        Assert.Equal(2, movements.Count(m => m.Kind == (consumed ? StockMovementKind.Consumption : StockMovementKind.Release)));
        Assert.DoesNotContain(movements, m => m.Kind == (consumed ? StockMovementKind.Release : StockMovementKind.Consumption));
        var capture = await read.PaymentCaptures.SingleAsync();
        Assert.Equal(consumed, capture.Allocated);
        Assert.Equal(consumed ? 0 : 1, await read.PaymentRefunds.CountAsync());
        if (!consumed)
        {
            var refund = await read.PaymentRefunds.SingleAsync();
            Assert.Equal(capture.Id, refund.CaptureId); Assert.Equal(capture.Amount, refund.Amount);
            Assert.Equal("RefundPending", refund.Status);
        }
        Assert.Equal(PaymentAttemptStatus.Captured, (await read.PaymentAttempts.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("unsigned", 401)]
    [InlineData("forged", 401)]
    [InlineData("changed-bytes", 401)]
    [InlineData("stale", 401)]
    [InlineData("future", 401)]
    [InlineData("malformed", 400)]
    [InlineData("duplicate-field", 400)]
    [InlineData("oversized", 413)]
    [InlineData("oversized-stream", 413)]
    public async Task Authentication_schema_and_size_rejections_create_no_receipt(string scenario, int expected)
    {
        await using var host = Host(); using var client = host.Browser();
        var body = "{}";
        if (scenario.StartsWith("oversized", StringComparison.Ordinal)) body = new string(' ', PaymentCallback.MaximumBytes + 1);
        if (scenario == "duplicate-field") body = "{\"schemaVersion\":1,\"schemaVersion\":1}";
        using var response = await SendAsync(client, body, signingSecret: scenario == "forged" ? new string('x', 64) : null,
            age: scenario == "stale" ? 301 : scenario == "future" ? -301 : 0,
            transmitted: scenario == "changed-bytes" ? body + " " : null,
            signed: scenario != "unsigned", unknownLength: scenario == "oversized-stream");
        Assert.Equal(expected, (int)response.StatusCode);
        await using var read = Database.CreateContext(); Assert.Empty(await read.PaymentEvents.ToListAsync());
        Assert.Empty(await read.ProcessedMessages.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_exact_redelivery_is_durable_once_and_changed_authenticated_bytes_alert()
    {
        var gate = new ReceiptInsertGate();
        await using var host = Host(gate); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using var original = await PaymentInitiationTests.InitiateAsync(browser, attempt.OrderId);
        var originalBytes = await original.Content.ReadAsByteArrayAsync();
        using var machine = host.Browser(); var body = Body(Result(attempt));
        gate.Enabled = true;
        var replies = await Task.WhenAll(SendAsync(machine, body), SendAsync(browser, body));
        Assert.Equal(2, gate.Participants); gate.Enabled = false;
        foreach (var reply in replies) { Assert.Equal(HttpStatusCode.Accepted, reply.StatusCode); reply.Dispose(); }
        using (var conflict = await SendAsync(machine, body + " ")) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using (var read = Database.CreateContext())
        {
            Assert.Equal(1, await read.PaymentEvents.CountAsync()); Assert.Equal(1, await read.ProcessedMessages.CountAsync());
            Assert.Equal(Encoding.UTF8.GetBytes(body), (await read.PaymentEvents.SingleAsync()).RawBody);
            Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        }
        // A new processor/context can resume the committed inbox after receipt host/request scope ends.
        Assert.True(await SettleAsync()); Assert.False(await SettleAsync());
        await ReconcileAsync(consumed: true);
        using var replay = await PaymentInitiationTests.InitiateAsync(browser, attempt.OrderId);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(originalBytes, await replay.Content.ReadAsByteArrayAsync());
        Assert.Contains(host.Logs.Entries, entry => entry.Event.Id == 1601);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task Allocation_uses_processing_time_not_early_occurrence_or_receipt(int microseconds, bool onTime)
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var response = await SendAsync(browser, Body(Result(attempt), occurredAt: Start.AddSeconds(-10))))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(microseconds * 10));
        Assert.True(await SettleAsync()); await ReconcileAsync(onTime);
        await using var read = Database.CreateContext();
        Assert.Equal(onTime ? OrderStatus.Confirmed : OrderStatus.Expired, (await read.Orders.SingleAsync()).Status);
        var capture = await read.PaymentCaptures.SingleAsync(); Assert.Equal(Start.AddSeconds(-10), capture.ProviderOccurredAt);
        Assert.Equal(clock.GetUtcNow(), (await read.PaymentEvents.SingleAsync()).DecisionAt);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("unknown-attempt")]
    [InlineData("wrong-order")]
    [InlineData("request-key")]
    public async Task Authenticated_invalid_facts_never_confirm_and_known_USD_capture_is_refunded(string change)
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        var payment = Result(attempt);
        payment = change switch
        {
            "amount" => payment with { Amount = "49.99" },
            "currency" => payment with { Currency = "EUR" },
            "unknown-attempt" => payment with { AttemptId = Guid.NewGuid() },
            "wrong-order" => payment with { OrderId = Guid.NewGuid() },
            _ => payment with { RequestKey = "other-key" }
        };
        using var response = await SendAsync(browser, Body(payment)); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(await SettleAsync());
        await using var read = Database.CreateContext();
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        Assert.All(await read.InventoryBalances.ToListAsync(), b => { Assert.Equal(10, b.OnHand); Assert.Equal(2, b.Reserved); });
        if (change == "amount")
        {
            Assert.Equal("49.99", (await read.PaymentCaptures.SingleAsync()).Amount.ToString());
            Assert.Equal("49.99", (await read.PaymentRefunds.SingleAsync()).Amount.ToString());
            using var replacement = await PaymentInitiationTests.InitiateAsync(browser, attempt.OrderId, "replace-mismatched-capture");
            Assert.NotEqual(attempt.Id, (await PaymentInitiationTests.AcceptedAsync(replacement)).Id);
        }
        else
        {
            Assert.Equal(PaymentEventStatus.Quarantined, (await read.PaymentEvents.SingleAsync()).Status);
            Assert.Equal(InboxStatus.Failed, (await read.ProcessedMessages.SingleAsync()).Status);
            Assert.Empty(await read.PaymentCaptures.ToListAsync()); Assert.Empty(await read.PaymentRefunds.ToListAsync());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_success_reordering_and_new_event_for_same_capture_cannot_regress_facts(bool failureFirst)
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        var success = Result(attempt); var failure = Result(attempt, GatewayPaymentState.Failed);
        foreach (var payment in failureFirst ? new[] { failure, success, success, failure } : new[] { success, failure, success, failure })
        {
            using var accepted = await SendAsync(browser, Body(payment)); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.True(await SettleAsync());
        }
        await ReconcileAsync(true);
        await using var read = Database.CreateContext();
        Assert.Equal(2, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(1, await read.OutboxMessages.CountAsync(o => o.EventType == "payments.payment-captured"));
    }

    [Fact]
    public async Task Distinct_surplus_captures_each_have_one_full_compensation_and_transaction_mismatch_is_quarantined()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        foreach (var capture in new[] { "capture-one", "capture-two", "capture-two", "capture-three" })
        {
            using var accepted = await SendAsync(browser, Body(Result(attempt, capture: capture))); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.True(await SettleAsync());
        }
        using (var accepted = await SendAsync(browser, Body(Result(attempt) with { TransactionId = "different-transaction" })))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await SettleAsync());
        await using var read = Database.CreateContext();
        Assert.Equal(3, await read.PaymentCaptures.CountAsync()); Assert.Equal(2, await read.PaymentRefunds.CountAsync());
        Assert.Equal(1, await read.PaymentCaptures.CountAsync(c => c.Allocated));
        Assert.Equal(1, await read.PaymentEvents.CountAsync(e => e.Status == PaymentEventStatus.Quarantined));
        Assert.Equal(2, await read.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Consumption));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_payment_race_with_cancellation_or_expiry_has_one_stock_effect(bool expire)
    {
        var gate = new CommandRaceGate(sql => sql.Contains("FROM ordering.\"Orders\"", StringComparison.Ordinal)
            && sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        await using var host = Host(gate); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        var csrf = await BrowserRequests.TokenAsync(browser);
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        if (expire) clock.Advance(TimeSpan.FromMinutes(15));
        gate.Enabled = true;
        var paymentTask = SettleAsync(gate);
        if (expire)
        {
            var options = new DbContextOptionsBuilder<OrderFlowDbContext>(Database.Options()).AddInterceptors(gate);
            await using var context = new OrderFlowDbContext(options.Options);
            await Task.WhenAll(paymentTask, new OrderExpiryProcessor(context, clock).ProcessNextAsync(default));
        }
        else
        {
            using var cancelled = await OrderManagementTestData.CancelAsync(browser, attempt.OrderId, csrf);
            Assert.Contains((int)cancelled.StatusCode, new[] { 200, 409 });
            await paymentTask;
        }
        Assert.Equal(2, gate.Participants);
        await using var read = Database.CreateContext(); var order = await read.Orders.SingleAsync();
        Assert.Contains(order.Status, expire ? new[] { OrderStatus.Expired } : new[] { OrderStatus.Confirmed, OrderStatus.Cancelled });
        await ReconcileAsync(order.Status == OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Callback_settles_while_submission_response_is_in_flight_and_old_response_is_fenced()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        var gateway = new CallbackRaceGateway(Result(attempt, GatewayPaymentState.Failed));
        using var services = PaymentInitiationTests.Processing(Database, clock, gateway);
        var processing = services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default);
        try
        {
            await gateway.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var accepted = await SendAsync(browser, Body(Result(attempt))); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.True(await SettleAsync());
        }
        finally { gateway.Release.TrySetResult(); }
        Assert.True(await processing); await ReconcileAsync(true);
        await using var read = Database.CreateContext(); var saved = await read.PaymentAttempts.SingleAsync();
        Assert.Equal(GatewayPaymentState.Succeeded, saved.ObservedState); Assert.Null(saved.LeaseToken);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public async Task Exact_delivery_skew_boundary_is_allowed(int seconds)
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using var response = await SendAsync(browser, Body(Result(attempt)), age: seconds);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Two_settlement_contexts_reaching_the_order_lock_do_not_consume_twice()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var gate = new OrderMutationGate { Enabled = true };
        await Task.WhenAll(SettleAsync(gate), SettleAsync(gate));
        Assert.Equal(2, gate.Participants); Assert.Equal(2, gate.Contexts.Distinct().Count());
        await ReconcileAsync(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eligibility_is_sampled_after_order_lock_and_kept_while_waiting_for_inventory(bool pauseInventory)
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        if (pauseInventory)
        {
            var pause = new CheckoutPause { Enabled = true };
            var pending = SettleAsync(pause);
            try { await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); clock.Advance(TimeSpan.FromMinutes(15)); }
            finally { pause.Release.TrySetResult(); }
            Assert.True(await pending);
        }
        else
        {
            await using var blocker = Database.CreateContext();
            await using var transaction = await blocker.Database.BeginTransactionAsync();
            await blocker.Orders.FromSqlInterpolated($"SELECT * FROM ordering.\"Orders\" WHERE \"Id\" = {attempt.OrderId} FOR UPDATE").ToListAsync();
            var arrival = new OrderLockArrival { Enabled = true };
            var pending = SettleAsync(arrival);
            try { await arrival.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); clock.Advance(TimeSpan.FromMinutes(15)); }
            finally { await transaction.CommitAsync(); }
            Assert.True(await pending);
        }
        await ReconcileAsync(pauseInventory);
        await using var read = Database.CreateContext();
        Assert.Equal(pauseInventory ? Start : clock.GetUtcNow(), (await read.PaymentEvents.SingleAsync()).DecisionAt);
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_executed_consumption_and_capture_but_keeps_durable_receipt_for_retry()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var setup = Database.CreateContext();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION payments.reject_test_confirmation() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."EventType"='ordering.order-confirmed' THEN RAISE EXCEPTION 'Injected outbox failure'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_TestConfirmation" BEFORE INSERT ON messaging."OutboxMessages"
            FOR EACH ROW EXECUTE FUNCTION payments.reject_test_confirmation();
            """);
        var probe = new ConsumptionWriteProbe();
        try { await Assert.ThrowsAsync<DbUpdateException>(() => SettleAsync(probe)); }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER "TR_TestConfirmation" ON messaging."OutboxMessages";
                DROP FUNCTION payments.reject_test_confirmation();
                """);
        }
        Assert.Equal(2, probe.Writes);
        await using (var read = Database.CreateContext())
        {
            Assert.Empty(await read.PaymentCaptures.ToListAsync()); Assert.Empty(await read.PaymentRefunds.ToListAsync());
            Assert.Equal(PaymentEventStatus.Pending, (await read.PaymentEvents.SingleAsync()).Status);
            Assert.Equal(InboxStatus.Pending, (await read.ProcessedMessages.SingleAsync()).Status);
            Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
            Assert.Equal(PaymentAttemptStatus.Queued, (await read.PaymentAttempts.SingleAsync()).Status);
            Assert.All(await read.InventoryBalances.ToListAsync(), b => { Assert.Equal(10, b.OnHand); Assert.Equal(2, b.Reserved); });
            Assert.Equal(1, await read.OutboxMessages.CountAsync());
        }
        Assert.True(await SettleAsync()); await ReconcileAsync(true);
    }

    [Fact]
    public async Task Database_rejects_capture_regression_receipt_mutation_and_uncompensated_capture()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await SettleAsync());
        foreach (var sql in new[]
        {
            "UPDATE payments.\"PaymentEvents\" SET \"RawBody\" = decode('7b7d','hex')",
            "UPDATE payments.\"PaymentAttempts\" SET \"Status\" = 'Failed'",
            "UPDATE payments.\"PaymentCaptures\" SET \"Amount\" = \"Amount\" + 1",
            "UPDATE inventory.\"InventoryReservations\" SET \"Status\" = 'Active', \"ConsumedAt\" = NULL",
            "UPDATE inventory.\"InventoryReservations\" SET \"ConsumedAt\" = \"ConsumedAt\" + interval '1 second'"
        })
        {
            await using var context = Database.CreateContext();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal("55000", failure.SqlState);
        }
        await using (var context = Database.CreateContext())
        {
            // Deferred constraint executes at commit, after all intentions should have been inserted.
            var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payments."PaymentCaptures"
                    ("Id","Provider","ProviderCaptureId","ProviderTransactionId","AttemptId","OrderId","ProviderEventId",
                     "Amount","Currency","Allocated","ProviderOccurredAt","RecordedAt")
                SELECT {Guid.NewGuid()},"Provider",'uncompensated',"ProviderTransactionId","AttemptId","OrderId","ProviderEventId",
                     "Amount","Currency",false,"ProviderOccurredAt","RecordedAt" FROM payments."PaymentCaptures"
                """));
            Assert.Equal("23514", failure.SqlState);
        }
        await ReconcileAsync(true);
    }

    [Fact]
    public async Task Failed_attempt_later_captures_after_replacement_and_surplus_attempt_is_refunded()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt, GatewayPaymentState.Failed))))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await SettleAsync());
        using var replacementResponse = await PaymentInitiationTests.InitiateAsync(browser, attempt.OrderId, "replacement");
        var replacementView = await PaymentInitiationTests.AcceptedAsync(replacementResponse);
        await using var read = Database.CreateContext();
        var replacement = await read.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == replacementView.Id);
        foreach (var result in new[] { Result(attempt), Result(replacement, capture: "replacement-capture") with { TransactionId = "replacement-transaction" } })
        {
            using var accepted = await SendAsync(browser, Body(result)); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.True(await SettleAsync());
        }
        Assert.Equal(2, await read.PaymentCaptures.CountAsync()); Assert.Equal(1, await read.PaymentRefunds.CountAsync());
        Assert.Equal(1, await read.PaymentCaptures.CountAsync(c => c.Allocated));
        Assert.Equal(2, await read.StockMovements.CountAsync(m => m.Kind == StockMovementKind.Consumption));
        Assert.All(await read.PaymentAttempts.ToListAsync(), a => Assert.Equal(PaymentAttemptStatus.Captured, a.Status));
        Assert.Equal(OrderStatus.Confirmed, (await read.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Known_cancelled_order_records_capture_and_unique_refund_without_resurrection()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var cancelled = await OrderManagementTestData.CancelAsync(browser, attempt.OrderId)) Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var body = Body(Result(attempt));
        for (var i = 0; i < 2; i++)
        {
            using var accepted = await SendAsync(browser, body); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }
        Assert.True(await SettleAsync()); await ReconcileAsync(false);
        await using var read = Database.CreateContext(); Assert.Equal(OrderStatus.Cancelled, (await read.Orders.SingleAsync()).Status);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => read.Database.ExecuteSqlRawAsync(
            "UPDATE ordering.\"Orders\" SET \"Status\" = 'Confirmed'"));
        Assert.Equal("55000", failure.SqlState);
    }

    [Fact]
    public async Task Classified_transient_processing_failures_have_bounded_durable_retries()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        using (var accepted = await SendAsync(browser, Body(Result(attempt)))) Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var fault = new SettlementTransientFault();
        for (var i = 1; i <= 8; i++)
        {
            Assert.True(await SettleAsync(fault));
            await using var read = Database.CreateContext(); var inbox = await read.ProcessedMessages.SingleAsync();
            Assert.Equal(i, inbox.AttemptCount); Assert.Equal(i == 8 ? InboxStatus.Failed : InboxStatus.Pending, inbox.Status);
            Assert.Equal("postgres_40P01", inbox.LastErrorCode);
            Assert.Empty(await read.PaymentCaptures.ToListAsync());
            Assert.False(await SettleAsync(fault)); // No immediate retry loop.
            clock.Advance(TimeSpan.FromSeconds(5));
        }
        Assert.False(await SettleAsync(fault));
        await using var final = Database.CreateContext(); Assert.Equal(PaymentEventStatus.Pending, (await final.PaymentEvents.SingleAsync()).Status);
    }

    [Fact]
    public async Task Inbox_insert_failure_rolls_back_receipt_and_redelivery_can_retry()
    {
        await using var host = Host(); var (owner, attempt) = await ArrangeAsync(host); using var browser = owner;
        await using var setup = Database.CreateContext();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION payments.reject_test_inbox() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected inbox failure'; END; $body$;
            CREATE TRIGGER "TR_TestInbox" BEFORE INSERT ON messaging."ProcessedMessages"
            FOR EACH ROW EXECUTE FUNCTION payments.reject_test_inbox();
            """);
        var body = Body(Result(attempt));
        try
        {
            using var failed = await SendAsync(browser, body); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            await using var read = Database.CreateContext(); Assert.Empty(await read.PaymentEvents.ToListAsync());
            Assert.Empty(await read.ProcessedMessages.ToListAsync());
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER "TR_TestInbox" ON messaging."ProcessedMessages";
                DROP FUNCTION payments.reject_test_inbox();
                """);
        }
        using var accepted = await SendAsync(browser, body); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await SettleAsync()); await ReconcileAsync(true);
    }
}

internal sealed class SettlementTransientFault : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM ordering.\"Orders\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            throw new PostgresException("Injected classified transient failure", "ERROR", "ERROR", "40P01");
        return ValueTask.FromResult(result);
    }
}

internal sealed class ReceiptInsertGate : DbCommandInterceptor
{
    private readonly RaceRendezvous rendezvous = new();
    public bool Enabled { get; set; }
    public int Participants => rendezvous.Participants;
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("INSERT INTO payments.\"PaymentEvents\"", StringComparison.Ordinal))
            await rendezvous.ArriveAsync(eventData.Context!.ContextId.InstanceId, cancellationToken);
        return result;
    }
}

internal sealed class ConsumptionWriteProbe : DbCommandInterceptor
{
    public int Writes { get; private set; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("UPDATE inventory.\"Balances\"", StringComparison.Ordinal)) Writes++;
        return ValueTask.FromResult(result);
    }
}

internal sealed class CallbackRaceGateway(GatewayPaymentResult result) : IPaymentGateway
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); return result;
    }
    public Task<GatewayPaymentResult?> LookupAsync(string requestKey, CancellationToken cancellationToken) => Task.FromResult<GatewayPaymentResult?>(null);
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<GatewayRefundResult?> LookupRefundAsync(string requestKey, CancellationToken cancellationToken) => throw new NotSupportedException();
}
