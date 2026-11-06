using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderFlow.Application.Modules.Payments;

public enum GatewayPaymentState { Pending, Succeeded, Failed }
public sealed record GatewayPaymentRequest(string RequestKey, Guid AttemptId, Guid OrderId, string Amount, string Currency);
public sealed record GatewayPaymentResult(string RequestKey, Guid AttemptId, Guid OrderId, string TransactionId,
    string Amount, string Currency, GatewayPaymentState State, string? CaptureId);
public sealed record GatewayRefundRequest(string RequestKey, string CaptureId, string Amount, string Currency);
public sealed record GatewayRefundResult(string RequestKey, string RefundId, string CaptureId, string Amount, string Currency);

public interface IPaymentGateway
{
    Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken);
    Task<GatewayPaymentResult?> LookupAsync(string requestKey, CancellationToken cancellationToken);
    // Provider contract only; merchant refund intentions/settlement remain a later milestone.
    Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken);
    Task<GatewayRefundResult?> LookupRefundAsync(string requestKey, CancellationToken cancellationToken);
}

public sealed class GatewayUnavailableException(string code) : Exception("The gateway result is uncertain.")
{
    public string Code { get; } = code;
}

public static class GatewayJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<GatewayPaymentState>(allowIntegerValues: false) }
    };
}
