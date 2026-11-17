using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using OrderFlow.IntegrationTests.Auth;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Deployment;

// Opt-in: the script provisions a dedicated project first. Never targets developer databases.
public sealed class DeploymentProfileFactAttribute : FactAttribute
{
    public DeploymentProfileFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG") is null)
            Skip = "Run infra/deployment/verify.sh against a fresh isolated Compose project.";
    }
}

[CollectionDefinition("Deployment", DisableParallelization = true)]
public sealed class DeploymentCollection;

[Collection("Deployment"), Trait("Category", "Deployment")]
public sealed class DeploymentProfileTests(ITestOutputHelper output)
{
    private string directory = "", repository = "", project = "", connectionString = "";
    private Uri origin = null!;
    private byte[] certificateHash = [];
    private bool snap;

    [DeploymentProfileFact]
    public async Task Real_TLS_profile_shares_sessions_arbitrates_checkout_delivers_cross_node_hints_and_recovers_durable_work()
    {
        directory = Path.GetFullPath(Environment.GetEnvironmentVariable("ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG")!);
        repository = Path.GetFullPath(Path.Combine(directory, "../.."));
        var env = File.ReadAllLines(Path.Combine(directory, "deployment.env")).Select(line => line.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        project = env["COMPOSE_PROJECT_NAME"];
        Assert.StartsWith("orderflow-verify-", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repository, "compose.deploy.yaml")), "Verification inputs must be in repository/.local/<name>.");
        snap = Environment.GetEnvironmentVariable("ORDERFLOW_VERIFY_SNAP_COMPAT") == "1";
        origin = new Uri("https://localhost:" + env["ORDERFLOW_DEPLOY_PORT"]);
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, "tls.crt"));
        certificateHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = int.Parse(Environment.GetEnvironmentVariable("ORDERFLOW_VERIFY_PG_PORT") ?? "15432", System.Globalization.CultureInfo.InvariantCulture),
            Database = "orderflow", Username = "orderflow_bootstrap", Password = File.ReadAllText(Path.Combine(directory, "postgres_password")).Trim(),
            Pooling = false, Timeout = 5, CommandTimeout = 25, IncludeErrorDetail = false, GssEncryptionMode = GssEncryptionMode.Disable
        }.ConnectionString;
        using var admin = Browser(new CookieContainer());
        using var bootstrap = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "bootstrap.json")));
        var account = bootstrap.RootElement.GetProperty("DevelopmentSeed").GetProperty("Account");
        await LoginAsync(admin, account.GetProperty("Email").GetString()!, account.GetProperty("Password").GetString()!);
        var nodes = new HashSet<string>();
        for (var i = 0; i < 6; i++) { using var me = await admin.GetAsync("/api/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode); nodes.Add(Node(me)); }
        Assert.Equal(new[] { "api1", "api2" }, nodes.Order().ToArray());
        // AF state issued by one node validates on the other: ordinary API requests have no affinity.
        using var csrfResponse = await admin.GetAsync("/api/auth/antiforgery");
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<OrderFlow.Api.Auth.AntiforgeryResponse>())!.RequestToken;
        using var category = await BrowserRequests.SendAsync(admin, HttpMethod.Post, "/api/admin/categories", new { name = "Deployment " + Guid.NewGuid().ToString("N") }, csrf);
        Assert.Equal(HttpStatusCode.Created, category.StatusCode); Assert.NotEqual(Node(csrfResponse), Node(category));
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        output.WriteLine("PASS TLS, session cookies on api1/api2, cross-replica antiforgery, ordinary API load balancing.");
        using (var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
        {
            spoof.Headers.Add("X-Forwarded-Proto", "http"); spoof.Headers.Add("X-Forwarded-Host", "attacker.invalid"); spoof.Headers.Add("X-Forwarded-For", "192.0.2.99");
            using var result = await admin.SendAsync(spoof); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        using (var missingCsrf = await admin.PostAsJsonAsync("/api/admin/categories", new { name = "Rejected" })) Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var cookies = new CookieContainer(); using var customer = Browser(cookies);
        var email = "deploy-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        const string password = "Fixture-only-Customer-123!";
        using (var registration = await BrowserRequests.PostAuthAsync(customer, "register", new { email, password }, await BrowserRequests.TokenAsync(customer))) Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        await LoginAsync(customer, email, password);
        using (var forbidden = await customer.GetAsync("/api/admin/categories")) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var frontend = await customer.GetAsync("/orders")) { Assert.Equal(HttpStatusCode.OK, frontend.StatusCode); Assert.Equal("text/html", frontend.Content.Headers.ContentType?.MediaType); }

        await using var socket1 = await ConnectAsync(customer, cookies, "api1");
        await using var socket2 = await ConnectAsync(customer, cookies, "api2");
        var token = await BrowserRequests.TokenAsync(customer);
        await ComposeAsync("stop", "worker1", "worker2");
        try
        {
            var firstProduct = await ProductAsync(admin, categoryId);
            var secondProduct = await ProductAsync(admin, categoryId);
            var key = Guid.NewGuid().ToString("D");
            var replay = await RaceAsync(customer, token, firstProduct, key, key);
            Assert.All(replay, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var original = await replay[0].Content.ReadAsStringAsync(); Assert.Equal(original, await replay[1].Content.ReadAsStringAsync());
            var firstOrder = JsonDocument.Parse(original).RootElement.GetProperty("id").GetGuid();
            foreach (var response in replay) response.Dispose();
            var competing = await RaceAsync(customer, token, secondProduct, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            Assert.Equal(new[] { 201, 409 }, competing.Select(r => (int)r.StatusCode).Order().ToArray());
            foreach (var response in competing) response.Dispose();
            Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM ordering.\"OrderItems\" WHERE \"ProductId\"=ANY(@ids)", new[] { firstProduct, secondProduct }));
            Assert.Equal(2L, await ScalarAsync("SELECT sum(\"Reserved\")::bigint FROM inventory.\"Balances\" WHERE \"ProductId\"=ANY(@ids)", new[] { firstProduct, secondProduct }));
            output.WriteLine("PASS two real API SQL participants reached locks: same-key replay, competing last-stock requests, one reservation per order.");

            // Test-only barrier in this owned database. Both real worker processes must reach UPDATE
            // with different SKIP LOCKED rows before either may publish. No sleeps imply correctness.
            await ExecuteAsync("""
                CREATE OR REPLACE FUNCTION messaging.deployment_verify_gate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW."Status"='InFlight' AND OLD."Status"='Pending' THEN
                  PERFORM pg_advisory_xact_lock_shared(73482017);
                END IF; RETURN NEW; END $$;
                CREATE TRIGGER deployment_verify_gate BEFORE UPDATE ON messaging."OutboxMessages"
                  FOR EACH ROW EXECUTE FUNCTION messaging.deployment_verify_gate();
                """);
            await using (var gate = new NpgsqlConnection(connectionString))
            {
                await gate.OpenAsync(); await using var hold = new NpgsqlCommand("SELECT pg_advisory_lock(73482017)", gate); await hold.ExecuteNonQueryAsync();
                try
                {
                    await ComposeAsync("start", "worker1", "worker2");
                    await WaitAsync(async () => await ScalarAsync("SELECT count(DISTINCT application_name) FROM pg_stat_activity WHERE application_name IN ('worker1','worker2') AND wait_event='advisory'") == 2, TimeSpan.FromSeconds(18));
                    output.WriteLine("PASS worker1 and worker2 both reached the claim barrier on distinct locked rows.");
                }
                finally { await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(73482017)", gate); await release.ExecuteNonQueryAsync(); }
            }
            await WaitAsync(async () => await ScalarAsync("SELECT count(*) FROM notifications.\"Notifications\" WHERE \"OrderId\"=@id", firstOrder) == 1, TimeSpan.FromSeconds(25));
            var hints1 = await socket1.ReadOrdersAsync(2); var hints2 = await socket2.ReadOrdersAsync(2);
            Assert.Equal(hints1.OrderBy(p => p.Key).ToArray(), hints2.OrderBy(p => p.Key).ToArray());
            Assert.Contains(firstOrder, hints1.Keys);
            await ExecuteAsync("DROP TRIGGER deployment_verify_gate ON messaging.\"OutboxMessages\"; DROP FUNCTION messaging.deployment_verify_gate()");
            output.WriteLine("PASS negotiated WebSockets affined to opposite APIs receive the same durable event identities through Redis.");

            await socket1.DisposeAsync(); await socket2.DisposeAsync();
            await ComposeAsync("stop", "worker1", "worker2");
            var recoveryProduct = await ProductAsync(admin, categoryId);
            using var pending = await CheckoutAsync(customer, token, recoveryProduct, Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.Created, pending.StatusCode);
            var recoveryId = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM messaging.\"OutboxMessages\" WHERE \"AggregateId\"=@id AND \"Status\"='Pending'", recoveryId));
            await ComposeAsync("restart", "postgres", "rabbitmq", "redis");
            await ComposeAsync("up", "-d", "--wait", "--wait-timeout", "120", "--force-recreate", "api1", "api2");
            using var restored = await customer.GetAsync("/api/auth/me"); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            // Preserve both cookie and exact pre-restart antiforgery request token/idempotency intention.
            using var recoveredReplay = await CheckoutAsync(customer, token, firstProduct, key);
            Assert.Equal(HttpStatusCode.Created, recoveredReplay.StatusCode); Assert.Equal(original, await recoveredReplay.Content.ReadAsStringAsync());
            await using var reconnected = await ConnectAsync(customer, cookies, "api2");
            await ComposeAsync("up", "-d", "--wait", "--wait-timeout", "120", "worker1", "worker2");
            Assert.Contains(recoveryId, (await reconnected.ReadOrdersAsync(1)).Keys);
            await WaitAsync(async () => await ScalarAsync("SELECT count(*) FROM messaging.\"OutboxMessages\" WHERE \"AggregateId\"=@id AND \"Status\"='Published'", recoveryId) == 1, TimeSpan.FromSeconds(25));
            using var notifications = await customer.GetAsync("/api/notifications"); Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
            Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM notifications.\"Notifications\" WHERE \"OrderId\"=@id", recoveryId));
            Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM pg_roles WHERE rolname IN ('orderflow_api','orderflow_worker') AND (rolsuper OR rolcreatedb OR rolcreaterole)"));
            Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM information_schema.role_table_grants WHERE grantee IN ('orderflow_api','orderflow_worker') AND table_name='__EFMigrationsHistory' AND privilege_type IN ('INSERT','UPDATE','DELETE')"));
            output.WriteLine("PASS database/broker/Redis restart, API replacement, original cookies+AF+checkout replay, pending work recovery, one notification, restricted runtime roles.");
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER IF EXISTS deployment_verify_gate ON messaging.\"OutboxMessages\"; DROP FUNCTION IF EXISTS messaging.deployment_verify_gate()");
            await ComposeAsync("start", "worker1", "worker2");
        }
    }

    private bool PinnedCertificate(X509Certificate? certificate) => certificate is not null && CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), certificateHash);
    private HttpClient Browser(CookieContainer cookies) => new(new HttpClientHandler
    { CookieContainer = cookies, ServerCertificateCustomValidationCallback = (_, cert, _, _) => PinnedCertificate(cert), AllowAutoRedirect = false })
    { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(30) };
    private static string Node(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("X-OrderFlow-Instance"));
    private static async Task LoginAsync(HttpClient browser, string email, string password)
    {
        using var login = await BrowserRequests.PostAuthAsync(browser, "login", new { email, password }, await BrowserRequests.TokenAsync(browser));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-OrderFlow.Auth=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }
    private static async Task<Guid> ProductAsync(HttpClient admin, Guid categoryId)
    {
        var token = await BrowserRequests.TokenAsync(admin);
        using var product = await BrowserRequests.SendAsync(admin, HttpMethod.Post, "/api/admin/products", new { sku = "DEPLOY-" + Guid.NewGuid().ToString("N"), name = "Deployment stock", description = "", price = "12.50", categoryId }, token);
        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
        var id = (await product.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var receipt = await BrowserRequests.SendAsync(admin, HttpMethod.Post, $"/api/admin/products/{id}/stock-receipts", new { operationId = Guid.NewGuid(), quantity = 1, reason = "Deployment fixture receipt" }, token);
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode); return id;
    }
    private static async Task<HttpResponseMessage> CheckoutAsync(HttpClient browser, string token, Guid productId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } }) };
        request.Headers.Add("X-CSRF-TOKEN", token); request.Headers.Add("Idempotency-Key", key); return await browser.SendAsync(request);
    }
    private async Task<HttpResponseMessage[]> RaceAsync(HttpClient browser, string token, Guid productId, string key1, string key2)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT 1 FROM inventory.\"Balances\" WHERE \"ProductId\"=@id FOR UPDATE", connection, transaction); hold.Parameters.AddWithValue("id", productId); await hold.ExecuteScalarAsync();
        var a = CheckoutAsync(browser, token, productId, key1); var b = CheckoutAsync(browser, token, productId, key2);
        try { await WaitAsync(async () => await ScalarAsync("SELECT count(DISTINCT application_name) FROM pg_stat_activity WHERE application_name IN ('api1','api2') AND wait_event_type='Lock'") == 2, TimeSpan.FromSeconds(4)); }
        finally { await transaction.RollbackAsync(); }
        var result = await Task.WhenAll(a, b); Assert.Equal(new[] { "api1", "api2" }, result.Select(Node).Order().ToArray()); return result;
    }
    private async Task<long> ScalarAsync(string sql, object? parameter = null)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(); await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null) command.Parameters.AddWithValue(parameter is Guid[] ? "ids" : "id", parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(); await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
    private static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var budget = new CancellationTokenSource(timeout);
        while (!await condition()) await Task.Delay(25, budget.Token); // Poll a concrete SQL predicate, never infer success from elapsed time.
    }
    private async Task ComposeAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "compose", "-f", "compose.deploy.yaml", "-f", "infra/deployment/compose.verify.yaml" }) start.ArgumentList.Add(arg);
        if (snap) { start.ArgumentList.Add("-f"); start.ArgumentList.Add("infra/deployment/compose.snap-compat.yaml"); }
        start.ArgumentList.Add("--env-file"); start.ArgumentList.Add(Path.Combine(directory, "deployment.env"));
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        await stdout; var errors = await stderr; Assert.True(process.ExitCode == 0, "Compose failed: " + errors);
    }
    private async Task<DeploymentSocket> ConnectAsync(HttpClient browser, CookieContainer cookies, string node)
    {
        // Route hint only: no authorization power. Negotiation and upgrade both use this API.
        var scopedCookies = new CookieContainer(); scopedCookies.Add(cookies.GetCookies(origin));
        scopedCookies.Add(origin, new Cookie("__Secure-OrderFlow.Route", node, "/api/hubs/notifications") { Secure = true, HttpOnly = true });
        var csrf = await BrowserRequests.TokenAsync(browser);
        var connection = new HubConnectionBuilder().WithUrl(new Uri(origin, "/api/hubs/notifications"), options =>
        {
            options.Cookies = scopedCookies; options.Headers["Origin"] = origin.GetLeftPart(UriPartial.Authority); options.Headers["X-CSRF-TOKEN"] = csrf;
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
            options.HttpMessageHandlerFactory = handler =>
            {
                ((HttpClientHandler)handler).ServerCertificateCustomValidationCallback = (_, cert, _, _) => PinnedCertificate(cert);
                return new ExpectedNodeHandler(handler, node);
            };
            options.WebSocketConfiguration = socket => socket.RemoteCertificateValidationCallback = (_, cert, _, _) => PinnedCertificate(cert);
        }).Build();
        var result = new DeploymentSocket(connection);
        try { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await connection.StartAsync(budget.Token); await result.Ready.Task.WaitAsync(budget.Token); return result; }
        catch { await result.DisposeAsync(); throw; }
    }
    private sealed class ExpectedNodeHandler(HttpMessageHandler inner, string node) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(node, Node(response));
            return response; // A successful upgrade/ConnectionReady also proves the node retained its negotiate token.
        }
    }
    private sealed class DeploymentSocket : IAsyncDisposable
    {
        private readonly HubConnection connection;
        private readonly System.Threading.Channels.Channel<JsonElement> hints = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DeploymentSocket(HubConnection connection)
        {
            this.connection = connection; connection.On("ConnectionReady", () => Ready.TrySetResult());
            connection.On<JsonElement>("NotificationAvailable", hint => hints.Writer.TryWrite(hint.Clone()));
        }
        public async Task<Dictionary<Guid, Guid>> ReadOrdersAsync(int count)
        {
            var result = new Dictionary<Guid, Guid>(); using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            while (result.Count < count) { var hint = await hints.Reader.ReadAsync(budget.Token); result[hint.GetProperty("orderId").GetGuid()] = hint.GetProperty("eventId").GetGuid(); }
            return result;
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
