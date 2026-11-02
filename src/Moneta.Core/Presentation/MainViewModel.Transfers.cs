using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    private Dictionary<Guid, TransferChange> _pendingTransfers = [];

    public IReadOnlyCollection<TransferChange> PendingTransfers => Array.AsReadOnly(_pendingTransfers.Values.ToArray());

    public TransferDetails CreateTransfer(TransferDraft draft)
    {
        var metadata = TransferContext();
        var details = BuildTransfer(draft, metadata, null);
        StageTransfer(metadata, details, 0);
        return details;
    }

    public TransferDetails EditTransfer(Guid transferId, long expectedRevision, TransferDraft draft)
    {
        var metadata = TransferContext();
        var previous = EditableTransfer(transferId, expectedRevision);
        var details = BuildTransfer(draft, metadata, previous);
        var expected = _pendingTransfers.TryGetValue(transferId, out var pending) ? pending.ExpectedRevision : previous.Value.Revision;
        details = details with { Value = details.Value with { Revision = expected == 0 ? 1 : checked(expected + 1) } };
        StageTransfer(metadata, details, expected);
        return details;
    }

    public void DeleteTransfer(Guid transferId, long expectedRevision)
    {
        var metadata = TransferContext();
        var previous = EditableTransfer(transferId, expectedRevision);
        foreach (var leg in TransferLegs(previous)) _ = ActiveTransferAccount(metadata, leg.AccountId);
        var expected = _pendingTransfers.TryGetValue(transferId, out var pending) ? pending.ExpectedRevision : previous.Value.Revision;
        var discardedFee = expected != 0 && previous.Fee is { } newFee &&
            _pending.TryGetValue(newFee.Id, out var feeChange) && feeChange.ExpectedRevision == 0 ? newFee : null;
        if (discardedFee is not null)
            previous = previous with { Value = previous.Value with { FeeTransactionId = null }, Fee = null };
        var now = TransferNow(previous);
        var deleted = previous with
        {
            Value = previous.Value with { IsDeleted = true, ModifiedUtc = now, Revision = expected == 0 ? 1 : checked(expected + 1) },
            Debit = NextRevision(previous.Debit with { IsDeleted = true }, previous.Debit),
            Credit = NextRevision(previous.Credit with { IsDeleted = true }, previous.Credit),
            Fee = previous.Fee is { } fee ? NextRevision(fee with { IsDeleted = true }, fee) : null
        };
        StageTransfer(metadata, deleted, expected, remove: expected == 0, discardedFee: discardedFee);
    }

    public TransferDetails GetTransfer(Guid transferId)
    {
        if (IsDemo) throw new InvalidOperationException("La démonstration ne contient aucun virement réel.");
        TransferDetails details;
        if (_pendingTransfers.TryGetValue(transferId, out var pending))
        {
            var value = pending.Value;
            details = new TransferDetails(value, PendingLeg(value.DebitTransactionId), PendingLeg(value.CreditTransactionId),
                value.FeeTransactionId is { } feeId ? PendingLeg(feeId) : null);
        }
        else
        {
            var source = _source as ITransferSource ?? throw new InvalidOperationException("Le registre des virements n'est pas connecté.");
            details = source.ReadTransfer(transferId);
        }
        return details with
        {
            Debit = OverlayLeg(details.Debit), Credit = OverlayLeg(details.Credit),
            Fee = details.Fee is { } fee ? OverlayLeg(fee) : null
        };
    }

    TransferDetails ITransferSource.ReadTransfer(Guid id) => GetTransfer(id);

    public Guid? FindTransferId(Guid transactionId)
    {
        foreach (var change in _pendingTransfers.Values)
            if (ContainsLeg(change.Value, transactionId)) return change.Value.Id;
        return (_source as ITransferSource)?.FindTransferId(transactionId);
    }

    public void NavigateToTransferCounterpart(Guid transferId, Guid transactionId)
    {
        var details = GetTransfer(transferId);
        if (details.Value.IsDeleted || !ContainsLeg(details.Value, transactionId))
            throw new InvalidOperationException("Cette opération n'appartient pas à un virement actif.");
        var target = transactionId == details.Debit.Id ? details.Credit : details.Debit;
        var summary = _ledgers.TryGetValue(target.AccountId, out var ledger) ? ledger.Summary
            : throw new InvalidOperationException("Le compte de contrepartie est absent du dossier.");
        // Read the ID-anchored page before publishing any account or row selection.
        var page = ReadPage(summary.Account, 0, PendingChanges, target.Id);
        var rows = BuildRows(page);
        if (!rows.Any(row => row.Transaction.Id == target.Id))
            throw new InvalidOperationException("La contrepartie n'est pas présente dans le registre.");
        SelectAccount(summary, page, rows);
        ApplyPage(page, rows, target.Id);
        SetLedgerError(string.Empty);
    }

    private FinancialDocument TransferContext()
    {
        if (IsDemo) throw new InvalidOperationException("Les virements réels ne sont pas disponibles en démonstration.");
        if (!_pendingReconciliations.IsEmpty)
            throw new InvalidOperationException("Enregistrez d’abord le brouillon de rapprochement avec Ctrl+S avant de modifier les virements.");
        return CaptureDocument();
    }

    private TransferDetails EditableTransfer(Guid id, long revision)
    {
        var details = GetTransfer(id);
        if (details.Value.IsDeleted) throw new InvalidOperationException("Ce virement est supprimé.");
        if (details.Value.Revision != revision)
            throw new InvalidOperationException("Ce virement a changé ; ouvrez à nouveau sa version actuelle.");
        if (details.HasReconciledLeg)
            throw new InvalidOperationException("Une jambe du virement est rapprochée. Rouvrez le relevé ou libérez le rapprochement hérité, puis enregistrez avec Ctrl+S.");
        return details;
    }

    private static Account ActiveTransferAccount(FinancialDocument metadata, Guid id)
    {
        var account = metadata.Accounts.SingleOrDefault(value => value.Id == id)
            ?? throw new ArgumentException("Le compte du virement est absent du dossier.");
        if (account.IsArchived) throw new InvalidOperationException("Un compte archivé ne peut pas participer à une modification de virement.");
        return account;
    }

    private TransferDetails BuildTransfer(TransferDraft draft, FinancialDocument metadata, TransferDetails? previous)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var source = ActiveTransferAccount(metadata, draft.SourceAccountId);
        var destination = ActiveTransferAccount(metadata, draft.DestinationAccountId);
        if (previous is not null && (source.Id != previous.Debit.AccountId || destination.Id != previous.Credit.AccountId))
            throw new InvalidOperationException("Les comptes d'un virement sont immuables : annulez le virement entier et recréez-le.");
        if (previous?.Fee is { } oldFee && draft.FeeAccountId != oldFee.AccountId)
            throw new InvalidOperationException("Le compte des frais est immuable ; conservez la ligne et saisissez zéro pour annuler les frais.");
        Account? feeAccount = draft.FeeAccountId is { } feeId ? ActiveTransferAccount(metadata, feeId) : null;
        if (feeAccount is null && draft.FeeAmount != 0)
            throw new ArgumentException("Choisissez le compte des frais.", nameof(draft));
        if (feeAccount is not null && (draft.FeeAmount < 0 || previous?.Fee is null && draft.FeeAmount == 0))
            throw new ArgumentException("Les nouveaux frais doivent être strictement positifs ; un frais existant peut être ramené à zéro.", nameof(draft));
        var reference = draft.Reference?.Trim() ?? throw new ArgumentException("La référence du virement est invalide.", nameof(draft));
        var notes = draft.Notes ?? throw new ArgumentException("Les notes du virement sont invalides.", nameof(draft));
        var crossCurrency = source.Currency.Code != destination.Currency.Code;
        var now = previous is null ? DateTimeOffset.UtcNow : TransferNow(previous);
        var catalog = new DraftCatalog(metadata);
        Transaction BuildLeg(Account account, decimal amount, TransactionDirection direction, string label, Transaction? old)
        {
            var value = catalog.Build(account, new TransactionDraft(draft.Date, amount, direction,
                reference.Length == 0 ? label : $"{label} · {reference}", Notes: notes), old);
            return old is null ? value : NextRevision(value, old);
        }
        var debit = BuildLeg(source, draft.DebitedAmount, TransactionDirection.Debit, "Virement émis", previous?.Debit);
        var credit = BuildLeg(destination, draft.CreditedAmount, TransactionDirection.Credit, "Virement reçu", previous?.Credit);
        var fee = feeAccount is null ? null : BuildLeg(feeAccount, draft.FeeAmount, TransactionDirection.Debit, "Frais de virement", previous?.Fee);
        var value = new Transfer(previous?.Value.Id ?? Guid.NewGuid(), debit.Id, credit.Id, fee?.Id, draft.Date,
            reference, notes, crossCurrency ? draft.ExchangeDate ?? draft.Date : null,
            crossCurrency ? string.IsNullOrWhiteSpace(draft.RateSource) ? "Montants saisis par l’utilisateur" : draft.RateSource.Trim() : null,
            previous?.Value.CreatedUtc ?? now, now, previous?.Value.Revision ?? 1);
        var details = new TransferDetails(value, debit, credit, fee);
        TransferRules.Validate(details, metadata);
        return details;
    }

    private void StageTransfer(FinancialDocument metadata, TransferDetails details, long expectedRevision,
        bool remove = false, Transaction? discardedFee = null)
    {
        TransferRules.Validate(details, metadata);
        var proposed = new Dictionary<Guid, TransactionChange>(_pending);
        var transfers = new Dictionary<Guid, TransferChange>(_pendingTransfers);
        if (discardedFee is not null) proposed.Remove(discardedFee.Id);
        foreach (var leg in TransferLegs(details))
        {
            if (remove) proposed.Remove(leg.Id);
            else
            {
                var expected = proposed.TryGetValue(leg.Id, out var existing) ? existing.ExpectedRevision
                    : checked(leg.Revision - 1);
                proposed[leg.Id] = new TransactionChange(leg, expected);
            }
        }
        if (remove) transfers.Remove(details.Value.Id);
        else transfers[details.Value.Id] = new TransferChange(details.Value, expectedRevision);
        var pending = proposed.Values.ToArray();
        var selected = _selectedAccount ?? _ledgers[details.Debit.AccountId].Summary;
        var affected = TransferLegs(details).Select(leg => leg.AccountId).Append(selected.Account.Id)
            .Append(discardedFee?.AccountId ?? selected.Account.Id).Distinct();
        var pages = new Dictionary<Guid, (LedgerPage Page, IReadOnlyList<LedgerRow> Rows)>();
        foreach (var id in affected)
        {
            var account = _ledgers[id].Summary.Account;
            var anchor = !remove && !details.Value.IsDeleted ? TransferLegs(details).FirstOrDefault(leg => leg.AccountId == id)?.Id : null;
            if (id == selected.Account.Id) anchor ??= _selectedRow?.Transaction.Id;
            var page = ReadPage(account, id == selected.Account.Id ? _pageOffset : 0, pending, anchor);
            var rows = BuildRows(page, transfers);
            pages.Add(id, (page, rows));
        }
        // All validation, source reads, progressive balances and P/NP totals have succeeded.
        _document = metadata;
        _pending = proposed;
        _pendingTransfers = transfers;
        foreach (var (id, prepared) in pages) _ledgers[id].Summary.UpdateBalance(prepared.Page.AccountBalance);
        var visible = pages[selected.Account.Id];
        SelectAccount(selected, visible.Page, visible.Rows);
        var selectedId = !remove && !details.Value.IsDeleted
            ? TransferLegs(details).FirstOrDefault(leg => leg.AccountId == selected.Account.Id)?.Id : null;
        ApplyPage(visible.Page, visible.Rows, selectedId);
        SetLedgerError(string.Empty);
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingChanges)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingTransfers)));
    }

    private Transaction PendingLeg(Guid id) => _pending.TryGetValue(id, out var change) ? change.Value
        : throw new InvalidOperationException("Une jambe du brouillon de virement est absente.");

    private Transaction OverlayLeg(Transaction value) => _pending.TryGetValue(value.Id, out var change) ? change.Value : value;

    private void EnsureOrdinaryTransaction(Guid id)
    {
        if (FindTransferId(id) is not null)
            throw new InvalidOperationException("Cette opération est liée à un virement. Modifiez ou supprimez le virement entier.");
    }

    private static bool ContainsLeg(Transfer value, Guid id) => value.DebitTransactionId == id || value.CreditTransactionId == id || value.FeeTransactionId == id;

    private static IEnumerable<Transaction> TransferLegs(TransferDetails details)
    {
        yield return details.Debit;
        yield return details.Credit;
        if (details.Fee is { } fee) yield return fee;
    }

    private static DateTimeOffset TransferNow(TransferDetails previous)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < previous.Value.ModifiedUtc) now = previous.Value.ModifiedUtc;
        foreach (var leg in TransferLegs(previous)) if (now < leg.ModifiedUtc) now = leg.ModifiedUtc;
        return now;
    }

    private static IReadOnlyDictionary<Guid, Guid> BuildTransferLinks(LedgerPage page, IReadOnlyDictionary<Guid, TransferChange> transfers)
    {
        if (transfers.Count == 0) return page.TransferIds;
        var links = new Dictionary<Guid, Guid>(page.TransferIds);
        foreach (var transaction in page.Transactions)
            foreach (var change in transfers.Values)
                if (ContainsLeg(change.Value, transaction.Id))
                {
                    links[transaction.Id] = change.Value.Id;
                    break;
                }
        return links;
    }
}
