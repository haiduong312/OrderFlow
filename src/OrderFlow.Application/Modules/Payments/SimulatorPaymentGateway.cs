using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderFlow.Application.Modules.Payments;

public sealed class SimulatorPaymentGateway(HttpClient client, string apiKey) : IPaymentGateway, IDisposable
{
    public Task<GatewayPaymentResult> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken) =>
        RequiredAsync<GatewayPaymentResult>(HttpMethod.Post, "provider/payments", request, cancellationToken);
    public Task<GatewayPaymentResult?> LookupAsync(string requestKey, CancellationToken cancellationToken) =>
        SendAsync<GatewayPaymentResult>(HttpMethod.Get, "provider/payments/" + Uri.EscapeDataString(requestKey), null, cancellationToken);
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) =>
        RequiredAsync<GatewayRefundResult>(HttpMethod.Post, "provider/refunds", request, cancellationToken);
    public Task<GatewayRefundResult?> LookupRefundAsync(string requestKey, CancellationToken cancellationToken) =>
        SendAsync<GatewayRefundResult>(HttpMethod.Get, "provider/refunds/" + Uri.EscapeDataString(requestKey), null, cancellationToken);

    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken) where T : class =>
        await SendAsync<T>(method, path, body, cancellationToken) ?? throw new GatewayUnavailableException("gateway_protocol_error");

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Simulator-Key", apiKey);
            if (body is not null) request.Content = JsonContent.Create(body, options: GatewayJson.Options);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new GatewayUnavailableException("gateway_http_error");
            if (response.Content.Headers.ContentLength > 16 * 1024) throw new GatewayUnavailableException("gateway_protocol_error");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > 16 * 1024) throw new GatewayUnavailableException("gateway_protocol_error");
            return JsonSerializer.Deserialize<T>(bytes, GatewayJson.Options) ?? throw new GatewayUnavailableException("gateway_protocol_error");
        }
        catch (HttpRequestException) { throw new GatewayUnavailableException("gateway_network_error"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new GatewayUnavailableException("gateway_timeout"); }
        catch (JsonException) { throw new GatewayUnavailableException("gateway_protocol_error"); }
    }

    public void Dispose() => client.Dispose();
}
