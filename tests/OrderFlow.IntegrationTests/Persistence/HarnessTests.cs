using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Catalog;
using OrderFlow.IntegrationTests.Inventory;
using Xunit;

namespace OrderFlow.IntegrationTests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class HarnessTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Uncontrolled_background_loops_are_rejected_before_the_host_starts()
    {
        await using var factory = new AuthApiFactory(Database);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHostedService<UncontrolledLoop>()));
        var failure = Assert.Throws<InvalidOperationException>(() => configured.CreateClient());
        Assert.Contains(nameof(UncontrolledLoop), failure.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_case_starts_with_a_migrated_empty_database_even_with_the_same_sku(int iteration)
    {
        await using var context = Database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.False(await context.Users.AnyAsync());
        Assert.False(await context.Products.AnyAsync());
        Assert.False(await context.StockMovements.AnyAsync());
        var category = Category.Create("Isolation", DateTimeOffset.UnixEpoch);
        context.Add(category);
        context.Add(Product.Create("SAME-SKU-EACH-CASE", "Iteration " + iteration, "",
            UsdMoney.FromDecimal(1m), category.Id, DateTimeOffset.UnixEpoch));
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.Products.CountAsync());
        Assert.Equal(1, await context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Cleanup_drops_only_owned_database_even_with_an_open_connection_and_is_repeatable()
    {
        await using var owned = await Server.CreateDatabaseAsync();
        var name = new NpgsqlConnectionStringBuilder(owned.ConnectionString).Database!;
        await using var stillOpen = new NpgsqlConnection(owned.ConnectionString);
        await stillOpen.OpenAsync();
        await owned.DisposeAsync();
        await owned.DisposeAsync();

        await using var admin = new NpgsqlConnection(Database.ConnectionString);
        await admin.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", admin);
        query.Parameters.AddWithValue("name", name);
        Assert.Equal(0L, await query.ExecuteScalarAsync());
        await using var surviving = Database.CreateContext();
        Assert.True(await surviving.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Replaced_clock_drives_persisted_business_time_and_cookie_expiry_without_sleeping()
    {
        var start = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        await using var factory = new AuthApiFactory(Database, clock: clock);
        Assert.Same(clock, factory.Services.GetRequiredService<TimeProvider>());
        var (client, actor) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        using var admin = client;
        var product = await InventoryHttpTests.Product(admin);
        clock.Advance(TimeSpan.FromMinutes(15));
        using var receipt = await CatalogHttpTests.Send(admin, HttpMethod.Post, InventoryHttpTests.Receipt(product.Id),
            new { operationId = Guid.NewGuid(), quantity = 1, reason = "Logical time" });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        await using (var read = Database.CreateContext())
        {
            Assert.Equal(start, (await read.Users.SingleAsync(user => user.Id == actor)).CreatedAt);
            Assert.Equal(start, (await read.Products.SingleAsync(row => row.Id == product.Id)).CreatedAt);
            Assert.Equal(clock.GetUtcNow(), (await read.StockMovements.SingleAsync(row => row.ProductId == product.Id && row.Revision == 1)).OccurredAt);
        }
        clock.Advance(TimeSpan.FromHours(8));
        using var expired = await admin.GetAsync("/api/auth/me");
        await AuthenticationTests.Problem(expired, 401, "unauthenticated");
    }

    private sealed class UncontrolledLoop : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            throw new InvalidOperationException("An uncontrolled loop must never start in a test host.");
    }
}
