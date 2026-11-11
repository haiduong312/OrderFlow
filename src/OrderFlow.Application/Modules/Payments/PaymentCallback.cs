using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderFlow.Application.Common.Money;

namespace OrderFlow.Application.Modules.Payments;

public sealed record PaymentCallback(int SchemaVersion, string Provider, Guid ProviderEventId,
    DateTimeOffset OccurredAt, GatewayPaymentResult Payment)
{
    public const int MaximumBytes = 64 * 1024;
    public const string Consumer = "Payments.SimulatorSettlement:v1";
    private static readonly JsonSerializerOptions Json = new(GatewayJson.Options)
    {
        PropertyNameCaseInsensitive = false, MaxDepth = 8
    };

    public static PaymentCallback Parse(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw, new() { MaxDepth = 8 });
        RequireProperties(document.RootElement, ["schemaVersion", "provider", "providerEventId", "occurredAt", "payment"]);
        RequireProperties(document.RootElement.GetProperty("payment"),
            ["requestKey", "attemptId", "orderId", "transactionId", "amount", "currency", "state", "captureId"]);
        var state = document.RootElement.GetProperty("payment").GetProperty("state");
        if (state.ValueKind != JsonValueKind.String || !Enum.GetNames<GatewayPaymentState>().Contains(state.GetString(), StringComparer.Ordinal))
            throw new JsonException("Invalid payment state.");
        var value = JsonSerializer.Deserialize<PaymentCallback>(raw, Json) ?? throw new JsonException();
        var p = value.Payment;
        if (value.SchemaVersion != 1 || value.Provider != "Simulator" || value.ProviderEventId == Guid.Empty
            || value.OccurredAt == default || p is null || p.AttemptId == Guid.Empty || p.OrderId == Guid.Empty
            || !Identity(p.RequestKey, 80) || !Identity(p.TransactionId, 100) || !Identity(p.Currency, 3)
            || p.Currency.Length != 3 || !UsdMoney.TryParse(p.Amount, out var amount) || amount.Value <= 0
            || !Enum.IsDefined(p.State)
            || (p.State == GatewayPaymentState.Succeeded ? !Identity(p.CaptureId, 100) : p.CaptureId is not null))
            throw new JsonException("Invalid callback schema.");
        return value;
    }

    private static bool Identity(string? value, int maximum) => !string.IsNullOrEmpty(value)
        && value.Length <= maximum && value.All(c => c is >= '!' and <= '~');

    private static void RequireProperties(JsonElement element, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();
        var remaining = required.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw new JsonException("Duplicate or unsupported property.");
        if (remaining.Count != 0) throw new JsonException("Missing property.");
    }

    public static byte[] Signature(string secret, string timestamp, ReadOnlySpan<byte> raw)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret));
        hmac.AppendData(Encoding.ASCII.GetBytes(timestamp + "."));
        hmac.AppendData(raw);
        return hmac.GetHashAndReset();
    }
}
