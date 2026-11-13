using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Reporting;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Ordering;
using Xunit;

namespace OrderFlow.IntegrationTests.Reporting;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class ReportingTests(PostgresServer server) : PostgresTest(server)
{
    private const string Range = ReportingScenario.Range;

    [Fact]
    public async Task Empty_366_day_range_is_zero_filled_with_string_money_and_zero_status_counts()
    {
        await using var host = new AuthApiFactory(Database);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var admin = browser;
        const string range = "from=2036-01-01&to=2037-01-01";
        using var response = await admin.GetAsync("/api/admin/reports/sales?" + range);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith("Z", json.GetProperty("observedAt").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("totals").GetProperty("netRevenue").ValueKind);
        var report = json.Deserialize<SalesReport>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(366, report.Days.Count); Assert.Equal(new SalesAmounts("0.00", "0.00", "0.00", "0.00", 0), report.Totals);
        var counts = (await admin.GetFromJsonAsync<OrderCountsReport>("/api/admin/reports/order-counts?" + range))!;
        Assert.Equal(7, counts.Counts.Count); Assert.All(counts.Counts, row => Assert.Equal(0, row.Count));
    }

    [Fact]
    public async Task Multi_item_sale_does_not_multiply_capture_revenue_or_paid_order_count()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var order = await OrderManagementTestData.CreateAsync(s.Customer, CheckoutTestData.Body(s.Products, 2));
        Assert.Equal("1998.20", order.Total);
        await s.CaptureAsync(await s.AttemptAsync(order.Id));
        var report = (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?from=2035-01-04&to=2035-01-05"))!;
        Assert.Equal(new SalesAmounts("2010.70", "2010.70", "0.00", "2010.70", 2), report.Totals);
    }

