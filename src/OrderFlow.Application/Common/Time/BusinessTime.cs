namespace OrderFlow.Application.Common.Time;

public static class BusinessTime
{
    public static DateTimeOffset GetUtcNow(TimeProvider timeProvider) =>
        ToUtcMicroseconds(timeProvider.GetUtcNow());

    public static DateTimeOffset ToUtcMicroseconds(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    public static bool HasExpired(DateTimeOffset now, DateTimeOffset expiresAt) =>
        ToUtcMicroseconds(now) >= ToUtcMicroseconds(expiresAt);
}
