using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Inventory;

public sealed class StockReceiptRequest
{
    [Required] public Guid OperationId { get; init; }
    [Required, Range(1, int.MaxValue)] public int? Quantity { get; init; }
    [Required, StringLength(500)] public string Reason { get; init; } = "";
}
public sealed class StockAdjustmentRequest
{
    [Required] public Guid OperationId { get; init; }
    [Required] public int? Delta { get; init; }
    [Required, StringLength(500)] public string Reason { get; init; } = "";
}
public sealed class StockHistoryQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, PageRequest.MaximumPageSize)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
}
public sealed record InventoryResponse(Guid ProductId, int OnHand, int Reserved, long Revision)
{
    public int Available => OnHand - Reserved;
}
public sealed record StockMovementResponse(Guid Id, Guid ProductId, string Operation, Guid OperationId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<StockMovementKind>))] StockMovementKind Kind,
    [property: JsonConverter(typeof(JsonStringEnumConverter<StockActorKind>))] StockActorKind ActorKind,
    Guid? ActorUserId, string Reason, int OnHandDelta, int ReservedDelta,
    int OnHandBefore, int ReservedBefore, int OnHandAfter, int ReservedAfter, long Revision,
    [property: JsonConverter(typeof(StockTimestampJsonConverter))] DateTimeOffset OccurredAt,
    Guid? OrderId = null, Guid? ReservationId = null)
{
    public int AvailableAfter => OnHandAfter - ReservedAfter;
}
public sealed record StockHistoryResponse(IReadOnlyList<StockMovementResponse> Items, int Page, int PageSize, int TotalCount);

// Property-specific: preserve UTC Z/microseconds without changing unrelated JSON fields.
public sealed class StockTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        BusinessTime.ToUtcMicroseconds(reader.GetDateTimeOffset());
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(BusinessTime.ToUtcMicroseconds(value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
}
