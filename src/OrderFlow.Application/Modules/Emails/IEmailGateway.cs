namespace OrderFlow.Application.Modules.Emails;

public sealed record EmailRequest(Guid DeliveryId, string ProviderKey, string Recipient, string From, string Subject,
    string TextBody, string MessageId, string RequestHash);
public enum EmailProviderState { Absent, Pending, Sent, Rejected }
public sealed record EmailGatewayResult(string ProviderKey, string RequestHash, EmailProviderState State, string? ProviderMessageId = null);
public sealed class EmailGatewayException(string code, bool retryable, bool uncertain) : Exception("Email gateway operation did not complete reliably.")
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public bool Uncertain { get; } = uncertain;
}
public interface IEmailGateway
{
    string Name { get; }
    bool SupportsIdempotentSend { get; }
    // Lookup must correlate the immutable request; Absent must be authoritative, not an eventually-consistent guess.
    bool SupportsLookup { get; }
    Task<EmailGatewayResult> LookupAsync(EmailRequest request, CancellationToken token);
    Task<EmailGatewayResult> SendAsync(EmailRequest request, CancellationToken token);
}
