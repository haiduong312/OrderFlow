using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class PaymentInitiationTests(PostgresServer server) : PostgresTest(server)
{
    private static readonly DateTimeOffset Start = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static string Path(Guid orderId) => $"/api/orders/{orderId:D}/payment-attempts";
    internal static async Task<HttpResponseMessage> InitiateAsync(HttpClient client, Guid orderId, string? key = "pay-one", object? body = null,
        string? token = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(orderId)) { Content = JsonContent.Create(body ?? new { }) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", token ?? await BrowserRequests.TokenAsync(client));
        return await client.SendAsync(request);
    }

    internal static async Task<PaymentAttemptResponse> AcceptedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<PaymentAttemptResponse>())!;
        Assert.Equal(Path(result.OrderId) + "/" + result.Id.ToString("D"), response.Headers.Location!.ToString()); return result;
    }

    internal static ServiceProvider Processing(PostgresDatabase database, TimeProvider clock, IPaymentGateway gateway)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(clock); services.AddSingleton(gateway);
        services.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, database.ConnectionString));
        services.AddSingleton<PaymentProcessingIteration>(); return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Ownership_roles_authentication_and_antiforgery_are_enforced_without_provider_calls()
    {
        await using var factory = new AuthApiFactory(Database);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var (n, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var other = n;
        var (a, _) = await AuthenticatedBrowser.CreateAsync(factory, "Admin"); using var admin = a;
        using var anonymous = factory.Browser();
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        foreach (var (client, status, code) in new[] { (anonymous, 401, "unauthenticated"), (other, 404, "not_found"), (admin, 403, "forbidden") })
        {
            using var denied = await InitiateAsync(client, order.Id); await AuthenticationTests.Problem(denied, status, code);
        }
        using (var missing = await InitiateAsync(owner, order.Id, csrf: false)) await AuthenticationTests.Problem(missing, 400, "antiforgery_failed");
        using var accepted = await InitiateAsync(owner, order.Id); var attempt = await AcceptedAsync(accepted);
        foreach (var client in new[] { other, anonymous, admin })
        {
            using var denied = await client.GetAsync(Path(order.Id) + "/" + attempt.Id);
            Assert.Equal(client == other ? HttpStatusCode.NotFound : client == admin ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        await using var read = Database.CreateContext(); Assert.Equal(1, await read.PaymentAttempts.CountAsync());
        Assert.Equal(0, await read.ProviderPayments.CountAsync());
    }

    [Theory]
    [InlineData(-1, 202)]
    [InlineData(0, 409)]
    [InlineData(1, 409)]
    public async Task Eligibility_uses_exact_deadline_and_commits_expiry_before_returning_conflict(int microseconds, int status)
    {
        var clock = new ManualTimeProvider(Start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(microseconds * 10));
        using var response = await InitiateAsync(owner, order.Id); Assert.Equal(status, (int)response.StatusCode);
        if (status == 409) await AuthenticationTests.Problem(response, status, "order_expired");
        await using var read = Database.CreateContext();
        Assert.Equal(status == 202 ? 1 : 0, await read.PaymentAttempts.CountAsync());
        Assert.Equal(status == 202 ? OrderStatus.PendingPayment : OrderStatus.Expired, (await read.Orders.SingleAsync()).Status);
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(10, row.OnHand); Assert.Equal(status == 202 ? 2 : 0, row.Reserved); });
        Assert.Equal(status == 202 ? 1 : 2, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(status == 202 ? 1 : 2, await read.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("status")]
    [InlineData("ownerId")]
    public async Task Customer_fields_cannot_choose_money_ownership_status_or_provider_scenario(string field)
    {
        await using var factory = new AuthApiFactory(Database);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        using var response = await InitiateAsync(owner, order.Id, body: new Dictionary<string, object> { [field] = "tampered" });
        await AuthenticationTests.Problem(response, 400, "validation_failed");
        await using var read = Database.CreateContext(); Assert.Equal(0, await read.PaymentAttempts.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_identical_or_distinct_keys_produce_one_active_attempt(bool identicalKey)
    {
        var gate = new OrderMutationGate();
        await using var factory = new AuthApiFactory(Database, catalogInterceptor: gate);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        var token = await BrowserRequests.TokenAsync(owner); gate.Enabled = true;
        var responses = await Task.WhenAll(InitiateAsync(owner, order.Id, "one", token: token),
            InitiateAsync(owner, order.Id, identicalKey ? "one" : "two", token: token));
        var one = await AcceptedAsync(responses[0]); var two = await AcceptedAsync(responses[1]);
        Assert.Equal(one, two); Assert.Equal(2, gate.Participants); Assert.Equal(2, gate.Contexts.Distinct().Count());
        foreach (var response in responses) response.Dispose();
        await using var read = Database.CreateContext();
        var attempt = await read.PaymentAttempts.SingleAsync(); Assert.Equal(one.Id, attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Queued, attempt.Status); Assert.Equal("25.00", attempt.Amount.ToString());
        Assert.Equal(identicalKey ? 1 : 2, await read.IdempotencyRecords.CountAsync(row => row.Operation.StartsWith("StartPayment:v1/")));
        Assert.All(await read.InventoryBalances.ToListAsync(), row => Assert.Equal(1, row.Reserved));
        Assert.Equal(1, await read.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("Succeeded", "Pending", true)]
    [InlineData("Pending", "Pending", false)]
    [InlineData("Failed", "Failed", false)]
    public async Task Committed_authoritative_intention_is_processed_outside_transactions_and_replayed_verbatim(string scenario, string status, bool attention)
    {
        var clock = new ManualTimeProvider(Start);
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        var (c, _) = await AuthenticatedBrowser.CreateAsync(factory, "Customer"); using var owner = c;
        var products = await CheckoutTestData.StockAsync(Database, factory);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        using var first = await InitiateAsync(owner, order.Id); var accepted = await AcceptedAsync(first);
        var original = await first.Content.ReadAsByteArrayAsync();
        await using var read = Database.CreateContext(); var attempt = await read.PaymentAttempts.SingleAsync();
        await simulator.ScenarioAsync(attempt.ProviderRequestKey, scenario);
        using var gateway = simulator.Gateway(); var probe = new CommittedIntentGateway(gateway, Database);
        using var services = Processing(Database, clock, probe);
        Assert.True(await services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
        Assert.Equal(1, probe.Calls);
        using var current = await owner.GetAsync(Path(order.Id) + "/" + accepted.Id);
        var response = (await current.Content.ReadFromJsonAsync<PaymentAttemptResponse>())!;
        Assert.Equal(status, response.Status); Assert.Equal(attention, response.NeedsAttention); Assert.Equal("50.00", response.Amount);
        using var replay = await InitiateAsync(owner, order.Id);
        Assert.Equal(original, await replay.Content.ReadAsByteArrayAsync());
        await read.Entry(attempt).ReloadAsync();
        await using var providerRead = providerDatabase.CreateContext();
        Assert.Equal(attempt.ProviderRequestKey, (await providerRead.ProviderPayments.SingleAsync()).RequestKey);
        Assert.Equal(OrderStatus.PendingPayment, (await read.Orders.SingleAsync()).Status);
        Assert.All(await read.InventoryBalances.ToListAsync(), row => { Assert.Equal(10, row.OnHand); Assert.Equal(2, row.Reserved); });
        Assert.Equal(1, await read.OutboxMessages.CountAsync()); Assert.Equal(1, await read.OrderStatusHistory.CountAsync());
        if (scenario == "Failed")
        {
            using var retry = await InitiateAsync(owner, order.Id, "new-after-failure"); var replacement = await AcceptedAsync(retry);
            Assert.NotEqual(accepted.Id, replacement.Id); Assert.Equal(2, await read.PaymentAttempts.CountAsync());
        }
        else
        {
            using var retry = await InitiateAsync(owner, order.Id, "new-while-unresolved"); Assert.Equal(accepted.Id, (await AcceptedAsync(retry)).Id);
            Assert.Equal(1, await read.PaymentAttempts.CountAsync());
        }
    }
}

internal sealed class CommittedIntentGateway(IPaymentGateway inner, PostgresDatabase database) : IPaymentGateway
{
    public int Calls { get; private set; }
    public async Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        await using (var read = database.CreateContext())
        await using (var transaction = await read.Database.BeginTransactionAsync(cancellationToken))
        {
            // An independent connection must see the intention and immediately acquire both
            // locks. This fails if merchant claim/initiation still holds a transaction over HTTP.
            var rows = await read.PaymentAttempts.FromSqlInterpolated($"""
                SELECT * FROM payments."PaymentAttempts" WHERE "Id" = {request.AttemptId} FOR UPDATE NOWAIT
                """).ToListAsync(cancellationToken);
            Assert.Single(rows); Assert.Equal(request.RequestKey, rows[0].ProviderRequestKey); Assert.Equal(request.Amount, rows[0].Amount.ToString());
            await read.Orders.FromSqlInterpolated($"""
                SELECT * FROM ordering."Orders" WHERE "Id" = {request.OrderId} FOR UPDATE NOWAIT
                """).ToListAsync(cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
        }
        return await inner.SubmitAsync(request, cancellationToken);
    }
    public Task<GatewayPaymentResult?> LookupAsync(string requestKey, CancellationToken cancellationToken) => inner.LookupAsync(requestKey, cancellationToken);
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) => inner.RefundAsync(request, cancellationToken);
    public Task<GatewayRefundResult?> LookupRefundAsync(string requestKey, CancellationToken cancellationToken) => inner.LookupRefundAsync(requestKey, cancellationToken);
}
