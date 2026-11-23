using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed class AccountSummary : INotifyPropertyChanged
{
    internal AccountSummary(Account account, decimal balance, int ordinal)
    {
        Account = account;
        Balance = balance;
        _ordinal = ordinal;
    }

    private int _ordinal;
    public Account Account { get; private set; }
    public string Name => Account.Name;
    public string CurrencyCode => Account.Currency.Code;
    public string DisplayName => $"{_ordinal + 1}. {Name} · {CurrencyCode}" + (Account.IsArchived ? L.Get("LedgerRow.Archived") : "");
    public decimal Balance { get; private set; }
    public string BalanceText => MoneyFormatter.Format(Balance, Account.Currency);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateAccount(Account value)
    {
        Account = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Account)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    internal void UpdateOrdinal(int ordinal)
    {
        if (ordinal == _ordinal) return;
        _ordinal = ordinal;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    internal void UpdateBalance(decimal balance)
    {
        if (Balance == balance) return;
        Balance = balance;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Balance)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BalanceText)));
    }

    internal void RefreshDisplayLanguage()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BalanceText)));
    }
}

public sealed class LedgerRow : INotifyPropertyChanged
{
    private readonly bool _showMarkingDates;

    internal LedgerRow(Transaction transaction, decimal balance, bool showMarkingDates = false, Guid? transferId = null)
    {
        Transaction = transaction;
        TransferId = transferId;
        Balance = balance;
        _showMarkingDates = showMarkingDates;
        Debit = transaction.Direction == TransactionDirection.Debit ? transaction.Amount : null;
        Credit = transaction.Direction == TransactionDirection.Credit ? transaction.Amount : null;
        if (transaction.Status is not (ReconciliationStatus.Unmarked or ReconciliationStatus.Marked or ReconciliationStatus.Reconciled))
            throw new ArgumentOutOfRangeException(nameof(transaction), L.Get("LedgerRow.StateOfReconciliationUnknown"));
    }

    public Transaction Transaction { get; }
    public Guid? TransferId { get; }
    public bool IsTransfer => TransferId is not null;
    public string TransferText => IsTransfer ? L.Get("LedgerRow.LinkedTransfer") : string.Empty;
    public string DateText => Transaction.Date.ToString(L.DatePattern, L.Culture);
    public string MarkedOnText => Transaction.MarkedOn?.ToString(L.DatePattern, L.Culture) ?? string.Empty;
    public string DisplayDateText => _showMarkingDates ? MarkedOnText : DateText;
    public string Label => Transaction.Label;
    public string PayeeName => Transaction.Payee.Name;
    public string CategoryName => Transaction.Category.Name;
    public string SubcategoryName => Transaction.Category.Subcategory ?? string.Empty;
    public string PaymentModeName => string.IsNullOrWhiteSpace(Transaction.PaymentMode) ? L.Get("LedgerRow.NotSpecified") : Transaction.PaymentMode;
    public string DebitText => Debit is decimal amount ? MoneyFormatter.Format(amount, Transaction.Currency) : string.Empty;
    public string CreditText => Credit is decimal amount ? MoneyFormatter.Format(amount, Transaction.Currency) : string.Empty;
    public string BalanceText => MoneyFormatter.Format(Balance, Transaction.Currency);
    public string MarkText => Transaction.Status switch
    {
        ReconciliationStatus.Unmarked => L.Get("LedgerRow.Uncleared"),
        ReconciliationStatus.Marked => L.Get("LedgerRow.Cleared"),
        _ => Transaction.ReconciliationId is null ? L.Get("LedgerRow.ReconciledInSourceStatementUnknown") : L.Get("LedgerRow.Reconciled")
    };
    public decimal Balance { get; }
    public decimal? Debit { get; }
    public decimal? Credit { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RefreshDisplayLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
