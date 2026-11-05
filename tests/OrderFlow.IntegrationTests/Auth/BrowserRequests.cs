using System.Net;
using System.Net.Http.Json;
using OrderFlow.Api.Auth;
using Xunit;

namespace OrderFlow.IntegrationTests.Auth;

/// <summary>Real cookie/token roundtrips; no test authentication handler or CSRF bypass.</summary>
public static class BrowserRequests
{
    public static async Task<string> TokenAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var value = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>(cancellationToken);
        Assert.Equal("X-CSRF-TOKEN", value!.HeaderName);
        return value.RequestToken;
    }

    public static Task<HttpResponseMessage> PostAuthAsync(HttpClient client, string operation, object payload,
        string? token, CancellationToken cancellationToken = default) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/" + operation, payload, token, cancellationToken);

    // Pass null deliberately for negative CSRF tests. Tokens are acquired before starting races
    // so token acquisition/rate limits cannot be mistaken for arrival at the contested SQL.
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object? payload, string? token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request, cancellationToken);
    }
}
