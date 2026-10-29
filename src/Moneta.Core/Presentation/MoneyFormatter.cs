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
        var rules = currency.DisplayRules;
        var culture = rules.CultureName == FrenchCulture.Name
            ? FrenchCulture
            : CultureInfo.GetCultureInfo(rules.CultureName);
        var number = amount.ToString(NumberFormats[currency.DecimalPlaces], culture);
        var unit = rules.UseSymbol ? currency.Symbol : currency.Code;
        return rules.UseSymbol && rules.SymbolBeforeAmount ? $"{unit} {number}" : $"{number} {unit}";
    }
}
