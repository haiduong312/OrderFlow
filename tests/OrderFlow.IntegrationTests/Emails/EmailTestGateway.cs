using OrderFlow.Application.Modules.Emails;

namespace OrderFlow.IntegrationTests.Emails;

// Controllable provider boundary; its store outlives merchant scopes/restarts in each test.
internal sealed class EmailTestGateway : IEmailGateway
{
    public string Name => "DevelopmentSmtp";
    public bool SupportsIdempotentSend { get; init; }
    public bool SupportsLookup { get; init; }
    public int SendCalls;
    public int EffectiveSends;
    public int LookupCalls;
    public Dictionary<string, EmailGatewayResult> Results { get; } = new();
    public Func<EmailRequest, CancellationToken, Task<EmailGatewayResult>>? OnSend { get; set; }
    public Func<EmailRequest, CancellationToken, Task<EmailGatewayResult>>? OnLookup { get; set; }
    public Task<EmailGatewayResult> LookupAsync(EmailRequest request, CancellationToken token)
    {
        Interlocked.Increment(ref LookupCalls);
        if (OnLookup is not null) return OnLookup(request, token);
        lock (Results) return Task.FromResult(Results.GetValueOrDefault(request.ProviderKey) ?? new(request.ProviderKey, request.RequestHash, EmailProviderState.Absent));
    }
    public async Task<EmailGatewayResult> SendAsync(EmailRequest request, CancellationToken token)
    {
        Interlocked.Increment(ref SendCalls);
        if (OnSend is not null) return await OnSend(request, token);
        return Accept(request);
    }
    public EmailGatewayResult Accept(EmailRequest request)
    {
        lock (Results)
        {
            if (SupportsIdempotentSend && Results.TryGetValue(request.ProviderKey, out var result)) return result;
            EffectiveSends++;
            return Results[request.ProviderKey] = new(request.ProviderKey, request.RequestHash, EmailProviderState.Sent, "provider-" + request.DeliveryId.ToString("N"));
        }
    }
}
