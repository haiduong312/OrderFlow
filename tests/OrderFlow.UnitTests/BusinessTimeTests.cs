using OrderFlow.Application.Common.Time;
using Xunit;

namespace OrderFlow.UnitTests;

public class BusinessTimeTests
{
    [Fact]
    public void Injected_clock_is_normalized_to_utc_and_truncated_to_microseconds()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.FromHours(7)).AddTicks(19));
        var now = BusinessTime.GetUtcNow(clock);

        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.Equal(8, now.Hour);
        Assert.Equal(0, now.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(clock.Now.UtcTicks - 9, now.UtcTicks);

        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(now.AddMinutes(1), BusinessTime.GetUtcNow(clock));
    }

    [Theory]
    [InlineData(-10, false)]
    [InlineData(0, true)]
    [InlineData(10, true)]
    public void Expiry_boundary_is_inclusive_without_sleep(long ticks, bool expired)
    {
        var deadline = new DateTimeOffset(2026, 10, 30, 8, 15, 0, TimeSpan.Zero);
        Assert.Equal(expired, BusinessTime.HasExpired(deadline.AddTicks(ticks), deadline));
    }

    [Fact]
    public void Equivalent_offsets_and_pre_unix_epoch_times_use_the_same_precision()
    {
        var value = new DateTimeOffset(1960, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(9);
        Assert.Equal(value.AddTicks(-9), BusinessTime.ToUtcMicroseconds(value.ToOffset(TimeSpan.FromHours(-7))));
        Assert.True(BusinessTime.HasExpired(value, value.ToOffset(TimeSpan.FromHours(7))));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
