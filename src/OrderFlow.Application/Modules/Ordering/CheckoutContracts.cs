using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Ordering;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public List<CreateOrderItemRequest?>? Items { get; init; }
}

public sealed class CreateOrderItemRequest
{
    [Required] public Guid? ProductId { get; init; }
    [Required, Range(1, 1000)] public int? Quantity { get; init; }
}

// These monetary strings are formatted from authoritative UsdMoney values, never request data.
public sealed record OrderItemResponse(Guid ProductId, string Sku, string Name, string UnitPrice, int Quantity, string LineTotal);
public sealed record OrderResponse(Guid Id, string Status, int Version,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(OrderTimestampJsonConverter))] DateTimeOffset ExpiresAt,
    string Total, IReadOnlyList<OrderItemResponse> Items)
{
    public string Currency => "USD";
}

public sealed record CheckoutResponse(int StatusCode, string ContentType, string Location, byte[] Body);

public sealed class OrderTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        BusinessTime.ToUtcMicroseconds(reader.GetDateTimeOffset());
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(BusinessTime.ToUtcMicroseconds(value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
}
