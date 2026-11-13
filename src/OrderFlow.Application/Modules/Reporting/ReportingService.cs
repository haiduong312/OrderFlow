using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Reporting;

public sealed class ReportingService(OrderFlowDbContext database, TimeProvider clock)
{
    public async Task<SalesReport> SalesAsync(ReportRangeQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        // SqlQuery parameterizes values. Aggregate database numeric directly, without floating point
        // or trying to translate UsdMoney.Value through its EF scalar converter.
        var rows = await database.Database.SqlQuery<SalesRow>($"""
            WITH captures AS MATERIALIZED (
                SELECT "OrderId", "RecordedAt", "Amount", "Allocated" FROM payments."PaymentCaptures"
                WHERE "RecordedAt" >= {range.Start} AND "RecordedAt" < {range.End}
            ), facts AS (
                SELECT ("RecordedAt" AT TIME ZONE 'UTC')::date AS "Date", 0::numeric AS "PaidSales",
                    SUM("Amount") AS "CapturedRevenue", 0::numeric AS "Refunds",
                    COUNT(*) FILTER (WHERE "Allocated") AS "PaidOrderCount"
                FROM captures GROUP BY 1
                UNION ALL
                SELECT (c."RecordedAt" AT TIME ZONE 'UTC')::date, SUM(i."LineTotal"), 0, 0, 0
                FROM captures c JOIN ordering."OrderItems" i ON i."OrderId" = c."OrderId"
                WHERE c."Allocated" GROUP BY 1
                UNION ALL
                SELECT ("SucceededAt" AT TIME ZONE 'UTC')::date, 0, 0, SUM("Amount"), 0
                FROM payments."Refunds" WHERE "Status" = 'Succeeded'
                    AND "SucceededAt" >= {range.Start} AND "SucceededAt" < {range.End} GROUP BY 1
            )
            SELECT "Date", SUM("PaidSales") AS "PaidSales", SUM("CapturedRevenue") AS "CapturedRevenue",
                SUM("Refunds") AS "Refunds", SUM("PaidOrderCount")::bigint AS "PaidOrderCount"
            FROM facts GROUP BY "Date" ORDER BY "Date"
            """).ToListAsync(token);
        var byDate = rows.ToDictionary(row => row.Date);
        var days = Enumerable.Range(0, range.To.DayNumber - range.From.DayNumber).Select(offset =>
        {
            var day = range.From.AddDays(offset);
            return new DailySales(day, Amounts(byDate.GetValueOrDefault(day) ?? new SalesRow()));
        }).ToArray();
        var totals = new SalesRow
        {
            PaidSales = rows.Sum(row => row.PaidSales), CapturedRevenue = rows.Sum(row => row.CapturedRevenue),
            Refunds = rows.Sum(row => row.Refunds), PaidOrderCount = rows.Sum(row => row.PaidOrderCount)
        };
        return new(range, snapshot.ObservedAt, Amounts(totals), days);
    }

    public async Task<OrderCountsReport> CountsAsync(ReportRangeQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        var rows = await database.Orders.AsNoTracking().Where(order => order.CreatedAt >= range.Start && order.CreatedAt < range.End)
            .GroupBy(order => order.Status).Select(group => new { Status = group.Key, Count = group.LongCount() }).ToListAsync(token);
        return new(range, snapshot.ObservedAt, Enum.GetValues<OrderStatus>().Select(status =>
            new StatusCount(status.ToString(), rows.SingleOrDefault(row => row.Status == status)?.Count ?? 0)).ToArray());
    }

    public async Task<TopProductsReport> TopAsync(TopProductsQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request);
        if (request.Limit is < 1 or > 100) throw ReportRange.Invalid("limit", "Limit must be 1–100.");
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        var rows = await database.Database.SqlQuery<TopRow>($"""
            SELECT p."Id" AS "ProductId", p."Sku", p."Name", totals."Units", totals."PaidSales"
            FROM (
                SELECT i."ProductId", SUM(i."Quantity"::bigint)::bigint AS "Units", SUM(i."LineTotal") AS "PaidSales"
                FROM payments."PaymentCaptures" c JOIN ordering."OrderItems" i ON i."OrderId" = c."OrderId"
                WHERE c."Allocated" AND c."RecordedAt" >= {range.Start} AND c."RecordedAt" < {range.End}
                GROUP BY i."ProductId" ORDER BY "Units" DESC, "PaidSales" DESC, i."ProductId" LIMIT {request.Limit}
            ) totals JOIN catalog."Products" p ON p."Id" = totals."ProductId"
            ORDER BY totals."Units" DESC, totals."PaidSales" DESC, p."Id"
            """).ToListAsync(token);
        return new(range, snapshot.ObservedAt, rows.Select(row => new TopProduct(row.ProductId, row.Sku, row.Name,
            row.Units, ReportMoney.Format(row.PaidSales))).ToArray());
    }

    public async Task<AuditPage<LowStockProduct>> LowStockAsync(LowStockQuery request, CancellationToken token)
    {
        var page = PageRequest.Create(request.Page, request.PageSize);
        if (request.Threshold < 0) throw ReportRange.Invalid("threshold", "Threshold must be nonnegative.");
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        var query = database.Products.AsNoTracking().Where(product => product.IsActive && product.Category.IsActive
            && product.Inventory.OnHand - product.Inventory.Reserved <= request.Threshold);
        var count = await query.CountAsync(token);
        var rows = await query.OrderBy(product => product.Inventory.OnHand - product.Inventory.Reserved).ThenBy(product => product.Id)
            .Skip(page.Offset).Take(page.PageSize).Select(product => new LowStockProduct(product.Id, product.Sku, product.Name,
                product.Inventory.OnHand, product.Inventory.Reserved, product.Inventory.OnHand - product.Inventory.Reserved)).ToListAsync(token);
        return new(rows, page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    private static SalesAmounts Amounts(SalesRow row) => new(ReportMoney.Format(row.PaidSales), ReportMoney.Format(row.CapturedRevenue),
        ReportMoney.Format(row.Refunds), ReportMoney.Format(row.CapturedRevenue - row.Refunds), row.PaidOrderCount);

    private sealed class SalesRow
    {
        public DateOnly Date { get; set; }
        public decimal PaidSales { get; set; }
        public decimal CapturedRevenue { get; set; }
        public decimal Refunds { get; set; }
        public long PaidOrderCount { get; set; }
    }
    private sealed class TopRow
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public long Units { get; set; }
        public decimal PaidSales { get; set; }
    }
}

// A DB-only read transaction; no tracking, durable projection, cache, external I/O or business mutation.
internal sealed class ReportSnapshot(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, DateTimeOffset observedAt) : IAsyncDisposable
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public static async Task<ReportSnapshot> OpenAsync(OrderFlowDbContext database, TimeProvider clock, CancellationToken token)
    {
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        try
        {
            await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
            await OrderDatabase.SetTimeoutsAsync(database, token);
            return new(transaction, BusinessTime.GetUtcNow(clock));
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
