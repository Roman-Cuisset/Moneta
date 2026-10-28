using System.Globalization;

namespace Moneta.Core.Presentation;

public sealed class AccountSummary
{
    internal AccountSummary(Account account, decimal balance)
    {
        Account = account;
        BalanceText = MoneyFormatter.Format(balance, account.Currency);
    }

    public Account Account { get; }
    public string Name => Account.Name;
    public string CurrencyCode => Account.Currency.Code;
    public string BalanceText { get; }
}

public sealed class LedgerRow
{
    internal LedgerRow(Transaction transaction, decimal balance)
    {
        Transaction = transaction;
        Balance = balance;
        DateText = transaction.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        Debit = transaction.Direction == TransactionDirection.Debit ? transaction.Amount : null;
        Credit = transaction.Direction == TransactionDirection.Credit ? transaction.Amount : null;
        DebitText = Debit is decimal debit ? MoneyFormatter.Format(debit, transaction.Currency) : string.Empty;
        CreditText = Credit is decimal credit ? MoneyFormatter.Format(credit, transaction.Currency) : string.Empty;
        BalanceText = MoneyFormatter.Format(balance, transaction.Currency);
        MarkText = transaction.Status switch
        {
            ReconciliationStatus.Unmarked => "non pointé",
            ReconciliationStatus.Marked => "pointé",
            ReconciliationStatus.Reconciled => "rapproché",
            _ => throw new ArgumentOutOfRangeException(nameof(transaction), "État de rapprochement inconnu.")
        };
    }

    public Transaction Transaction { get; }
    public string DateText { get; }
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
