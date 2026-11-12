namespace OrderFlow.Application.Modules.Notifications;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid SourceEventId { get; set; }
    public required string Type { get; set; }
    public Guid OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
