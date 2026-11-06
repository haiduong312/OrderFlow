using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Payments.Simulation;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class PaymentGatewayTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("TimeoutBeforeAccept")]
    [InlineData("TimeoutAfterCapture")]
    public async Task Real_HTTP_timeout_and_provider_restart_preserve_identity_and_do_not_allow_a_replacement(string scenario)
    {
        var clock = new ManualTimeProvider(Start);
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var initiation = await PaymentInitiationTests.InitiateAsync(owner, order.Id);
        var accepted = await PaymentInitiationTests.AcceptedAsync(initiation);
        await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.SingleAsync();
        string? capture = null; string? transactionId = null;
        await using (var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock))
        {
            await simulator.ScenarioAsync(attempt.ProviderRequestKey, scenario);
            using var gateway = simulator.Gateway(TimeSpan.FromSeconds(1));
            using var services = PaymentInitiationTests.Processing(Database, clock, gateway);
            Assert.True(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
            await read.Entry(attempt).ReloadAsync();
            Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status); Assert.Equal("gateway_timeout", attempt.LastErrorCode);
            Assert.False(attempt.NeedsAttention); Assert.Equal(1, attempt.DeliveryCount);
            await using var providerRead = providerDatabase.CreateContext();
            var saved = await providerRead.ProviderPayments.SingleOrDefaultAsync();
            Assert.Equal(scenario == "TimeoutAfterCapture", saved is not null);
            capture = saved?.CaptureId; transactionId = saved?.TransactionId;
        }
        using (var duplicate = await PaymentInitiationTests.InitiateAsync(owner, order.Id, "another-client-key"))
            Assert.Equal(accepted.Id, (await PaymentInitiationTests.AcceptedAsync(duplicate)).Id);
        Assert.Equal(1, await read.PaymentAttempts.CountAsync());
        // Provider state survives an actual separate HTTP-host shutdown/restart and merchant uncertainty.
        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var restarted = await SimulatorTestHost.StartAsync(providerDatabase, clock))
        {
            if (scenario == "TimeoutBeforeAccept") await restarted.ScenarioAsync(attempt.ProviderRequestKey, "Succeeded");
            using var gateway = restarted.Gateway();
            using var services = PaymentInitiationTests.Processing(Database, clock, gateway);
            Assert.True(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
            Assert.False(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        }
        await read.Entry(attempt).ReloadAsync();
        Assert.Equal(accepted.Id, attempt.Id); Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(GatewayPaymentState.Succeeded, attempt.ObservedState); Assert.True(attempt.NeedsAttention);
        Assert.Equal("settlement_pending", attempt.LastErrorCode); Assert.Equal(2, attempt.DeliveryCount);
        await using var finalProvider = providerDatabase.CreateContext(); var final = await finalProvider.ProviderPayments.SingleAsync();
        Assert.Equal(attempt.ProviderRequestKey, final.RequestKey); Assert.Equal(final.TransactionId, attempt.ProviderTransactionId);
        if (capture is not null) { Assert.Equal(capture, final.CaptureId); Assert.Equal(transactionId, final.TransactionId); }
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(10, row.OnHand); Assert.Equal(1, row.Reserved); });
        Assert.Equal(0, await read.StockMovements.CountAsync(row => row.Kind == StockMovementKind.Release));
        Assert.Equal(1, await read.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Mismatched_provider_amount_is_retained_for_reconciliation_and_blocks_new_attempts()
    {
        var clock = new ManualTimeProvider(Start);
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id); var accepted = await PaymentInitiationTests.AcceptedAsync(response);
        await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.SingleAsync();
        await simulator.ScenarioAsync(attempt.ProviderRequestKey, "MismatchedAmount");
        using var gateway = simulator.Gateway(); using var services = PaymentInitiationTests.Processing(Database, clock, gateway);
        Assert.True(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        await read.Entry(attempt).ReloadAsync();
        Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status); Assert.Equal("gateway_response_mismatch", attempt.LastErrorCode);
        Assert.True(attempt.NeedsAttention); Assert.Contains("24.99", attempt.LastObservation);
        Assert.Equal("25.00", attempt.Amount.ToString()); Assert.Null(attempt.ProviderCaptureId);
        using var duplicate = await PaymentInitiationTests.InitiateAsync(owner, order.Id, "new-key");
        Assert.Equal(accepted.Id, (await PaymentInitiationTests.AcceptedAsync(duplicate)).Id);
        Assert.False(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        Assert.Equal(1, await read.PaymentAttempts.CountAsync()); Assert.Equal(1, await read.OutboxMessages.CountAsync());
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        await using var providerRead = providerDatabase.CreateContext(); Assert.NotNull((await providerRead.ProviderPayments.SingleAsync()).CaptureId);
    }

    [Fact]
    public async Task Provider_idempotency_coordinates_concurrent_calls_and_rejects_changed_amounts()
    {
        var clock = new ManualTimeProvider(Start); var gate = new ProviderSubmissionGate();
        await using var simulator = await SimulatorTestHost.StartAsync(Database, clock, gate);
        var request = new GatewayPaymentRequest("provider-race", Guid.NewGuid(), Guid.NewGuid(), "10.00", "USD");
        await simulator.ScenarioAsync(request.RequestKey, "Succeeded");
        using var client = simulator.Client(); gate.Enabled = true;
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/provider/payments", request, GatewayJson.Options),
            client.PostAsJsonAsync("/provider/payments", request, GatewayJson.Options));
        var results = new List<GatewayPaymentResult>();
        foreach (var response in responses)
        { Assert.Equal(HttpStatusCode.OK, response.StatusCode); results.Add((await response.Content.ReadFromJsonAsync<GatewayPaymentResult>(GatewayJson.Options))!); response.Dispose(); }
        Assert.Equal(2, gate.Participants); Assert.Equal(results[0], results[1]);
        gate.Enabled = false;
        using var mismatch = await client.PostAsJsonAsync("/provider/payments", request with { Amount = "10.01" }, GatewayJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await using var read = Database.CreateContext(); Assert.Equal(1, await read.ProviderPayments.CountAsync());
        Assert.Equal(0, await read.Orders.CountAsync()); // Simulator has no merchant FK/write dependency.
    }

    [Fact]
    public async Task Callback_generation_is_control_authenticated_signed_and_redelivery_keeps_event_identity()
    {
        var clock = new ManualTimeProvider(Start);
        await using var simulator = await SimulatorTestHost.StartAsync(Database, clock);
        var request = new GatewayPaymentRequest("callback-payment", Guid.NewGuid(), Guid.NewGuid(), "10.00", "USD");
        await simulator.ScenarioAsync(request.RequestKey, "Succeeded");
        using var gateway = simulator.Gateway(); await gateway.SubmitAsync(request, default);
        using var anonymous = simulator.Client(authenticated: false); using var merchant = simulator.Client();
        foreach (var client in new[] { anonymous, merchant })
        {
            using var denied = await client.PostAsync("/control/payments/" + request.RequestKey + "/callback", null);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        using var control = simulator.Client(control: true);
        using var first = await control.PostAsync("/control/payments/" + request.RequestKey + "/callback", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var signed = (await first.Content.ReadFromJsonAsync<SignedSimulatorCallback>())!;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(simulator.SigningKey), Encoding.UTF8.GetBytes(signed.Timestamp + "." + signed.Body));
        Assert.Equal(Convert.ToHexStringLower(expected), signed.Signature);
        Assert.Contains("providerEventId", signed.Body); Assert.DoesNotContain(simulator.SigningKey, signed.Body);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var again = await control.PostAsync("/control/payments/" + request.RequestKey + "/callback", null);
        var redelivery = (await again.Content.ReadFromJsonAsync<SignedSimulatorCallback>())!;
        Assert.Equal(signed.Body, redelivery.Body); Assert.NotEqual(signed.Timestamp, redelivery.Timestamp); Assert.NotEqual(signed.Signature, redelivery.Signature);
        using var regress = await control.PostAsJsonAsync("/control/payments/" + request.RequestKey + "/resolve", new { state = "Failed" });
        Assert.Equal(HttpStatusCode.Conflict, regress.StatusCode);
    }

    [Fact]
    public async Task Provider_refund_replay_uses_stable_identity_and_rejects_changed_or_second_full_refunds()
    {
        var clock = new ManualTimeProvider(Start);
        await using var simulator = await SimulatorTestHost.StartAsync(Database, clock);
        var request = new GatewayPaymentRequest("refundable", Guid.NewGuid(), Guid.NewGuid(), "10.00", "USD");
        await simulator.ScenarioAsync(request.RequestKey, "Succeeded");
        using var gateway = simulator.Gateway(); var payment = await gateway.SubmitAsync(request, default);
        var refund = new GatewayRefundRequest("refund-stable-key", payment.CaptureId!, "10.00", "USD");
        var results = await Task.WhenAll(gateway.RefundAsync(refund, default), gateway.RefundAsync(refund, default));
        Assert.Equal(results[0], results[1]); Assert.Equal(results[0], await gateway.LookupRefundAsync(refund.RequestKey, default));
        using var client = simulator.Client();
        foreach (var invalid in new[] { refund with { Amount = "9.99" }, refund with { RequestKey = "second-full-refund" } })
        {
            using var response = await client.PostAsJsonAsync("/provider/refunds", invalid, GatewayJson.Options);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        await using var read = Database.CreateContext(); Assert.Equal(1, await read.ProviderRefunds.CountAsync());
    }

    [Fact]
    public async Task A_stale_lease_owner_cannot_overwrite_a_newer_success_observation()
    {
        var clock = new ManualTimeProvider(Start);
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var response = await PaymentInitiationTests.InitiateAsync(owner, order.Id); await PaymentInitiationTests.AcceptedAsync(response);
        using var firstGateway = simulator.Gateway(); using var secondGateway = simulator.Gateway();
        var pause = new PauseProviderObservation(firstGateway);
        using var one = PaymentInitiationTests.Processing(Database, clock, pause);
        using var two = PaymentInitiationTests.Processing(Database, clock, secondGateway);
        var pending = one.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            clock.Advance(TimeSpan.FromSeconds(60));
            await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.AsNoTracking().SingleAsync();
            using var control = simulator.Client(control: true);
            using var resolve = await control.PostAsJsonAsync("/control/payments/" + attempt.ProviderRequestKey + "/resolve", new { state = "Succeeded" });
            Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
            Assert.True(await two.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        }
        finally { pause.Continue.TrySetResult(); }
        Assert.True(await pending);
        await using var final = Database.CreateContext(); var saved = await final.PaymentAttempts.SingleAsync();
        Assert.Equal(GatewayPaymentState.Succeeded, saved.ObservedState); Assert.True(saved.NeedsAttention);
        Assert.Equal(PaymentAttemptStatus.Pending, saved.Status); Assert.NotNull(saved.ProviderCaptureId); Assert.Equal(2, saved.DeliveryCount);
        await using var providerRead = providerDatabase.CreateContext(); Assert.Equal(1, await providerRead.ProviderPayments.CountAsync());
    }
}

internal sealed class ProviderSubmissionGate : DbCommandInterceptor
{
    private readonly RaceRendezvous gate = new();
    public bool Enabled { get; set; }
    public int Participants => gate.Participants;
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            await gate.ArriveAsync(eventData.Context!.ContextId.InstanceId, cancellationToken);
        return result;
    }
}

internal sealed class PauseProviderObservation(IPaymentGateway inner) : IPaymentGateway
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await inner.SubmitAsync(request, cancellationToken); Reached.TrySetResult();
        await Continue.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); return result;
    }
    public Task<GatewayPaymentResult?> LookupAsync(string requestKey, CancellationToken cancellationToken) => inner.LookupAsync(requestKey, cancellationToken);
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) => inner.RefundAsync(request, cancellationToken);
    public Task<GatewayRefundResult?> LookupRefundAsync(string requestKey, CancellationToken cancellationToken) => inner.LookupRefundAsync(requestKey, cancellationToken);
}
