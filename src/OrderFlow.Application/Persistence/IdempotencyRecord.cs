using System.Security.Cryptography;
using System.Text;

namespace OrderFlow.Application.Persistence;

public enum IdempotencyStatus { InProgress, Completed }

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Operation { get; set; }
    public required string Key { get; set; }
    public required string CanonicalRequest { get; set; }
    public required byte[] RequestHash { get; set; }
    public IdempotencyStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseLocation { get; set; }
    public byte[]? ResponseBody { get; set; }

    // Canonicalization is operation-specific and belongs to the future command handler.
    public static IdempotencyRecord Create(Guid userId, string operation, string key,
        string canonicalRequest, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, Operation = operation, Key = key,
            CanonicalRequest = canonicalRequest,
            RequestHash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)),
            Status = IdempotencyStatus.InProgress, CreatedAt = now
        };

    public void Complete(int statusCode, string contentType, byte[] body, string? location, DateTimeOffset now)
    {
        if (Status != IdempotencyStatus.InProgress || statusCode is < 200 or > 299 || now < CreatedAt)
        {
            throw new InvalidOperationException("Only an in-progress request can store a successful response at or after creation.");
        }

        ResponseStatusCode = statusCode;
        ResponseContentType = contentType;
        ResponseBody = body.ToArray();
        ResponseLocation = location;
        CompletedAt = now;
        Status = IdempotencyStatus.Completed;
    }
}
