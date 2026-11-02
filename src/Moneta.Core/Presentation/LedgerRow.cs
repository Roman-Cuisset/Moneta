using System.ComponentModel;
using System.Globalization;

namespace Moneta.Core.Presentation;

public sealed class AccountSummary : INotifyPropertyChanged
{
    internal AccountSummary(Account account, decimal balance)
    {
        Account = account;
        Balance = balance;
        BalanceText = MoneyFormatter.Format(balance, account.Currency);
    }

    public Account Account { get; }
    public string Name => Account.Name;
    public string CurrencyCode => Account.Currency.Code;
    public decimal Balance { get; private set; }
    public string BalanceText { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateBalance(decimal balance)
    {
        if (Balance == balance) return;
        Balance = balance;
        BalanceText = MoneyFormatter.Format(balance, Account.Currency);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Balance)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BalanceText)));
    }
}

public sealed class LedgerRow
{
    internal LedgerRow(Transaction transaction, decimal balance, bool showMarkingDates = false, Guid? transferId = null)
    {
        Transaction = transaction;
        TransferId = transferId;
        Balance = balance;
        DateText = transaction.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        MarkedOnText = transaction.MarkedOn?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        DisplayDateText = showMarkingDates ? MarkedOnText : DateText;
        Debit = transaction.Direction == TransactionDirection.Debit ? transaction.Amount : null;
        Credit = transaction.Direction == TransactionDirection.Credit ? transaction.Amount : null;
        DebitText = Debit is decimal debit ? MoneyFormatter.Format(debit, transaction.Currency) : string.Empty;
        CreditText = Credit is decimal credit ? MoneyFormatter.Format(credit, transaction.Currency) : string.Empty;
        BalanceText = MoneyFormatter.Format(balance, transaction.Currency);
        MarkText = transaction.Status switch
        {
            ReconciliationStatus.Unmarked => "non pointé",
            ReconciliationStatus.Marked => "pointé",
            ReconciliationStatus.Reconciled => transaction.ReconciliationId is null
                ? "rapproché hérité – relevé inconnu" : "rapproché",
            _ => throw new ArgumentOutOfRangeException(nameof(transaction), "État de rapprochement inconnu.")
        };
    }

    public Transaction Transaction { get; }
    public Guid? TransferId { get; }
    public bool IsTransfer => TransferId is not null;
    public string TransferText => IsTransfer ? "Virement lié" : string.Empty;
    public string DateText { get; }
    public string MarkedOnText { get; }
    public string DisplayDateText { get; }
    public string Label => Transaction.Label;
    public string PayeeName => Transaction.Payee.Name;
    public string CategoryName => Transaction.Category.Name;
    public string SubcategoryName => Transaction.Category.Subcategory ?? string.Empty;
    public string PaymentModeName => string.IsNullOrWhiteSpace(Transaction.PaymentMode) ? "Non renseigné" : Transaction.PaymentMode;
    public string DebitText { get; }
    public string CreditText { get; }
    public string BalanceText { get; }
    public string MarkText { get; }
    public decimal Balance { get; }
    public decimal? Debit { get; }
    public decimal? Credit { get; }
}
