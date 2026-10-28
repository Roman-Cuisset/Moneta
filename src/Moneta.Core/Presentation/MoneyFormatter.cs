using System.Globalization;

namespace Moneta.Core.Presentation;

public static class MoneyFormatter
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] NumberFormats = Enumerable.Range(0, 29)
        .Select(precision => $"N{precision}").ToArray();

    public static string Format(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentOutOfRangeException.ThrowIfNegative(currency.DecimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currency.DecimalPlaces, 28);
        return $"{amount.ToString(NumberFormats[currency.DecimalPlaces], FrenchCulture)} {currency.Code}";
    }
}
