namespace Moneta.Core;

public sealed record LedgerCategory(Guid Id, string Name, Guid? ParentId = null);
public sealed record LedgerPayee(Guid Id, string Name);
public sealed record PaymentMethod(Guid Id, string Name);

// Amount is a nonnegative magnitude; Direction alone determines its effect on the balance.
public sealed record TransactionDraft(
    DateOnly Date, decimal Amount, TransactionDirection Direction, string Label,
    string? PayeeName = null, string? CategoryName = null, string? SubcategoryName = null,
    string? PaymentMode = null, string? Notes = null);

// ExpectedRevision is zero only for an insertion; deletion retains the full record as a tombstone.
public sealed record TransactionChange(Transaction Value, long ExpectedRevision);

public sealed record LedgerPage(
    IReadOnlyList<Transaction> Transactions, decimal BalanceBeforePage,
    decimal AccountBalance, long TotalCount, int Offset);

// Storage supplies only the requested page; pending edits remain private to the workspace until Save.
public interface ILedgerSource
{
    LedgerPage ReadPage(Account account, int offset, int pageSize,
        IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null);
}
