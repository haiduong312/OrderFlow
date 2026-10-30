using System.Text.Json;
using System.Text.Json.Serialization;
using OrderFlow.Application.Common.Money;

namespace OrderFlow.Api.Http;

public sealed class UsdMoneyJsonConverter : JsonConverter<UsdMoney>
{
    public override UsdMoney Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !UsdMoney.TryParse(reader.GetString(), out var money))
        {
            throw new JsonException("Expected a nonnegative invariant USD string with at most two decimal places within numeric(18,2).");
        }

        return money;
    }

    public override void Write(Utf8JsonWriter writer, UsdMoney value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
