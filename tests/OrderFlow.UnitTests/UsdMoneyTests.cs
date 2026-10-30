using System.Globalization;
using OrderFlow.Application.Common.Money;
using Xunit;

namespace OrderFlow.UnitTests;

public class UsdMoneyTests
{
    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("0.01", "0.01")]
    [InlineData("12", "12.00")]
    [InlineData("12.5", "12.50")]
    [InlineData("12.50", "12.50")]
    [InlineData("9999999999999999.99", "9999999999999999.99")]
    public void Parse_preserves_exact_value_and_formats_two_places(string input, string output)
    {
        Assert.True(UsdMoney.TryParse(input, out var money));
        Assert.Equal(output, money.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" 1.00")]
    [InlineData("1.00 ")]
    [InlineData("-1.00")]
    [InlineData("+1.00")]
    [InlineData("01.00")]
    [InlineData("1,00")]
    [InlineData("1,000.00")]
    [InlineData("1e2")]
    [InlineData("NaN")]
    [InlineData("1.001")]
    [InlineData("1.000")]
    [InlineData("1.")]
    [InlineData(".50")]
    [InlineData("1..0")]
    [InlineData("１００")]
    [InlineData("10000000000000000.00")]
    public void Parse_rejects_noncanonical_scale_format_and_range(string? input)
    {
        Assert.False(UsdMoney.TryParse(input, out _));
    }

    [Fact]
    public void Decimal_factory_never_rounds_out_of_range_or_excess_scale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdMoney.FromDecimal(-0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdMoney.FromDecimal(UsdMoney.Maximum + 0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdMoney.FromDecimal(1.001m));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdMoney.FromDecimal(1.000m));
        Assert.Equal("0.00", default(UsdMoney).ToString());
        Assert.Equal(UsdMoney.Maximum, UsdMoney.FromDecimal(UsdMoney.Maximum).Value);
    }

    [Fact]
    public void Money_is_invariant_under_a_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            Assert.True(UsdMoney.TryParse("1234.50", out var money));
            Assert.Equal("1234.50", money.ToString());
            Assert.False(UsdMoney.TryParse("1234,50", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
