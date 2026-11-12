using System.Net;
using System.Security.Cryptography;
using OrderFlow.Application.Modules.Payments;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class GatewayFailureClassificationTests
{
    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(409, false)]
    [InlineData(422, false)]
    public async Task Only_classified_transient_HTTP_failures_are_retryable(int status, bool retryable)
    {
        using var gateway = Gateway((HttpStatusCode)status, "{}");
        var failure = await Assert.ThrowsAsync<GatewayUnavailableException>(() => gateway.LookupRefundAsync("original-key", default));
        Assert.Equal(retryable, failure.Retryable); Assert.Equal("gateway_http_error", failure.Code);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null")]
    public async Task Invalid_success_protocol_needs_review_instead_of_blind_retry(string body)
    {
        using var gateway = Gateway(HttpStatusCode.OK, body);
        var failure = await Assert.ThrowsAsync<GatewayUnavailableException>(() => gateway.LookupRefundAsync("original-key", default));
        Assert.False(failure.Retryable); Assert.Equal("gateway_protocol_error", failure.Code);
    }

    [Fact]
    public async Task HttpClient_response_buffer_limit_is_protocol_review_not_a_transient_network_retry()
    {
        using var gateway = Gateway(HttpStatusCode.OK, new string('x', 16 * 1024 + 1));
        var failure = await Assert.ThrowsAsync<GatewayUnavailableException>(() => gateway.LookupRefundAsync("original-key", default));
        Assert.False(failure.Retryable); Assert.Equal("gateway_protocol_error", failure.Code);
    }

    private static SimulatorPaymentGateway Gateway(HttpStatusCode status, string body) => new(
        new HttpClient(new ResponseHandler(status, body)) { BaseAddress = new Uri("http://127.0.0.1/"), MaxResponseContentBufferSize = 16 * 1024 },
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
