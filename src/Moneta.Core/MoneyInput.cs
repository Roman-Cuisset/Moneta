namespace Moneta.Core;

public static class MoneyInput
{
    public static decimal ParseFrench(string text, Currency currency, bool allowNegative = false) =>
        Parse(text, currency, allowNegative, english: false);

    public static decimal ParseLocalized(string text, Currency currency, bool allowNegative = false) =>
        Parse(text, currency, allowNegative, L.Culture.Name == L.English);

    private static decimal Parse(string text, Currency currency, bool allowNegative, bool english)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(currency);
        var input = text.AsSpan().Trim();
        var invalid = L.Get("Money.InvalidInput", System.Globalization.CultureInfo.GetCultureInfo(english ? L.English : L.French));
        var negative = false;
        if (!input.IsEmpty && input[0] is '-' or '+')
        {
            if (!allowNegative) throw new ArgumentException(invalid);
            negative = input[0] == '-';
            input = input[1..];
        }
        if (input.IsEmpty) throw new ArgumentException(invalid);
        var separator = input.IndexOf(english ? '.' : ',');
        var whole = separator < 0 ? input : input[..separator];
        var fraction = separator < 0 ? ReadOnlySpan<char>.Empty : input[(separator + 1)..];
        if (whole.IsEmpty || (separator >= 0 && fraction.IsEmpty)) throw new ArgumentException(invalid);
        // Accumulate exact minor units, retaining the symmetric bounds of ExactMoney.
        long minor = 0;
        var digitsInGroup = 0;
        var grouped = false;
        try
        {
            foreach (var character in whole)
            {
                if (!english && character is ' ' or '\u00a0' or '\u202f')
                {
                    if (digitsInGroup == 0 || (!grouped && digitsInGroup > 3) || (grouped && digitsInGroup != 3))
                        throw new ArgumentException(invalid);
                    grouped = true;
                    digitsInGroup = 0;
                    continue;
                }
                if (character is < '0' or > '9') throw new ArgumentException(invalid);
                digitsInGroup++;
                minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
            }
            if (digitsInGroup == 0 || (grouped && digitsInGroup != 3)) throw new ArgumentException(invalid);
            for (var index = 0; index < fraction.Length; index++)
            {
                var character = fraction[index];
                if (character is < '0' or > '9') throw new ArgumentException(invalid);
                if (index < currency.DecimalPlaces)
                    minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
                else if (character != '0')
                    throw new ArgumentException(L.Format("MoneyInput.ThisAmountExceedsTheAccuracyOf0Decimals", (currency.DecimalPlaces), (currency.Code)));
            }
            for (var index = fraction.Length; index < currency.DecimalPlaces; index++) minor = checked(minor * 10);
            if (minor == long.MinValue) throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw new ArgumentException(L.Format("MoneyInput.OffLimitAmountFor0TheNumberOf", (currency.Code), (-long.MaxValue), (long.MaxValue)));
        }
        return ExactMoney.FromMinorUnits(minor, currency);
    }
}
