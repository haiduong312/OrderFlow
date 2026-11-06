using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OrderFlow.Application.Common.Pagination;

namespace OrderFlow.Application.Modules.Ordering;

public sealed class CustomerOrderQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, PageRequest.MaximumPageSize)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
    [StringLength(32)] public string? Status { get; init; }
}

public sealed class AdminOrderQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, PageRequest.MaximumPageSize)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
    [StringLength(32)] public string? Status { get; init; }
    public Guid? CustomerId { get; init; }
}

public sealed class CancelOrderRequest;

public sealed class OrderTransitionRequest
{
    [Required, StringLength(32)] public string TargetStatus { get; init; } = "";
    [Required, Range(1, int.MaxValue)] public int? ExpectedVersion { get; init; }
    [StringLength(500)] public string? Reason { get; init; }
}

public sealed record OrderExpiryInfo(
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset ObservedAt,
    string EffectiveStatus, bool IsExpired, bool CanCancel);

public sealed record OrderSummaryResponse(Guid Id, Guid CustomerId, string Status, int Version,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset ExpiresAt,
    string Total, OrderExpiryInfo Expiry)
{
    public string Currency => "USD";
}

public sealed record OrderPage(IReadOnlyList<OrderSummaryResponse> Items, int Page, int PageSize, int TotalCount);

public sealed record OrderStatusHistoryResponse(int Version, string? FromStatus, string Status,
    string ActorKind, Guid ActorUserId,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset OccurredAt, string Reason);

// Read DTOs evolve independently of the immutable checkout response bytes.
public sealed record OrderDetailResponse(Guid Id, Guid CustomerId, string Status, int Version,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset ExpiresAt,
    string Total, IReadOnlyList<OrderItemResponse> Items, OrderExpiryInfo Expiry,
    IReadOnlyList<OrderStatusHistoryResponse> History)
{
    public string Currency => "USD";
}
