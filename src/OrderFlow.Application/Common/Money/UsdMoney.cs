using System.Globalization;

namespace OrderFlow.Application.Common.Money;

public readonly record struct UsdMoney
{
    public const decimal Maximum = 9999999999999999.99m;
    public const string Currency = "USD";

    public decimal Value { get; }

    private UsdMoney(decimal value) => Value = value;

    // Zero is valid for aggregate/report values. Positive prices are a separate domain rule.
    public static UsdMoney FromDecimal(decimal value)
    {
        var scale = (decimal.GetBits(value)[3] >> 16) & 0xff;
        if (value < 0 || value > Maximum || scale > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "USD amounts require scale <= 2 and the numeric(18,2) nonnegative range.");
        }

        return new UsdMoney(value);
    }

    public static bool TryParse(string? text, out UsdMoney money)
    {
        money = default;
        if (string.IsNullOrEmpty(text) || text.Length > 19)
        {
            return false;
        }

        var dot = text.IndexOf('.');
        var wholeLength = dot < 0 ? text.Length : dot;
        if (wholeLength is < 1 or > 16 || (wholeLength > 1 && text[0] == '0'))
        {
            return false;
        }

        if (dot >= 0 && text.Length - dot - 1 is < 1 or > 2)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (index != dot && text[index] is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value > Maximum)
        {
            return false;
        }

        money = new UsdMoney(value);
        return true;
    }

    public override string ToString() => Value.ToString("0.00", CultureInfo.InvariantCulture);
}
