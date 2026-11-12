using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Modules.Emails;

public enum EmailDeliveryStatus { Pending, InFlight, Sent, NeedsReview }
public sealed class EmailDelivery
{
    public Guid Id { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid OrderId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string FromEmail { get; set; } = "orderflow@orderflow.invalid";
    public string Template { get; set; } = "order-confirmation:v1";
    public string Subject { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string Provider { get; set; } = "DevelopmentSmtp";
    public string ProviderKey { get; set; } = "";
    public string MessageId { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public EmailDeliveryStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public int AttemptsInCycle { get; set; }
    public int ReviewRetryCount { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public bool OutcomeUncertain { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }

    public static EmailDelivery Confirmation(Order order, Guid sourceEventId, string? recipient, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var valid = recipient is { Length: > 0 and <= 256 } && !recipient.Contains('\r') && !recipient.Contains('\n')
            && MailAddress.TryCreate(recipient, out var address) && address.Address == recipient;
        var delivery = new EmailDelivery
        {
            Id = id, SourceEventId = sourceEventId, OrderId = order.Id, RecipientUserId = order.UserId,
            RecipientEmail = recipient ?? "", Subject = $"Order {order.Id:D} confirmed",
            TextBody = $"Your order {order.Id:D} is confirmed.\nTotal: {order.Total} {order.Currency}.\n",
            ProviderKey = "order-confirmation-" + id.ToString("N"), MessageId = id.ToString("N") + "@orderflow.invalid",
            CreatedAt = now, UpdatedAt = now, NextAttemptAt = now,
            Status = valid ? EmailDeliveryStatus.Pending : EmailDeliveryStatus.NeedsReview,
            LastErrorCode = valid ? null : "email_recipient_invalid", LastFailureAt = valid ? null : now
        };
        delivery.RequestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { delivery.RecipientEmail, delivery.FromEmail, delivery.Template, delivery.Subject, delivery.TextBody, delivery.MessageId }));
        return delivery;
    }
    public EmailRequest Request() => new(Id, ProviderKey, RecipientEmail, FromEmail, Subject, TextBody, MessageId, Convert.ToHexString(RequestHash));
}

public sealed class EmailDeliveryRetry
{
    public Guid OperationId { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid ActorUserId { get; set; }
    public int CycleNumber { get; set; }
    public required string Reason { get; set; }
    public bool AcknowledgeDuplicateRisk { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}
