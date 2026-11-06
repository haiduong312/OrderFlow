namespace OrderFlow.Worker;

public sealed class OrderExpiryOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 25;
    public int PollIntervalSeconds { get; set; } = 5;
}
