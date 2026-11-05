namespace OrderFlow.IntegrationTests;

/// <summary>Controls business/cookie UTC time; infrastructure timeouts still use real time.</summary>
public sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private long utcTicks = initialTime.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        Interlocked.Add(ref utcTicks, elapsed.Ticks);
    }
}
