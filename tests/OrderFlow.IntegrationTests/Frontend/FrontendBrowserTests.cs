using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Frontend;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL"), Trait("Category", "Browser")]
public sealed class FrontendBrowserTests(PostgresServer server, ITestOutputHelper output) : PostgresTest(server)
{
    [Theory]
    [InlineData("session_restore")]
    [InlineData("registration_validation")]
    [InlineData("credentials_csrf")]
    [InlineData("missing_expired_auth")]
    [InlineData("account_switch_isolation")]
    [InlineData("cross_tab_isolation")]
    [InlineData("loading_empty_error")]
    [InlineData("unavailable_restore")]
    [InlineData("responsive_navigation")]
    public async Task Real_browser_session_and_navigation_flow(string scenario)
    {
        var root = RepositoryRoot();
        var client = Path.Combine(root, "client");
        var cli = Path.Combine(client, "node_modules", "@playwright", "test", "cli.js");
        Assert.True(File.Exists(cli), "Browser prerequisite missing: run npm ci in client, then npx playwright install chromium. See docs/frontend.md. No fake API fallback/skip.");
        var clock = new ManualTimeProvider(new DateTimeOffset(2035, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var control = Guid.NewGuid().ToString("N");
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        var emailA = "browser-a-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var emailB = "browser-b-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        await using var app = Api.Program.Build(["--environment", "Development"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = Database.ConnectionString,
                ["Authentication:AllowInsecureHttp"] = "true", ["Urls"] = "http://127.0.0.1:0"
            });
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Logging.ClearProviders();
        });
        // Test-host-only control, never mapped by Api.Program or a shipped controller.
        app.MapPost("/api/_browser/advance-clock", (HttpContext context) =>
        {
            if (!string.Equals(context.Request.Headers["X-Browser-Test-Control"], control, StringComparison.Ordinal))
                return Results.NotFound();
            clock.Advance(TimeSpan.FromHours(9));
            return Results.NoContent();
        });
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(startup.Token);
        try
        {
            Guid productId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
                await DevelopmentSeeder.SeedRolesAsync(db, startup.Token);
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var admin = await User("browser-admin-" + Guid.NewGuid().ToString("N") + "@example.invalid", "Admin");
                await User(emailA, "Customer"); await User(emailB, "Customer");
                var category = Category.Create("Browser fixture", clock.GetUtcNow());
                var product = Product.Create("BROWSER-" + Guid.NewGuid().ToString("N"), "Browser fixture product", "",
                    UsdMoney.FromDecimal(12.50m), category.Id, clock.GetUtcNow());
                db.Add(category); db.Add(product); await db.SaveChangesAsync(startup.Token);
                var balance = await db.InventoryBalances.SingleAsync(row => row.ProductId == product.Id, startup.Token);
                db.StockMovements.Add(balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, 100, "Browser fixture receipt", admin, clock.GetUtcNow()));
                await db.SaveChangesAsync(startup.Token); productId = product.Id;

                async Task<Guid> User(string email, string role)
                {
                    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, CreatedAt = clock.GetUtcNow() };
                    Assert.True((await users.CreateAsync(user, password)).Succeeded);
                    Assert.True((await users.AddToRoleAsync(user, role)).Succeeded); return user.Id;
                }
            }
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start = new ProcessStartInfo("node") { WorkingDirectory = client, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { cli, "test", "--project=chromium", "--grep", scenario + "$" }) start.ArgumentList.Add(argument);
            var artifacts = Path.Combine(Path.GetTempPath(), "orderflow-frontend", "browser-artifacts", scenario);
            start.Environment["ORDERFLOW_API_TARGET"] = address;
            start.Environment["ORDERFLOW_BROWSER_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["ORDERFLOW_BROWSER_OUTPUT"] = artifacts;
            start.Environment["ORDERFLOW_BROWSER_CUSTOMER_A"] = emailA; start.Environment["ORDERFLOW_BROWSER_CUSTOMER_B"] = emailB;
            start.Environment["ORDERFLOW_BROWSER_PASSWORD"] = password;
            start.Environment["ORDERFLOW_BROWSER_PRODUCT"] = productId.ToString("D");
            start.Environment["ORDERFLOW_BROWSER_CONTROL"] = control;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node/Playwright browser runner.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await process.WaitForExitAsync(budget.Token);
                output.WriteLine(await stdout); output.WriteLine(await stderr);
                Assert.True(process.ExitCode == 0, $"Real Playwright flow {scenario} failed (exit {process.ExitCode}); artifacts: {artifacts}. See captured runner output.");
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            }
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await app.StopAsync(shutdown.Token);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string source = "") =>
        Directory.GetParent(source)!.Parent!.Parent!.Parent!.FullName;
}
