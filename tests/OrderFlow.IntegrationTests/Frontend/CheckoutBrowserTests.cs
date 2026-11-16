using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Api.Realtime;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Payments;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Frontend;

[CollectionDefinition("CheckoutBrowser", DisableParallelization = true)]
public sealed class CheckoutBrowserCollection : ICollectionFixture<PostgresServer>, ICollectionFixture<RedisServer>;

[Collection("CheckoutBrowser"), Trait("Category", "PostgreSQL"), Trait("Category", "Redis"), Trait("Category", "Browser")]
public sealed class CheckoutBrowserTests(PostgresServer server, RedisServer redis, ITestOutputHelper output) : PostgresTest(server)
{
    [Theory]
    [InlineData("flow_happy_payment")]
    [InlineData("flow_lost_create")]
    [InlineData("flow_checkout_account_isolation")]
    [InlineData("flow_payment_timeout")]
    [InlineData("flow_expired_order")]
    [InlineData("flow_late_refund")]
    [InlineData("flow_cancel_notifications")]
    [InlineData("flow_realtime_reconnect")]
    [InlineData("flow_refund_review")]
    [InlineData("admin_catalog_management")]
    [InlineData("admin_stock_recovery")]
    [InlineData("admin_order_lifecycle_reports")]
    [InlineData("admin_permissions_conflicts")]
    [InlineData("admin_email_retry")]
    [InlineData("admin_empty_error_views")]
    public async Task Real_checkout_payment_notification_browser_flow(string scenario)
    {
        var client = Path.Combine(RepositoryRoot(), "client");
        var cli = Path.Combine(client, "node_modules", "@playwright", "test", "cli.js");
        Assert.True(File.Exists(cli), "Run npm ci and install the matching Playwright Chromium; no fake backend/skip.");
        var clock = new ManualTimeProvider(new(2035, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await using var providerDatabase = await Server.CreateDatabaseAsync();
        await using var simulator = await SimulatorTestHost.StartAsync(providerDatabase, clock);
        using var gateway = simulator.Gateway();
        var nonce = Guid.NewGuid().ToString("N"); var password = "Aa1!" + Guid.NewGuid().ToString("N");
        var emailA = "checkout-a-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var emailB = "checkout-b-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var adminEmail = "checkout-admin-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var connections = new BrowserHubConnections();
        await using var app = Api.Program.Build(["--environment", "Development"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = Database.ConnectionString, ["Authentication:AllowInsecureHttp"] = "true",
                ["Urls"] = "http://127.0.0.1:0", ["Realtime:Enabled"] = "true", ["Realtime:DispatchEnabled"] = "false",
                ["Realtime:RedisConnectionString"] = redis.ConnectionString, ["Realtime:RedisPrefix"] = "checkout-" + Guid.NewGuid().ToString("N"),
                ["Realtime:InstanceName"] = "checkout-browser"
            });
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddSingleton<IPaymentGateway>(gateway);
            builder.Services.AddSingleton<PaymentProcessingIteration>(); builder.Services.AddSingleton<PaymentReconciliationIteration>(); builder.Services.AddSingleton<RefundProcessingIteration>();
            builder.Services.AddSingleton(connections);
            builder.Services.Configure<HubOptions>(options => options.AddFilter<BrowserHubConnections>());
            builder.Logging.ClearProviders();
        });
        // All controls are mapped only in this test host. Browser application code never calls them.
        app.MapPost("/api/_checkout-ui/{action}", async (string action, HttpContext context) =>
        {
            if (context.Request.Headers["X-Checkout-Test-Control"] != nonce) return Results.NotFound();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); budget.CancelAfter(TimeSpan.FromSeconds(20));
            var token = budget.Token;
            if (action == "advance-expiry") clock.Advance(TimeSpan.FromMinutes(16));
            else if (action == "disconnect") connections.AbortAll();
            else if (action == "submit" || action == "capture")
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
                var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(token);
                if (action == "submit")
                {
                    await simulator.ScenarioAsync(attempt.ProviderRequestKey, "Pending");
                    Assert.True(await app.Services.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(token));
                }
                else
                {
                    using var control = simulator.Client(control: true);
                    using var resolved = await control.PostAsJsonAsync("/control/payments/" + attempt.ProviderRequestKey + "/resolve", new { state = "Succeeded" }, token);
                    Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);
                    clock.Advance(TimeSpan.FromSeconds(5));
                    Assert.True(await app.Services.GetRequiredService<PaymentReconciliationIteration>().ProcessNextAsync(token));
                }
            }
            else if (action is "refund" or "refund-review")
            {
                if (action == "refund-review")
                {
                    await using var scope = app.Services.CreateAsyncScope();
                    var refund = await scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>().PaymentRefunds.AsNoTracking().SingleAsync(token);
                    using var control = simulator.Client(control: true);
                    using var scenarioResponse = await control.PutAsJsonAsync("/control/refunds/" + refund.ProviderRefundKey + "/scenario", new { scenario = "Rejected" }, token);
                    Assert.Equal(HttpStatusCode.NoContent, scenarioResponse.StatusCode);
                }
                Assert.True(await app.Services.GetRequiredService<RefundProcessingIteration>().ProcessNextAsync(token));
            }
            else if (action == "email-review")
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
                var order = await db.Orders.SingleAsync(token);
                var recipient = await db.Users.Where(row => row.Id == order.UserId).Select(row => row.Email).SingleAsync(token);
                var source = await db.OutboxMessages.SingleAsync(row => row.AggregateId == order.Id && row.EventType == "ordering.order-confirmed", token);
                var delivery = EmailDelivery.Confirmation(order, source.EventId, recipient, clock.GetUtcNow());
                delivery.Status = EmailDeliveryStatus.NeedsReview; delivery.OutcomeUncertain = true;
                delivery.AttemptCount = 5; delivery.AttemptsInCycle = 5; delivery.LastErrorCode = "controlled_failure"; delivery.LastFailureAt = clock.GetUtcNow();
                db.EmailDeliveries.Add(delivery); await db.SaveChangesAsync(token);
            }
            else if (action is "notify" or "notify-durable")
            {
                // Replay committed source events through the production deduplicating consumer.
                await using (var read = app.Services.CreateAsyncScope())
                {
                    var db = read.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
                    var sources = await db.OutboxMessages.AsNoTracking().Where(row => row.Producer == "Ordering").OrderBy(row => row.AggregateVersion).ToListAsync(token);
                    foreach (var source in sources)
                    {
                        await using var work = app.Services.CreateAsyncScope();
                        await new NotificationProcessor(work.ServiceProvider.GetRequiredService<OrderFlowDbContext>(), clock)
                            .ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source), source.EventId.ToString("D"), source.EventType, "application/json"), token);
                    }
                }
                if (action == "notify")
                {
                    await using var read = app.Services.CreateAsyncScope();
                    var hints = await read.ServiceProvider.GetRequiredService<OrderFlowDbContext>().OutboxMessages.AsNoTracking()
                        .Where(row => row.EventType == "notifications.notification-created").ToListAsync(token);
                    foreach (var hint in hints)
                    {
                        var dispatcher = app.Services.GetRequiredService<NotificationRealtimeDispatcher>();
                        await dispatcher.DispatchAsync(EventEnvelope.Serialize(hint), hint.EventId.ToString("D"), hint.EventType, "application/json", token);
                        await dispatcher.DispatchAsync(EventEnvelope.Serialize(hint), hint.EventId.ToString("D"), hint.EventType, "application/json", token); // Duplicate hints on purpose.
                    }
                }
            }
            else return Results.NotFound();
            return Results.NoContent();
        });
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await app.StartAsync(startup.Token);
        try
        {
            Guid productId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
                await DevelopmentSeeder.SeedRolesAsync(db, startup.Token);
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var admin = await User(adminEmail, "Admin");
                await User(emailA, "Customer"); await User(emailB, "Customer");
                var category = Category.Create("Checkout fixture", clock.GetUtcNow());
                var product = Product.Create("CHECKOUT-" + Guid.NewGuid().ToString("N"), "Checkout fixture product", "Real browser checkout fixture.", UsdMoney.FromDecimal(12.50m), category.Id, clock.GetUtcNow());
                db.AddRange(category, product);
                if (scenario == "admin_empty_error_views")
                    for (var index = 1; index <= 21; index++) db.Add(Category.Create($"Paged category {index:00}", clock.GetUtcNow()));
                await db.SaveChangesAsync(startup.Token);
                var balance = await db.InventoryBalances.SingleAsync(row => row.ProductId == product.Id, startup.Token);
                db.StockMovements.Add(balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 100, "Checkout browser receipt", admin, clock.GetUtcNow()));
                await db.SaveChangesAsync(startup.Token); productId = product.Id;
                async Task<Guid> User(string email, string role)
                {
                    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, CreatedAt = clock.GetUtcNow() };
                    Assert.True((await users.CreateAsync(user, password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, role)).Succeeded); return user.Id;
                }
            }
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start = new ProcessStartInfo("node") { WorkingDirectory = client, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { cli, "test", "--project=chromium", "--grep", scenario + "$" }) start.ArgumentList.Add(arg);
            start.Environment["ORDERFLOW_API_TARGET"] = address; start.Environment["ORDERFLOW_BROWSER_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["ORDERFLOW_BROWSER_OUTPUT"] = Path.Combine(Path.GetTempPath(), scenario.StartsWith("admin_", StringComparison.Ordinal) ? "orderflow-admin-ui" : "orderflow-checkout-ui", "browser-artifacts", scenario);
            start.Environment["ORDERFLOW_BROWSER_CUSTOMER_A"] = emailA; start.Environment["ORDERFLOW_BROWSER_CUSTOMER_B"] = emailB;
            start.Environment["ORDERFLOW_BROWSER_ADMIN"] = adminEmail;
            start.Environment["ORDERFLOW_BROWSER_PASSWORD"] = password; start.Environment["ORDERFLOW_BROWSER_PRODUCT"] = productId.ToString("D");
            start.Environment["ORDERFLOW_CHECKOUT_CONTROL"] = nonce; start.Environment["VITE_ORDERFLOW_REALTIME"] = "true";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3)); await process.WaitForExitAsync(budget.Token);
                output.WriteLine(await stdout); output.WriteLine(await stderr); Assert.True(process.ExitCode == 0, $"Actual checkout browser flow {scenario} failed; see runner output/artifacts.");
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } }
            await using var verify = Database.CreateContext();
            Assert.InRange(await verify.Orders.CountAsync(), scenario.StartsWith("admin_", StringComparison.Ordinal) ? 0 : 1, scenario == "flow_checkout_account_isolation" ? 2 : 1);
            Assert.InRange(await verify.PaymentAttempts.CountAsync(), 0, 1);
            if (scenario == "admin_stock_recovery") Assert.Equal(1, await verify.StockMovements.CountAsync(row => row.Reason == "Recovered stock receipt"));
            if (scenario == "admin_email_retry")
            {
                Assert.Equal(1, await verify.EmailDeliveryRetries.CountAsync());
                var delivery = await verify.EmailDeliveries.SingleAsync();
                Assert.Equal(5, delivery.AttemptCount); Assert.Equal(1, delivery.ReviewRetryCount); Assert.Equal(0, delivery.AttemptsInCycle);
            }
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await app.StopAsync(stop.Token); }
    }
    private static string RepositoryRoot([CallerFilePath] string path = "") => Directory.GetParent(path)!.Parent!.Parent!.Parent!.FullName;
}

public sealed class BrowserHubConnections : IHubFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Action> abort = new();
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    { abort[context.Context.ConnectionId] = context.Context.GetHttpContext()!.Abort; await next(context); }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    { abort.TryRemove(context.Context.ConnectionId, out _); await next(context, exception); }
    public void AbortAll() { foreach (var stop in abort.Values) stop(); }
}
