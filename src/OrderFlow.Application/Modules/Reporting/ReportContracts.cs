using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Timestamp = OrderFlow.Application.Modules.Ordering.OrderTimestampJsonConverter;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;

namespace OrderFlow.Application.Modules.Reporting;

public class ReportRangeQuery
{
    [Required] public DateOnly? From { get; init; }
    [Required] public DateOnly? To { get; init; }
}
public sealed class TopProductsQuery : ReportRangeQuery
{
    [Range(1, 100)] public int Limit { get; init; } = 10;
}
public class AuditQuery : ReportRangeQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, 100)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
}
public sealed class StockAuditQuery : AuditQuery
{
    public Guid? ProductId { get; init; }
    public Guid? OrderId { get; init; }
}
public sealed class LowStockQuery
{
    [Range(0, int.MaxValue)] public int Threshold { get; init; } = 5;
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, 100)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
}

public sealed record ReportRange(DateOnly From, DateOnly To)
{
    [JsonIgnore] public DateTimeOffset Start => new(From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    [JsonIgnore] public DateTimeOffset End => new(To.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    public static ReportRange Create(ReportRangeQuery query)
    {
        if (query.From is not { } from || query.To is not { } to || from >= to || to.DayNumber - from.DayNumber > 366)
            throw Invalid("from", "Supply UTC dates from < to, with an exclusive to and a maximum of 366 days.");
        return new(from, to);
    }
    public static ApplicationFailureException Invalid(string field, string message) =>
        new(ApplicationError.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });
}

// Report aggregates may exceed a single order's numeric(18,2) bound; net can be negative.
public static class ReportMoney
{
    public static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
public sealed record SalesAmounts(string PaidSales, string CapturedRevenue, string Refunds, string NetRevenue, long PaidOrderCount);
public sealed record DailySales(DateOnly Date, SalesAmounts Amounts);
public sealed record SalesReport(ReportRange Range, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset ObservedAt, SalesAmounts Totals, IReadOnlyList<DailySales> Days)
{
    public string Currency => "USD";
}
public sealed record StatusCount(string Status, long Count);
public sealed record OrderCountsReport(ReportRange Range, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset ObservedAt, IReadOnlyList<StatusCount> Counts);
public sealed record TopProduct(Guid ProductId, string Sku, string Name, long Units, string PaidSales);
public sealed record TopProductsReport(ReportRange Range, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset ObservedAt, IReadOnlyList<TopProduct> Items)
{
    public string Currency => "USD";
}
public sealed record LowStockProduct(Guid ProductId, string Sku, string Name, int OnHand, int Reserved, int Available);
public sealed record AuditPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset ObservedAt);
public sealed record PaymentAudit(Guid Id, Guid OrderId, string Status, string Amount, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(Timestamp))] DateTimeOffset UpdatedAt, bool NeedsAttention);
public sealed record CaptureAudit(Guid Id, Guid OrderId, Guid AttemptId, string Amount, bool Allocated,
    [property: JsonConverter(typeof(Timestamp))] DateTimeOffset RecordedAt, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset? ProviderOccurredAt);
public sealed record RefundAudit(Guid Id, Guid CaptureId, Guid OrderId, string Status, string Amount,
    [property: JsonConverter(typeof(Timestamp))] DateTimeOffset CreatedAt, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset UpdatedAt, [property: JsonConverter(typeof(Timestamp))] DateTimeOffset? SucceededAt);