    [Fact]
    public async Task Index_only_upgrade_preserves_populated_orders_captures_refunds_and_reports()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var before = (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?" + Range))!;
        await using var context = Database.CreateContext();
        var captureIds = await context.PaymentCaptures.Select(row => row.Id).OrderBy(id => id).ToListAsync();
        var refundIds = await context.PaymentRefunds.Select(row => row.Id).OrderBy(id => id).ToListAsync();
        // This migration's Down removes only its five indexes. Reach the exact previous schema
        // after fixture seeding, whose normal guard correctly refuses pending migrations.
        await context.GetService<IMigrator>().MigrateAsync("20261112082134_AddEmailDeliveries");
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());
        await context.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(captureIds, await context.PaymentCaptures.Select(row => row.Id).OrderBy(id => id).ToListAsync());
        Assert.Equal(refundIds, await context.PaymentRefunds.Select(row => row.Id).OrderBy(id => id).ToListAsync());
        Assert.Equal(before.Totals, (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?" + Range))!.Totals);
        var indexes = await context.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value" FROM pg_indexes WHERE indexname IN (
                'IX_PaymentCaptures_RecordedAt_Id', 'IX_PaymentCaptures_Order_RecordedAt_Id',
                'IX_PaymentAttempts_Order_CreatedAt_Id', 'IX_Refunds_SucceededAt_Id', 'IX_StockMovements_OccurredAt_Id')
            """).ToListAsync();
        Assert.Equal(5, indexes.Count);
    }

    [Fact]
    public async Task Audit_count_and_page_use_one_read_snapshot_while_an_Admin_appends_history()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var gate = new HistoryCountGate();
        await using var host = new AuthApiFactory(Database, clock: s.Clock, catalogInterceptor: gate);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var admin = browser;
        var path = $"/api/admin/audit/orders/{s.Paid}/history?from=2035-01-02&to=2035-01-05";
        gate.Enabled = true;
        var pending = admin.GetFromJsonAsync<AuditPage<OrderStatusHistoryResponse>>(path);
        try
        {
            await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var advanced = await OrderManagementTestData.AdvanceAsync(s.Admin, s.Paid, "Processing", 2);
            advanced.EnsureSuccessStatusCode();
        }
        finally { gate.Release.TrySetResult(); }
        var first = (await pending)!;
        Assert.Equal(1, gate.Participants); Assert.Equal(1, first.TotalCount); Assert.Equal(2, Assert.Single(first.Items).Version);
        var next = (await admin.GetFromJsonAsync<AuditPage<OrderStatusHistoryResponse>>(path))!;
        Assert.Equal(2, next.TotalCount); Assert.Equal(new[] { 3, 2 }, next.Items.Select(row => row.Version));
    }

    [Fact]
    public async Task UTC_boundaries_paid_vs_completed_refunded_late_capture_and_snapshot_prices_are_distinct()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var report = (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?" + Range))!;
        Assert.Equal(new SalesAmounts("25.30", "37.80", "12.50", "25.30", 2), report.Totals);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(new SalesAmounts("25.30", "37.80", "0.00", "37.80", 2), report.Days[0].Amounts);
        Assert.Equal(new SalesAmounts("0.00", "0.00", "12.50", "-12.50", 0), report.Days[1].Amounts);
        Assert.Equal(s.Clock.GetUtcNow(), report.ObservedAt);
        var refundOnly = (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?from=2035-01-03&to=2035-01-04"))!;
        Assert.Equal("-12.50", refundOnly.Totals.NetRevenue);
        var counts = (await s.Admin.GetFromJsonAsync<OrderCountsReport>("/api/admin/reports/order-counts?" + Range))!;
        Assert.Equal(1, counts.Counts.Single(row => row.Status == "Completed").Count);
        Assert.Equal(1, counts.Counts.Single(row => row.Status == "Confirmed").Count); // AtEnd created within range, paid outside.
        Assert.Equal(1, counts.Counts.Single(row => row.Status == "PendingPayment").Count);
        Assert.Equal(1, counts.Counts.Single(row => row.Status == "Cancelled").Count);
        var top = (await s.Admin.GetFromJsonAsync<TopProductsReport>("/api/admin/reports/top-products?" + Range))!;
        Assert.Equal(s.Products[1].Id, top.Items[0].ProductId); Assert.Equal(3, top.Items[0].Units); Assert.Equal("0.30", top.Items[0].PaidSales);
        Assert.Equal("25.00", top.Items[1].PaidSales); Assert.Equal("Renamed now", top.Items[1].Name); // Current label, historic prices.
        await using var context = Database.CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await context.Orders.SingleAsync(o => o.Id == s.Paid)).Status);
        Assert.Equal(OrderStatus.PendingPayment, (await context.Orders.SingleAsync(o => o.Id == s.Unpaid)).Status); // Report never expires orders.
        var before = await context.PaymentCaptures.CountAsync();
        await s.CaptureAsync(s.LateAttempt); // Source settlement replay has no new capture/refund/financial contribution.
        Assert.Equal(before, await context.PaymentCaptures.CountAsync());
        Assert.Equal(report.Totals, (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?" + Range))!.Totals);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Ho_Chi_Minh'");
        var differentSessionZone = await new ReportingService(context, s.Clock).SalesAsync(new()
            { From = new(2035, 1, 2), To = new(2035, 1, 4) }, default);
        Assert.Equal(report.Totals, differentSessionZone.Totals);
        Assert.Equal(report.Days, differentSessionZone.Days); // Bucket boundaries do not depend on the DB session timezone.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_or_review_refunds_are_exposed_but_never_deducted_from_cash(bool review)
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var id = await s.OrderAsync(s.Products[0], 1); var attempt = await s.AttemptAsync(id);
        using (var cancel = await OrderManagementTestData.CancelAsync(s.Customer, id)) cancel.EnsureSuccessStatusCode();
        await s.CaptureAsync(attempt);
        await using (var context = Database.CreateContext())
        {
            var capture = await context.PaymentCaptures.SingleAsync(c => c.OrderId == id);
            var refund = await context.PaymentRefunds.SingleAsync(r => r.CaptureId == capture.Id);
            if (review)
            {
                refund.Claim(s.Clock.GetUtcNow());
                refund.Observe(null, "refund_response_mismatch", false, s.Clock.GetUtcNow());
                await context.SaveChangesAsync();
            }
        }
        const string day = "from=2035-01-04&to=2035-01-05";
        var report = (await s.Admin.GetFromJsonAsync<SalesReport>("/api/admin/reports/sales?" + day))!;
        Assert.Equal("1011.50", report.Totals.CapturedRevenue); // 12.50 boundary capture + new 999.00 late capture.
        Assert.Equal("12.50", report.Totals.PaidSales); Assert.Equal("0.00", report.Totals.Refunds);
        var audit = (await s.Admin.GetFromJsonAsync<AuditPage<RefundAudit>>($"/api/admin/audit/orders/{id}/refunds?{day}"))!;
        Assert.Equal(review ? "NeedsReview" : "Pending", Assert.Single(audit.Items).Status);
    }

    [Fact]
    public async Task Low_stock_uses_OnHand_minus_reserved_inclusive_threshold_and_excludes_archived_catalog()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var page = (await s.Admin.GetFromJsonAsync<AuditPage<LowStockProduct>>("/api/admin/reports/low-stock?threshold=95&pageSize=1"))!;
        var low = Assert.Single(page.Items);
        Assert.Equal(s.Products[0].Id, low.ProductId); Assert.Equal(96, low.OnHand); Assert.Equal(1, low.Reserved); Assert.Equal(95, low.Available);
        var past = (await s.Admin.GetFromJsonAsync<AuditPage<LowStockProduct>>("/api/admin/reports/low-stock?threshold=95&pageSize=1&page=2"))!;
        Assert.Empty(past.Items); Assert.Equal(1, past.TotalCount);
        await using (var context = Database.CreateContext())
            await new CatalogService(context, s.Clock).ArchiveCategoryAsync(s.Products[0].CategoryId, new ArchiveRequest { ExpectedVersion = 1 }, default);
        Assert.Empty((await s.Admin.GetFromJsonAsync<AuditPage<LowStockProduct>>("/api/admin/reports/low-stock?threshold=100"))!.Items);
    }

    [Fact]
    public async Task Audit_pages_are_bounded_ordered_secret_free_and_read_only()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        var path = $"/api/admin/audit/orders/{s.Completed}/history?{Range}&pageSize=2";
        var first = (await s.Admin.GetFromJsonAsync<AuditPage<OrderStatusHistoryResponse>>(path))!;
        var second = (await s.Admin.GetFromJsonAsync<AuditPage<OrderStatusHistoryResponse>>(path + "&page=2"))!;
        Assert.Equal(5, first.TotalCount); Assert.Equal(new[] { 5, 4, 3, 2 }, first.Items.Concat(second.Items).Select(row => row.Version));
        foreach (var kind in new[] { "payment-attempts", "captures", "refunds" })
        {
            using var response = await s.Admin.GetAsync($"/api/admin/audit/orders/{s.Late}/{kind}?{Range}");
            response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl?.NoStore);
            var text = await response.Content.ReadAsStringAsync();
            foreach (var secret in new[] { "providerRequestKey", "providerRefundKey", "providerCaptureId", "providerTransactionId", "lastObservation", "leaseToken", "rawBody", "payloadHash" })
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            Assert.Single(JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray());
        }
        var movements = (await s.Admin.GetFromJsonAsync<AuditPage<StockMovementResponse>>($"/api/admin/audit/stock-movements?{Range}&orderId={s.Late}"))!;
        Assert.Equal(2, movements.TotalCount); Assert.All(movements.Items, row => Assert.Equal(s.Late, row.OrderId));
        using var missing = await s.Admin.GetAsync($"/api/admin/audit/orders/{Guid.NewGuid()}/history?{Range}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var edit = await BrowserRequests.SendAsync(s.Admin, HttpMethod.Put,
            $"/api/admin/audit/orders/{s.Completed}/history?{Range}", new { reason = "tamper" }, await BrowserRequests.TokenAsync(s.Admin));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
    }

    [Fact]
    public async Task Every_report_and_audit_requires_Admin_and_customers_cannot_read_another_owners_audit()
    {
        await using var s = await ReportingScenario.CreateAsync(Database);
        using var anonymous = s.Host.Browser();
        var paths = new[] { "reports/sales", "reports/order-counts", "reports/top-products", "reports/low-stock",
            $"audit/orders/{s.Late}/history", $"audit/orders/{s.Late}/payment-attempts", $"audit/orders/{s.Late}/captures",
            $"audit/orders/{s.Late}/refunds", "audit/stock-movements" };
        foreach (var path in paths)
        {
            using var noAuth = await anonymous.GetAsync("/api/admin/" + path + "?" + Range);
            Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
            using var denied = await s.Customer.GetAsync("/api/admin/" + path + "?" + Range);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var otherOrder = await s.Customer.GetAsync($"/api/orders/{s.Late}");
        Assert.Equal(HttpStatusCode.NotFound, otherOrder.StatusCode);
        using var ownAdminAudit = await s.Customer.GetAsync($"/api/admin/audit/orders/{s.AtEnd}/history?{Range}");
        Assert.Equal(HttpStatusCode.Forbidden, ownAdminAudit.StatusCode);
    }

    [Theory]
    [InlineData("reports/sales", "")]
    [InlineData("reports/sales", "from=2035-01-02&to=2035-01-02")]
    [InlineData("reports/sales", "from=2035-01-03&to=2035-01-02")]
    [InlineData("reports/sales", "from=2035-01-01&to=2036-01-03")]
    [InlineData("reports/sales", "from=2035-01-02T00:00:00Z&to=2035-01-03")]
    [InlineData("reports/top-products", Range + "&limit=101")]
    [InlineData("reports/low-stock", "threshold=-1")]
    [InlineData("audit/stock-movements", Range + "&pageSize=101")]
    [InlineData("audit/stock-movements", Range + "&page=2147483647&pageSize=100")]
    public async Task Invalid_ranges_filters_and_pagination_return_400(string path, string query)
    {
        await using var host = new AuthApiFactory(Database);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var admin = browser;
        using var response = await admin.GetAsync("/api/admin/" + path + "?" + query);
        await AuthenticationTests.Problem(response, 400, "validation_failed");
    }

    private sealed class HistoryCountGate : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int Participants { get; private set; }
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("OrderStatusHistory", StringComparison.Ordinal))
            {
                Enabled = false; Participants++; Arrived.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
