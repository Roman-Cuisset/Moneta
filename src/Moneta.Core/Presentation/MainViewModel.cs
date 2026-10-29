using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace Moneta.Core.Presentation;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs AccountChanged = new(nameof(SelectedAccount));
    private static readonly PropertyChangedEventArgs RowsChanged = new(nameof(Rows));
    private static readonly PropertyChangedEventArgs BalanceChanged = new(nameof(SelectedAccountBalanceText));
    private static readonly PropertyChangedEventArgs RowChanged = new(nameof(SelectedRow));
    private static readonly PropertyChangedEventArgs DetailsChanged = new(nameof(SelectedRowDetails));
    private static readonly PropertyChangedEventArgs StatusChanged = new(nameof(StatusText));
    private static readonly PropertyChangedEventArgs DisplayCurrencyChanged = new(nameof(DisplayCurrency));
    private static readonly PropertyChangedEventArgs DisplayBalanceChanged = new(nameof(DisplayBalanceText));
    private static readonly PropertyChangedEventArgs DisplayRateChanged = new(nameof(DisplayRateDescription));
    private readonly ObservableCollection<AccountSummary> _accounts = [];
    private readonly Dictionary<Guid, AccountLedger> _ledgers = [];
    private AccountSummary? _selectedAccount;
    private Currency? _displayCurrency;
    private LedgerRow? _selectedRow;
    private IReadOnlyList<LedgerRow> _rows = Array.Empty<LedgerRow>();
    private string _selectedRowDetails = string.Empty;
    private string _statusText = "Aucun compte sélectionné · données fictives en mémoire uniquement";

    public MainViewModel(DemoPortfolio portfolio, CurrencyRegistry? currencies = null)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        CurrencyRegistry = currencies ?? new CurrencyRegistry();
        Accounts = new ReadOnlyObservableCollection<AccountSummary>(_accounts);
        var accountsById = portfolio.Accounts.ToDictionary(account => account.Id);
        foreach (var transaction in portfolio.Transactions)
        {
            if (!accountsById.TryGetValue(transaction.AccountId, out var account))
                throw new ArgumentException("Une opération référence un compte absent.", nameof(portfolio));
            if (transaction.Currency != account.Currency)
                throw new ArgumentException(
                    $"Devise incompatible pour le compte {account.Name} : {transaction.Currency.Code} au lieu de {account.Currency.Code}.",
                    nameof(portfolio));
            if (transaction.Amount < 0)
                throw new ArgumentOutOfRangeException(nameof(portfolio), "Le montant d'une opération doit être une magnitude positive ou nulle.");
            if (transaction.Direction is not (TransactionDirection.Debit or TransactionDirection.Credit))
                throw new ArgumentOutOfRangeException(nameof(portfolio), "Sens d'opération inconnu.");
        }

        var transactionsByAccount = portfolio.Transactions.ToLookup(transaction => transaction.AccountId);
        foreach (var account in portfolio.Accounts)
        {
            if (!CurrencyRegistry.TryGet(account.Currency.Code, out var registered))
                CurrencyRegistry.Register(account.Currency);
            else if (registered!.DecimalPlaces != account.Currency.DecimalPlaces)
                throw new ArgumentException("La précision de la devise du compte diffère du référentiel.", nameof(portfolio));
        }
        for (var index = 0; index < portfolio.Accounts.Count; index++)
        {
            var account = portfolio.Accounts[index];
            var balance = account.OpeningBalance;
            // OrderBy is stable: equal-date entries retain their order in the supplied portfolio.
            var transactions = transactionsByAccount[account.Id].OrderBy(transaction => transaction.Date).ToArray();
            var rows = new LedgerRow[transactions.Length];
            for (var rowIndex = 0; rowIndex < transactions.Length; rowIndex++)
            {
                var transaction = transactions[rowIndex];
                balance = transaction.Direction == TransactionDirection.Credit
                    ? checked(balance + transaction.Amount)
                    : checked(balance - transaction.Amount);
                rows[rowIndex] = new LedgerRow(transaction, balance);
            }

            var summary = new AccountSummary(account, balance);
            _accounts.Add(summary);
            _ledgers.Add(account.Id, new AccountLedger(
                summary,
                Array.AsReadOnly(rows),
                $"{rows.Length.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} opérations · {account.Name} ({account.Currency.Code}) · données fictives en mémoire uniquement"));
        }

        SelectedAccount = Accounts.FirstOrDefault();
    }

    public ReadOnlyObservableCollection<AccountSummary> Accounts { get; }
    public CurrencyRegistry CurrencyRegistry { get; }
    public ReadOnlyObservableCollection<Currency> DisplayCurrencies => CurrencyRegistry.Currencies;

    public Currency? DisplayCurrency
    {
        get => _displayCurrency;
        set
        {
            Currency? registered = null;
            if (value is not null)
            {
                if (!CurrencyRegistry.TryGet(value.Code, out registered) || registered != value)
                    throw new ArgumentException("La devise d'affichage ne fait pas partie du référentiel.", nameof(value));
            }
            else if (_selectedAccount is not null)
            {
                throw new ArgumentException("Choisissez une devise d'affichage.", nameof(value));
            }
            if (ReferenceEquals(registered, _displayCurrency))
                return;
            _displayCurrency = registered;
            NotifyDisplayCurrencyChanged();
        }
    }

    public string DisplayBalanceText => _selectedAccount is null
        ? string.Empty
        : IsNativeDisplay ? _selectedAccount.BalanceText : "Cours indisponible";

    public string DisplayRateDescription => _selectedAccount is null
        ? "Aucun compte sélectionné. Aucun service de change actif ; aucun cours chargé."
        : IsNativeDisplay
            ? $"Devise native {_selectedAccount.CurrencyCode} · aucune conversion. Aucun service de change actif ; aucun cours chargé."
            : $"Cours indisponible : {_selectedAccount.CurrencyCode} → {_displayCurrency?.Code}. Aucun service de change actif ; aucun cours chargé. Le solde natif reste inchangé.";

    private bool IsNativeDisplay => _displayCurrency?.Code == _selectedAccount?.CurrencyCode;

    public AccountSummary AddDemoAccount(string name, Currency currency, string? type, decimal openingBalance, string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(currency);
        var accountName = name.Trim();
        if (accountName.Length > 120)
            throw new ArgumentException("Le nom du compte est limité à 120 caractères.", nameof(name));
        if (type?.Length > 80)
            throw new ArgumentException("Le type est limité à 80 caractères.", nameof(type));
        if (notes?.Length > 2000)
            throw new ArgumentException("Les notes sont limitées à 2 000 caractères.", nameof(notes));
        if (!CurrencyRegistry.TryGet(currency.Code, out var registered) || registered != currency)
            throw new ArgumentException("Choisissez une devise du référentiel.", nameof(currency));
        if (decimal.Round(openingBalance, currency.DecimalPlaces) != openingBalance)
            throw new ArgumentException("Le solde initial dépasse la précision de la devise ; aucun arrondi automatique.", nameof(openingBalance));
        decimal scale = 1m;
        for (var index = 0; index < currency.DecimalPlaces; index++)
            scale *= 10m;
        var limit = long.MaxValue / scale;
        if (openingBalance < -limit || openingBalance > limit)
            throw new ArgumentOutOfRangeException(nameof(openingBalance), "Le solde initial dépasse la limite en unités mineures.");

        var account = new Account(Guid.NewGuid(), accountName, currency, openingBalance)
        {
            Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        var summary = new AccountSummary(account, openingBalance);
        _ledgers.Add(account.Id, new AccountLedger(summary, Array.Empty<LedgerRow>(),
            $"0 opérations · {account.Name} ({account.Currency.Code}) · données fictives en mémoire uniquement"));
        _accounts.Add(summary);
        SelectedAccount = summary;
        return summary;
    }

    public AccountSummary? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (ReferenceEquals(value, _selectedAccount))
                return;
            if (value is not null &&
                (!_ledgers.TryGetValue(value.Account.Id, out var candidate) || !ReferenceEquals(candidate.Summary, value)))
                throw new ArgumentException("Le compte sélectionné ne fait pas partie de ce portefeuille.", nameof(value));

            _selectedAccount = value;
            var ledger = value is null ? null : _ledgers[value.Account.Id];
            _rows = ledger?.Rows ?? Array.Empty<LedgerRow>();
            _selectedRow = null;
            _selectedRowDetails = string.Empty;
            _statusText = ledger?.StatusText ?? "Aucun compte sélectionné · données fictives en mémoire uniquement";
            _displayCurrency = value is null ? null : CurrencyRegistry.Get(value.CurrencyCode);
            PropertyChanged?.Invoke(this, AccountChanged);
            PropertyChanged?.Invoke(this, RowsChanged);
            PropertyChanged?.Invoke(this, BalanceChanged);
            PropertyChanged?.Invoke(this, RowChanged);
            PropertyChanged?.Invoke(this, DetailsChanged);
            PropertyChanged?.Invoke(this, StatusChanged);
            NotifyDisplayCurrencyChanged();
        }
    }

    public IReadOnlyList<LedgerRow> Rows => _rows;

    public LedgerRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(value, _selectedRow))
                return;
            if (value is not null && !_rows.Contains(value))
                throw new ArgumentException("L'opération sélectionnée n'appartient pas au compte courant.", nameof(value));
            _selectedRow = value;
            _selectedRowDetails = value is null ? string.Empty : FormatDetails(value);
            PropertyChanged?.Invoke(this, RowChanged);
            PropertyChanged?.Invoke(this, DetailsChanged);
        }
    }

    public string SelectedAccountBalanceText => _selectedAccount?.BalanceText ?? string.Empty;
    public string SelectedRowDetails => _selectedRowDetails;
    public string StatusText => _statusText;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyDisplayCurrencyChanged()
    {
        PropertyChanged?.Invoke(this, DisplayCurrencyChanged);
        PropertyChanged?.Invoke(this, DisplayBalanceChanged);
        PropertyChanged?.Invoke(this, DisplayRateChanged);
    }

    private static string FormatDetails(LedgerRow row)
    {
        var transaction = row.Transaction;
        var valuationText = transaction.Valuation is { } valuation
            ? $"Valorisation indicative : {MoneyFormatter.Format(valuation.Amount, valuation.Currency)} · cours du {valuation.RateDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} · source : {valuation.Source}"
            : "Aucune conversion : aucune valorisation disponible.";
        var directionText = transaction.Direction == TransactionDirection.Debit ? "débit" : "crédit";
        return $"Date : {row.DateText}\nLibellé : {row.Label}\nTiers : {row.PayeeName}\nCatégorie : {row.CategoryName}\nSous-catégorie : {row.SubcategoryName}\nMode : {row.PaymentModeName}\nMontant d'origine ({directionText}) : {MoneyFormatter.Format(transaction.Amount, transaction.Currency)}\nÉtat : {row.MarkText}\nDétail : {transaction.Details}\n{valuationText}";
    }

    private sealed record AccountLedger(AccountSummary Summary, IReadOnlyList<LedgerRow> Rows, string StatusText);
}
