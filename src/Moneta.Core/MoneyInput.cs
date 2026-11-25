using System.Globalization;
using System.Text;

namespace Moneta.Core;

public static class MoneyInput
{
    public static decimal ParseFrench(string text, Currency currency, bool allowNegative = false) =>
        Parse(text, currency, allowNegative, CultureInfo.GetCultureInfo(L.French));

    public static decimal ParseLocalized(string text, Currency currency, bool allowNegative = false) =>
        Parse(text, currency, allowNegative, L.Culture);

    public static string FormatForEdit(decimal value, int? decimalPlaces = null)
    {
        var text = value.ToString(decimalPlaces is { } places ? $"F{places}" : "0.############################", L.Culture);
        var separator = text.IndexOf(L.Culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
        var magnitude = Math.Abs(value);
        // Disambiguate the generated text while keeping the exact amount editable.
        return separator >= 0 && text.Length - separator == 4 && magnitude is >= 1 and < 1000
            ? text + "0" : text;
    }

    private static decimal Parse(string text, Currency currency, bool allowNegative, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(currency);
        var (whole, fraction, negative) = ReadParts(text, allowNegative, culture);
        if (fraction.Length > currency.DecimalPlaces)
            throw new ArgumentException(L.Format("MoneyInput.ThisAmountExceedsTheAccuracyOf0Decimals", currency.DecimalPlaces, currency.Code));
        // Accumulate exact minor units, retaining the symmetric bounds of ExactMoney.
        long minor = 0;
        try
        {
            foreach (var character in whole)
                minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
            foreach (var character in fraction)
                minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
            for (var index = fraction.Length; index < currency.DecimalPlaces; index++) minor = checked(minor * 10);
            if (minor == long.MinValue) throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw new ArgumentException(L.Format("MoneyInput.OffLimitAmountFor0TheNumberOf", (currency.Code), (-long.MaxValue), (long.MaxValue)));
        }
        return ExactMoney.FromMinorUnits(minor, currency);
    }

    internal static decimal ParseFilter(string text, CultureInfo culture)
    {
        var (whole, fraction, negative) = ReadParts(text, allowSign: true, culture);
        var significantDigits = (whole + fraction).TrimStart('0').TrimEnd('0').Length;
        // With at most 28 significant digits and decimal places, TryParse cannot round.
        if (significantDigits > 28 || fraction.Length > 28 ||
            !decimal.TryParse((negative ? "-" : "") + whole + "." + fraction,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentException(L.Get("Money.FilterPrecision", culture));
        return amount;
    }

    private static (string Whole, string Fraction, bool Negative) ReadParts(string text, bool allowSign, CultureInfo culture)
    {
        var input = text.AsSpan().Trim();
        ArgumentException Invalid() => new(L.Get("Money.InvalidInput", culture));
        var negative = false;
        if (!input.IsEmpty && input[0] is '-' or '+')
        {
            if (!allowSign) throw Invalid();
            negative = input[0] == '-';
            input = input[1..];
        }
        if (input.IsEmpty) throw Invalid();

        var first = input.IndexOfAny('.', ',');
        var last = input.LastIndexOfAny('.', ',');
        var separator = last;
        if (first >= 0 && first != last && !input.Contains(input[first] == '.' ? ',' : '.'))
            separator = -1; // Repeated punctuation can only describe groups of three.
        if (first > 0 && first == last && first <= 3 && input.Length - first == 4 &&
            input[0] != '0' && AllDigits(input[..first]) && AllDigits(input[(first + 1)..]))
            throw new ArgumentException(L.Get("Money.AmbiguousInput", culture));

        var whole = separator < 0 ? input : input[..separator];
        var fraction = separator < 0 ? ReadOnlySpan<char>.Empty : input[(separator + 1)..];
        if (whole.IsEmpty || separator >= 0 && (fraction.IsEmpty || !AllDigits(fraction))) throw Invalid();
        var digits = new StringBuilder(whole.Length);
        var count = 0;
        var group = '\0';
        foreach (var character in whole)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                count++;
                continue;
            }

            var kind = character is ' ' or '\u00a0' or '\u202f' ? ' ' : character;
            if (kind is not (' ' or '.' or ',') || (separator >= 0 && kind == input[separator]) ||
                (group == '\0' ? count is < 1 or > 3 || whole[0] == '0' : group != kind || count != 3))
                throw Invalid();
            group = kind;
            count = 0;
        }
        if (count == 0 || group != '\0' && count != 3) throw Invalid();
        return (digits.ToString(), fraction.TrimEnd('0').ToString(), negative);
    }

    private static bool AllDigits(ReadOnlySpan<char> value)
    {
        foreach (var digit in value)
            if (!char.IsAsciiDigit(digit)) return false;
        return true;
    }
}
