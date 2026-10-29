using System.Collections.ObjectModel;
using System.Globalization;

namespace Moneta.Core;

public sealed class CurrencyRegistry
{
    private readonly Dictionary<string, Currency> _byCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<Currency> _currencies = [];

    public CurrencyRegistry()
    {
        Currencies = new ReadOnlyObservableCollection<Currency>(_currencies);
        Register(new Currency("EUR", "Euro", 2) { Symbol = "€" });
        Register(new Currency("BYN", "Rouble biélorusse", 2) { Symbol = "Br" });
        Register(new Currency("RUB", "Rouble russe", 2) { Symbol = "₽" });
    }

    public ReadOnlyObservableCollection<Currency> Currencies { get; }

    public Currency Get(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return _byCode.TryGetValue(code, out var currency)
            ? currency
            : throw new ArgumentException("La devise n'est pas enregistrée.", nameof(code));
    }

    public bool TryGet(string code, out Currency? currency) => _byCode.TryGetValue(code, out currency);

    public Currency Register(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (currency.Code is not { Length: 3 } || currency.Code.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Le code ISO doit contenir trois lettres majuscules.", nameof(currency));
        ArgumentException.ThrowIfNullOrWhiteSpace(currency.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency.Symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(currency.DecimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currency.DecimalPlaces, 28);
        ArgumentNullException.ThrowIfNull(currency.DisplayRules);
        _ = CultureInfo.GetCultureInfo(currency.DisplayRules.CultureName);
        if (_byCode.TryGetValue(currency.Code, out var existing))
        {
            if (existing != currency)
                throw new ArgumentException("Ce code ISO possède déjà une définition différente.", nameof(currency));
            return existing;
        }

        _byCode.Add(currency.Code, currency);
        _currencies.Add(currency);
        return currency;
    }
}
