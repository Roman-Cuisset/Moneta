using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, Currency> _presentationByCode = new(StringComparer.Ordinal);
    private readonly ObservableCollection<Currency> _presentationValues = [];
    private ReadOnlyObservableCollection<Currency>? _presentationView;
    private OfficialRateSet? _displayRates;
    private RateProvider _rateProvider;
    private DateOnly _valuationDate;
    private bool _rateFromCache;
    private bool _ratesConnected;
    private string _rateMessage = string.Empty;
    private string _projectedBalance = string.Empty;
    private string _projectedRateDescription = "Aucun compte sélectionné. Aucun service de change actif ; aucun cours chargé.";

    // Presentation currencies never enter the financial registry or CaptureDocument.
    private ReadOnlyObservableCollection<Currency> PresentationCurrencies
    {
        get
        {
            if (_presentationView is not null) return _presentationView;
            foreach (var currency in CurrencyRegistry.Currencies)
            {
                _presentationByCode.Add(currency.Code, currency);
                _presentationValues.Add(currency);
            }
            foreach (var currency in new CurrencyRegistry().Currencies)
                if (_presentationByCode.TryAdd(currency.Code, currency)) _presentationValues.Add(currency);
            _presentationView = new ReadOnlyObservableCollection<Currency>(_presentationValues);
            // Optional caller-owned registries must not retain discarded view models.
            var weak = new WeakReference<MainViewModel>(this);
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (sender, _) =>
            {
                if (weak.TryGetTarget(out var model)) model.SynchronizePresentationCurrencies();
                else if (sender is INotifyCollectionChanged changes) changes.CollectionChanged -= handler;
            };
            ((INotifyCollectionChanged)CurrencyRegistry.Currencies).CollectionChanged += handler;
            return _presentationView;
        }
    }

    private bool TryPresentationCurrency(string code, out Currency? currency)
    {
        _ = PresentationCurrencies;
        return _presentationByCode.TryGetValue(code, out currency);
    }

    private void SynchronizePresentationCurrencies()
    {
        foreach (var currency in CurrencyRegistry.Currencies)
        {
            if (_presentationByCode.TryAdd(currency.Code, currency)) _presentationValues.Add(currency);
            else if (!ReferenceEquals(_presentationByCode[currency.Code], currency))
            {
                _presentationByCode[currency.Code] = currency;
                for (var index = 0; index < _presentationValues.Count; index++)
                    if (_presentationValues[index].Code == currency.Code) { _presentationValues[index] = currency; break; }
                if (_displayCurrency?.Code == currency.Code) _displayCurrency = currency;
            }
        }
        NotifyDisplayCurrencyChanged();
    }

    public string NativeBalanceDescription => _selectedAccount is not null && !IsNativeDisplay
        ? "Solde natif : " + _selectedAccount.BalanceText : string.Empty;

    public void UpdateRateDisplay(OfficialRateSet? rates, RateProvider provider, DateOnly date, bool fromCache, string message)
    {
        _ = RateRules.ReferenceCurrency(provider);
        if (date == default) throw new ArgumentException("Date de valorisation invalide.", nameof(date));
        if (rates is not null)
        {
            RateRules.Validate(rates);
            if (rates.Provider != provider || rates.EffectiveDate > date || date.DayNumber - rates.EffectiveDate.DayNumber > RateRules.MaximumAgeDays)
                throw new ArgumentException("Cotations incompatibles avec le fournisseur ou la date de valorisation.", nameof(rates));
        }
        _displayRates = rates;
        _rateProvider = provider;
        _valuationDate = date;
        _rateFromCache = fromCache;
        _rateMessage = message;
        _ratesConnected = true;
        NotifyDisplayCurrencyChanged();
    }

    private void RefreshRateProjection()
    {
        if (_selectedAccount is null)
        {
            _projectedBalance = string.Empty;
            _projectedRateDescription = "Aucun compte sélectionné." + (_ratesConnected ? " " + _rateMessage : " Aucun service de change actif ; aucun cours chargé.");
            return;
        }
        if (IsNativeDisplay)
        {
            _projectedBalance = _selectedAccount.BalanceText;
            _projectedRateDescription = $"Devise native {_selectedAccount.CurrencyCode} · aucune conversion." +
                (_ratesConnected ? string.Empty : " Aucun service de change actif ; aucun cours chargé.");
            return;
        }
        _projectedBalance = "Cours indisponible";
        _projectedRateDescription = $"Cours indisponible : {_selectedAccount.CurrencyCode} → {_displayCurrency?.Code}. Le solde natif reste inchangé. ";
        if (!_ratesConnected)
        {
            _projectedRateDescription += "Aucun service de change actif ; aucun cours chargé.";
            return;
        }
        if (_displayRates is null || _displayCurrency is null)
        {
            _projectedRateDescription += RateRules.ProviderName(_rateProvider) + " · " + _rateMessage;
            return;
        }
        try
        {
            var estimate = RateConverter.ConvertAmount(_selectedAccount.Balance, _selectedAccount.CurrencyCode, _displayCurrency, _displayRates);
            _projectedBalance = MoneyFormatter.Format(estimate, _displayCurrency);
            var age = _valuationDate.DayNumber - _displayRates.EffectiveDate.DayNumber;
            _projectedRateDescription = $"Équivalent estimé · {RateRules.ProviderName(_rateProvider)}\nCours du {_displayRates.EffectiveDate:dd/MM/yyyy}" +
                (age == 0 ? string.Empty : $" · ancien de {age} jour(s)") +
                $"\nDate demandée : {_valuationDate:dd/MM/yyyy}. Récupéré le {_displayRates.RetrievedUtc.ToLocalTime():dd/MM/yyyy HH:mm}. " +
                (_rateFromCache ? "Cache local. " : "Réponse réseau validée. ") + _rateMessage;
        }
        catch (Exception error) when (error is RateException or OverflowException or ArgumentException)
        {
            _projectedRateDescription += error.Message + " " + _rateMessage;
        }
    }
}
