namespace OrderFlow.Worker.Messaging;

public sealed class NotificationConsumerOptions
{
    public int ConsumersPerQueue { get; init; } = 1;
    public ushort Prefetch { get; init; } = 4;
    public void Validate()
    {
        if (ConsumersPerQueue is < 1 or > 4 || Prefetch is < 1 or > 32)
            throw new InvalidOperationException("Notification consumer requires 1–4 consumers per work queue and prefetch 1–32.");
    }
}
