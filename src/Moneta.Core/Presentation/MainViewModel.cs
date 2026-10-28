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
    private readonly Dictionary<Guid, AccountLedger> _ledgers = [];
    private AccountSummary? _selectedAccount;
    private LedgerRow? _selectedRow;
    private IReadOnlyList<LedgerRow> _rows = Array.Empty<LedgerRow>();
    private string _selectedRowDetails = string.Empty;
    private string _statusText = "Aucun compte sélectionné · données fictives en mémoire uniquement";

    public MainViewModel(DemoPortfolio portfolio)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
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
        var summaries = new AccountSummary[portfolio.Accounts.Count];
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
            summaries[index] = summary;
            _ledgers.Add(account.Id, new AccountLedger(
                summary,
                Array.AsReadOnly(rows),
                $"{rows.Length.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} opérations · {account.Name} ({account.Currency.Code}) · données fictives en mémoire uniquement"));
        }

        Accounts = Array.AsReadOnly(summaries);
        SelectedAccount = Accounts.FirstOrDefault();
    }

    public IReadOnlyList<AccountSummary> Accounts { get; }

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
            PropertyChanged?.Invoke(this, AccountChanged);
            PropertyChanged?.Invoke(this, RowsChanged);
            PropertyChanged?.Invoke(this, BalanceChanged);
            PropertyChanged?.Invoke(this, RowChanged);
            PropertyChanged?.Invoke(this, DetailsChanged);
            PropertyChanged?.Invoke(this, StatusChanged);
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

    private static string FormatDetails(LedgerRow row)
    {
        var transaction = row.Transaction;
        var valuationText = transaction.Valuation is { } valuation
            ? $"Valorisation indicative : {MoneyFormatter.Format(valuation.Amount, valuation.Currency)} · cours du {valuation.RateDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} · source : {valuation.Source}"
            : "Aucune conversion : aucune valorisation disponible.";
        var directionText = transaction.Direction == TransactionDirection.Debit ? "débit" : "crédit";
        return $"Date : {row.DateText}\nLibellé : {row.Label}\nTiers : {row.PayeeName}\nCatégorie : {row.CategoryName}\nMontant d'origine ({directionText}) : {MoneyFormatter.Format(transaction.Amount, transaction.Currency)}\nÉtat : {row.MarkText}\nDétail : {transaction.Details}\n{valuationText}";
    }

    private sealed record AccountLedger(AccountSummary Summary, IReadOnlyList<LedgerRow> Rows, string StatusText);
}
