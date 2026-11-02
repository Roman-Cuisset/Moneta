namespace Moneta.Core;

// Amounts and account currencies remain authoritative on the linked transactions.
// Relationships live separately so historical reconciliation snapshots V1 never change.
public sealed record Transfer(
    Guid Id, Guid DebitTransactionId, Guid CreditTransactionId, Guid? FeeTransactionId,
    DateOnly Date, string Reference, string Notes, DateOnly? ExchangeDate, string? RateSource,
    DateTimeOffset CreatedUtc, DateTimeOffset ModifiedUtc, long Revision = 1, bool IsDeleted = false);

public sealed record TransferDetails(Transfer Value, Transaction Debit, Transaction Credit, Transaction? Fee)
{
    public bool HasReconciledLeg => Debit.Status == ReconciliationStatus.Reconciled ||
        Credit.Status == ReconciliationStatus.Reconciled || Fee?.Status == ReconciliationStatus.Reconciled;
    public bool IsCrossCurrency => Debit.Currency.Code != Credit.Currency.Code;
    // Quote units for one source unit. The exact ratio is Credit.Amount / Debit.Amount;
    // any decimal quotient displayed by the UI is approximate and excludes fees.
    public int QuoteScale => 1;
}

public sealed record TransferChange(Transfer Value, long ExpectedRevision);

public sealed record TransferDraft(
    Guid SourceAccountId, Guid DestinationAccountId, DateOnly Date,
    decimal DebitedAmount, decimal CreditedAmount, string Reference = "", string Notes = "",
    DateOnly? ExchangeDate = null, string? RateSource = null,
    Guid? FeeAccountId = null, decimal FeeAmount = 0m);

public interface ITransferSource
{
    TransferDetails ReadTransfer(Guid id);
    Guid? FindTransferId(Guid transactionId);
}
