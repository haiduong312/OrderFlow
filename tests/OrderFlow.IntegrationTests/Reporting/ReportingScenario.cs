using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Payments;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Reporting;

internal sealed class ReportingScenario : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2035, 1, 2, 0, 0, 0, TimeSpan.Zero);
    public const string Range = "from=2035-01-02&to=2035-01-04";
    public ManualTimeProvider Clock { get; } = new(Start.AddMinutes(-3));
    public AuthApiFactory Host { get; }
    public HttpClient Admin { get; private set; } = null!;
    public HttpClient Customer { get; private set; } = null!;
    public Product[] Products { get; private set; } = [];
    public Guid Paid { get; private set; }
    public Guid Completed { get; private set; }
    public Guid Late { get; private set; }
    public Guid Unpaid { get; private set; }
    public Guid AtEnd { get; private set; }
    public Guid LateAttempt { get; private set; }
    private readonly PostgresDatabase database;
    private ReportingScenario(PostgresDatabase db) { database = db; Host = new(db, clock: Clock); }
    private void At(DateTimeOffset instant) => Clock.Advance(instant - Clock.GetUtcNow());

    public static async Task<ReportingScenario> CreateAsync(PostgresDatabase db)
    {
        var s = new ReportingScenario(db);
        try
        {
            s.Products = await CheckoutTestData.StockAsync(db, s.Host, count: 2, stock: 100);
            await using (var context = db.CreateContext())
                await new CatalogService(context, s.Clock).UpdateProductAsync(s.Products[1].Id, new UpdateProductRequest
                {
                    Name = "Ten cent product", Price = UsdMoney.FromDecimal(0.10m), CategoryId = s.Products[1].CategoryId,
                    IsActive = true, ExpectedVersion = 1
                }, default);
            (s.Customer, _) = await AuthenticatedBrowser.CreateAsync(s.Host, "Customer");
            var before = await s.OrderAsync(s.Products[0], 1);
            var beforeAttempt = await s.AttemptAsync(before);
            s.At(Start.AddTicks(-10)); // One PostgreSQL microsecond before the lower boundary.
            await s.CaptureAsync(beforeAttempt);
            s.Paid = await s.OrderAsync(s.Products[0], 2); // Created before range, paid at inclusive from.
            var paidAttempt = await s.AttemptAsync(s.Paid);
            s.At(Start); await s.CaptureAsync(paidAttempt);
            s.At(Start.AddMinutes(1));
            s.Completed = await s.OrderAsync(s.Products[1], 3);
            await s.CaptureAsync(await s.AttemptAsync(s.Completed));
            var (admin, _) = await AuthenticatedBrowser.CreateAsync(s.Host, "Admin");
            using (admin)
            {
                var version = 2;
                foreach (var status in new[] { "Processing", "Shipped", "Completed" })
                {
                    using var response = await OrderManagementTestData.AdvanceAsync(admin, s.Completed, status, version++);
                    response.EnsureSuccessStatusCode();
                }
            }
            s.At(Start.AddMinutes(2));
            s.Late = await s.OrderAsync(s.Products[0], 1); s.LateAttempt = await s.AttemptAsync(s.Late);
            using (var cancel = await OrderManagementTestData.CancelAsync(s.Customer, s.Late)) cancel.EnsureSuccessStatusCode();
            s.At(Start.AddMinutes(3)); await s.CaptureAsync(s.LateAttempt);
            s.At(Start.AddMinutes(4)); s.Unpaid = await s.OrderAsync(s.Products[0], 1);
            s.At(Start.AddDays(1));
            await using (var context = db.CreateContext())
            {
                var refund = await context.PaymentRefunds.SingleAsync();
                refund.Claim(s.Clock.GetUtcNow());
                refund.Observe(new GatewayRefundResult(refund.ProviderRefundKey, "test-refund", refund.ProviderCaptureId,
                    refund.Amount.ToString(), "USD"), null, true, s.Clock.GetUtcNow());
                await context.SaveChangesAsync();
            }
            s.At(Start.AddDays(2).AddMinutes(-1));
            s.Customer.Dispose(); // Refresh authentication after advancing days, rather than bypassing cookie expiry.
            (s.Customer, _) = await AuthenticatedBrowser.CreateAsync(s.Host, "Customer");
            s.AtEnd = await s.OrderAsync(s.Products[0], 1);
            var endAttempt = await s.AttemptAsync(s.AtEnd);
            s.At(Start.AddDays(2)); await s.CaptureAsync(endAttempt); // Exclusive upper boundary.
            await using (var context = db.CreateContext())
                await new CatalogService(context, s.Clock).UpdateProductAsync(s.Products[0].Id, new UpdateProductRequest
                {
                    Name = "Renamed now", Price = UsdMoney.FromDecimal(999m), CategoryId = s.Products[0].CategoryId,
                    IsActive = true, ExpectedVersion = 1
                }, default);
            (s.Admin, _) = await AuthenticatedBrowser.CreateAsync(s.Host, "Admin");
            return s;
        }
        catch { await s.DisposeAsync(); throw; }
    }

    public async Task<Guid> OrderAsync(Product product, int quantity) =>
        (await OrderManagementTestData.CreateAsync(Customer, CheckoutTestData.Body([product], quantity))).Id;
    public async Task<Guid> AttemptAsync(Guid order)
    {
        using var response = await PaymentInitiationTests.InitiateAsync(Customer, order);
        return (await PaymentInitiationTests.AcceptedAsync(response)).Id;
    }
    public async Task CaptureAsync(Guid attemptId)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var orderId = await context.PaymentAttempts.Where(a => a.Id == attemptId).Select(a => a.OrderId).SingleAsync();
        var order = await context.Orders.FromSqlInterpolated($"SELECT * FROM ordering.\"Orders\" WHERE \"Id\"={orderId} FOR UPDATE").SingleAsync();
        var attempt = await context.PaymentAttempts.FromSqlInterpolated($"SELECT * FROM payments.\"PaymentAttempts\" WHERE \"Id\"={attemptId} FOR UPDATE").SingleAsync();
        var result = new GatewayPaymentResult(attempt.ProviderRequestKey, attempt.Id, order.Id, "txn-" + attempt.Id.ToString("N"),
            attempt.Amount.ToString(), "USD", GatewayPaymentState.Succeeded, "capture-" + attempt.Id.ToString("N"));
        await PaymentSettlement.LockIdentitiesAsync(context, result, default);
        Assert.Null(await new PaymentSettlement(context).SettleAsync(order, attempt, result,
            new PaymentEvidence(Guid.NewGuid(), null, null), Clock.GetUtcNow(), default));
        await context.SaveChangesAsync(); await transaction.CommitAsync();
    }
    public async ValueTask DisposeAsync()
    {
        Admin?.Dispose(); Customer?.Dispose(); await Host.DisposeAsync();
    }
}
