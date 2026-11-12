using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using OrderFlow.Application.Modules.Emails;

namespace OrderFlow.Worker.Emails;

public sealed class DevelopmentSmtpOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 1025;
    public int TimeoutSeconds { get; init; } = 10;
    public void Validate()
    {
        if (!IPAddress.TryParse(Host, out var address) || !IPAddress.IsLoopback(address) || Port is < 1 or > 65535 || TimeoutSeconds is < 1 or > 20)
            throw new InvalidOperationException("Development SMTP requires a numeric loopback address, valid port and timeout 1–20 seconds.");
    }
}

// Local sink only. No SMTP authentication, relay configuration, provider idempotency or lookup is claimed.
public sealed class DevelopmentSmtpEmailGateway(DevelopmentSmtpOptions options) : IEmailGateway
{
    public string Name => "DevelopmentSmtp";
    public bool SupportsIdempotentSend => false;
    public bool SupportsLookup => false;
    public Task<EmailGatewayResult> LookupAsync(EmailRequest request, CancellationToken token) => throw new NotSupportedException("SMTP sink has no authoritative status lookup.");
    public async Task<EmailGatewayResult> SendAsync(EmailRequest request, CancellationToken token)
    {
        if (!MailAddress.TryCreate(request.Recipient, out var recipient) || !recipient.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
            || recipient.Address != request.Recipient || request.Recipient.Contains('\r') || request.Recipient.Contains('\n'))
            throw new EmailGatewayException("email_development_recipient_rejected", false, false);
        using var message = new MailMessage(request.From, request.Recipient, request.Subject, request.TextBody)
            { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8, IsBodyHtml = false };
        message.Headers.Add("Message-ID", "<" + request.MessageId + ">");
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = false, UseDefaultCredentials = false, DeliveryMethod = SmtpDeliveryMethod.Network };
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try { await client.SendMailAsync(message, budget.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new EmailGatewayException("email_smtp_timeout", true, true); }
        catch (SmtpException failure)
        {
            var code = (int)failure.StatusCode;
            if (code is >= 400 and <= 499) throw new EmailGatewayException("email_smtp_temporary_rejection", true, false);
            if (code is >= 500 and <= 599) throw new EmailGatewayException("email_smtp_permanent_rejection", false, false);
            if (failure.InnerException is IOException or SocketException or TimeoutException)
                throw new EmailGatewayException("email_smtp_connection_uncertain", true, true);
            throw new EmailGatewayException("email_smtp_protocol_review", false, true);
        }
        return new(request.ProviderKey, request.RequestHash, EmailProviderState.Sent);
    }
}
