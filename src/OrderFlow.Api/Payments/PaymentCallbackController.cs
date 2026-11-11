using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;

namespace OrderFlow.Api.Payments;

[ApiController, Route("api/payments/simulator/callback"), AllowAnonymous]
// Provider HMAC authenticates this route. Browser endpoints retain the global CSRF policy.
[IgnoreAntiforgeryToken(Order = 1001), RequestSizeLimit(PaymentCallback.MaximumBytes)]
public sealed class PaymentCallbackController(PaymentCallbackSettings settings, PaymentCallbackReceipt receipts,
    TimeProvider clock, ILogger<PaymentCallbackController> logger) : ControllerBase
{
    [HttpPost, ProducesResponseType(202)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!settings.Enabled) throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        if (Request.ContentLength > PaymentCallback.MaximumBytes)
            throw new BadHttpRequestException("Callback too large.", 413);
        // Also bound chunked/unknown-length bodies; do not deserialize or normalize before authentication.
        var buffer = new byte[PaymentCallback.MaximumBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await Request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (count == 0) break;
            length += count;
        }
        if (length > PaymentCallback.MaximumBytes) throw new BadHttpRequestException("Callback too large.", 413);
        var raw = buffer.AsSpan(0, length).ToArray();
        if (Request.Headers.ContainsKey("Content-Encoding") || !Authenticated(raw))
        {
            logger.LogWarning(new EventId(1600, "PaymentCallbackRejected"), "Provider callback authentication rejected");
            throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        }
        PaymentCallback callback;
        try { callback = PaymentCallback.Parse(raw); }
        catch (JsonException) { throw new ApplicationFailureException(ApplicationError.ValidationFailed); }
        try { await receipts.AcceptAsync(callback, raw, cancellationToken); }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            logger.LogWarning(new EventId(1603, "PaymentReceiptContention"), "Callback receipt postponed with PostgreSQL state {SqlState}",
                OrderDatabase.Contention(failure)!.SqlState);
            throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        }
        return Accepted();
    }

    private bool Authenticated(byte[] raw)
    {
        var keyId = SingleHeader("X-Payment-Key-Id");
        var timestamp = SingleHeader("X-Payment-Timestamp");
        var signature = SingleHeader("X-Payment-Signature");
        if (keyId != settings.KeyId || timestamp is null || timestamp.Length > 12
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || timestamp != seconds.ToString(CultureInfo.InvariantCulture)
            || signature is null || signature.Length != 64 || signature.Any(c => !char.IsAsciiHexDigit(c))) return false;
        var skew = Math.Abs((clock.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / (decimal)TimeSpan.TicksPerSecond - seconds);
        if (skew > 300) return false;
        var supplied = Convert.FromHexString(signature);
        return CryptographicOperations.FixedTimeEquals(PaymentCallback.Signature(settings.Secret, timestamp, raw), supplied);
    }

    private string? SingleHeader(string name) => Request.Headers.TryGetValue(name, out var values) && values.Count == 1
        ? values[0] : null;
}
