using System.Globalization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Modules.Reporting;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class ReportingRulesTests
{
    [Fact]
    public void UTC_dates_have_half_open_midnight_boundaries_and_allow_366_days()
    {
        var range = ReportRange.Create(new() { From = new DateOnly(2036, 1, 1), To = new DateOnly(2037, 1, 1) });
        Assert.Equal(TimeSpan.FromDays(366), range.End - range.Start);
        Assert.Equal(TimeSpan.Zero, range.Start.Offset); Assert.Equal(TimeSpan.Zero, range.End.Offset);
        Assert.Equal(TimeSpan.Zero, range.Start.TimeOfDay);
    }

    [Theory]
    [InlineData(null, "2035-01-02")]
    [InlineData("2035-01-01", null)]
    [InlineData("2035-01-01", "2035-01-01")]
    [InlineData("2035-01-02", "2035-01-01")]
    [InlineData("2035-01-01", "2036-01-03")]
    public void Missing_reversed_empty_and_oversized_ranges_are_rejected(string? from, string? to) =>
        Assert.Throws<ApplicationFailureException>(() => ReportRange.Create(new()
        {
            From = from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture),
            To = to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture)
        }));

    [Fact]
    public void Report_money_is_exact_invariant_and_supports_negative_net_and_large_aggregates()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            Assert.Equal("0.30", ReportMoney.Format(0.10m * 3));
            Assert.Equal("-12.50", ReportMoney.Format(-12.50m));
            Assert.Equal("19999999999999999.98", ReportMoney.Format(9999999999999999.99m * 2));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
