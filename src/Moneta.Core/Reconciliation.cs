namespace Moneta.Core;

public sealed record TransactionSelection(Guid Id, long Revision);
public sealed record AccountLedgerTotals(decimal CurrentBalance, decimal PointedBalance, decimal UnmarkedAmount);

public enum ReconciliationSessionState { Validated, Reopened }
public enum ReconciliationEventKind { Validated, Reopened, LegacyReleased }

public sealed record ReconciliationRequest(DateOnly StatementDate, decimal ExpectedClosingBalance, string StatementNumber = "");

// A reopened session remains an immutable historical statement; revalidation creates a new session.
public sealed record ReconciliationSession(
    Guid Id, Guid AccountId, DateOnly StatementDate, string StatementNumber,
    decimal OpeningBalance, decimal ExpectedClosingBalance, decimal CalculatedBalance,
    Guid? PreviousSessionId, DateTimeOffset ValidatedUtc,
    ReconciliationSessionState State = ReconciliationSessionState.Validated,
    long Revision = 1, DateTimeOffset? ReopenedUtc = null, string? ReopenReason = null);

// Snapshot before validation, including the original pointing state; never overwritten during reopening.
public sealed record ReconciliationEntry(Guid SessionId, Transaction Original);
public sealed record ReconciliationEvent(
    Guid Id, Guid? SessionId, Guid? TransactionId, ReconciliationEventKind Kind,
    DateTimeOffset OccurredUtc, string Reason)
{
    public Transaction? OperationBefore { get; init; }
}
public sealed record ReconciliationSessionChange(
    ReconciliationSession Value, long ExpectedRevision, IReadOnlyList<ReconciliationEntry> Entries);
public sealed record ReconciliationBatch(
    IReadOnlyList<ReconciliationSessionChange> Sessions, IReadOnlyList<ReconciliationEvent> Events)
{
    public static ReconciliationBatch Empty { get; } = new(Array.Empty<ReconciliationSessionChange>(), Array.Empty<ReconciliationEvent>());
    public bool IsEmpty => Sessions.Count == 0 && Events.Count == 0;
}

public sealed record ReconciliationCandidatePage(
    IReadOnlyList<Transaction> Transactions, long TotalCount, int Offset,
    decimal OpeningBalance, decimal SelectedNet, long SelectedCount,
    Guid? PreviousSessionId, long LegacyReconciledCount);
public sealed record ReconciliationHistoryPage(IReadOnlyList<ReconciliationSession> Sessions, long TotalCount, int Offset);
public sealed record ReconciliationDetails(ReconciliationSession Session,
    IReadOnlyList<ReconciliationEntry> Entries, IReadOnlyList<ReconciliationEvent> Events);

// Separate from the ledger interface so existing bounded-page consumers remain usable.
public interface IReconciliationSource
{
    ReconciliationCandidatePage ReadReconciliationCandidates(Account account, DateOnly statementDate,
        IReadOnlySet<Guid> selectedIds, int offset, int pageSize);
    IReadOnlyList<Transaction> ReadTransactions(IReadOnlyCollection<Guid> ids);
    ReconciliationHistoryPage ReadReconciliationHistory(Guid accountId, int offset, int pageSize);
    ReconciliationDetails ReadReconciliation(Guid sessionId);
}
