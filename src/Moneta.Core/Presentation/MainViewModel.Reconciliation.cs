using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    private ReconciliationBatch _pendingReconciliations = ReconciliationBatch.Empty;
    private AccountLedgerTotals? _totals;
    private bool _showMarkingDates;

    public ReconciliationBatch PendingReconciliations => _pendingReconciliations;
    public IReconciliationSource? ReconciliationSource => _source as IReconciliationSource;
    public bool CanReconcile => !IsDemo && ReconciliationSource is not null && !IsDirty &&
        _pending.Count == 0 && _pendingReconciliations.IsEmpty && _selectedAccount is { Account.IsArchived: false };
    public decimal? PointedBalance => _totals?.PointedBalance;
    public decimal? UnmarkedAmount => _totals?.UnmarkedAmount;
    public string SelectedPointedBalanceText => FormatTotal(PointedBalance);
    public string SelectedUnmarkedAmountText => FormatTotal(UnmarkedAmount);
    public string DateColumnHeader => ShowMarkingDates ? "Date de pointage" : "Date";
    public bool ShowMarkingDates
    {
        get => _showMarkingDates;
        set
        {
            if (_showMarkingDates == value) return;
            var anchor = _selectedRow?.Transaction.Id;
            var rows = Array.AsReadOnly(_rows.Select(row => new LedgerRow(row.Transaction, row.Balance, value)).ToArray());
            _showMarkingDates = value;
            PublishRows(rows, anchor);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMarkingDates)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateColumnHeader)));
        }
    }

    private string FormatTotal(decimal? value) => value is { } amount && _selectedAccount is { } selected
        ? MoneyFormatter.Format(amount, selected.Account.Currency) : string.Empty;

    private void NotifyReconciliationChanged()
    {
        foreach (var property in new[] { nameof(PendingReconciliations), nameof(CanReconcile), nameof(PointedBalance),
            nameof(UnmarkedAmount), nameof(SelectedPointedBalanceText), nameof(SelectedUnmarkedAmountText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private static AccountLedgerTotals DemoTotals(Account account, IReadOnlyList<LedgerRow> rows)
    {
        var current = account.OpeningBalance;
        var pointed = current;
        foreach (var row in rows)
        {
            var net = FinancialRules.SignedAmount(row.Transaction);
            current = checked(current + net);
            if (row.Transaction.Status != ReconciliationStatus.Unmarked) pointed = checked(pointed + net);
        }
        return new AccountLedgerTotals(current, pointed, checked(current - pointed));
    }

    private static void EnsureNotReconciled(Transaction value)
    {
        if (value.Status == ReconciliationStatus.Reconciled)
            throw new InvalidOperationException("Cette opération est rapprochée. Rouvrez le relevé ou libérez explicitement le rapprochement hérité avant de la modifier.");
    }

    public void SetPointing(IReadOnlyCollection<TransactionSelection> selection, bool pointed, DateOnly? markedOn = null) =>
        StagePointing(selection, _ => pointed, markedOn);

    public void TogglePointing(IReadOnlyCollection<TransactionSelection> selection) =>
        StagePointing(selection, value => value.Status == ReconciliationStatus.Unmarked, null);

    private void StagePointing(IReadOnlyCollection<TransactionSelection> selection, Func<Transaction, bool> target, DateOnly? markedOn)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var account = EditableAccount();
        if (selection.Count == 0) throw new ArgumentException("Sélectionnez au moins une opération.", nameof(selection));
        var ids = new HashSet<Guid>();
        var updates = new List<Transaction>(selection.Count);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var item in selection)
        {
            if (!ids.Add(item.Id)) throw new ArgumentException("Une opération apparaît plusieurs fois dans la sélection.");
            // Status clicks operate only on the already loaded, bounded page: never reread the ledger.
            if (!_rows.Any(row => row.Transaction.Id == item.Id))
                throw new InvalidOperationException("La sélection n’appartient pas à la page courante.");
            var original = FindEditable(item.Id, item.Revision, account);
            var pointed = target(original);
            if (pointed && original.Status == ReconciliationStatus.Marked && markedOn is null) continue;
            var date = pointed ? markedOn ?? original.MarkedOn ?? today : today;
            ReconciliationRules.ValidatePointing(original, pointed, date, today);
            var value = original with
            {
                Status = pointed ? ReconciliationStatus.Marked : ReconciliationStatus.Unmarked,
                MarkedOn = pointed ? (original.Status == ReconciliationStatus.Marked && markedOn is null ? original.MarkedOn : date) : null
            };
            if (value.Status == original.Status && value.MarkedOn == original.MarkedOn) continue;
            updates.Add(NextRevision(value, original));
        }
        if (updates.Count != 0) PublishStatusChanges(updates, _pendingReconciliations);
    }

    public void ChangeMarkingDate(TransactionSelection selection, DateOnly date)
    {
        var account = EditableAccount();
        var original = FindEditable(selection.Id, selection.Revision, account);
        if (original.Status != ReconciliationStatus.Marked)
            throw new InvalidOperationException("Seule la date d’une opération pointée peut être modifiée.");
        ReconciliationRules.ValidatePointing(original, true, date, DateOnly.FromDateTime(DateTime.Today));
        if (original.MarkedOn != date)
            PublishStatusChanges([NextRevision(original with { MarkedOn = date }, original)], _pendingReconciliations);
    }

    private Transaction NextRevision(Transaction value, Transaction previous)
    {
        var expected = _pending.TryGetValue(value.Id, out var pending) ? pending.ExpectedRevision : previous.Revision;
        var now = DateTimeOffset.UtcNow;
        return value with { Revision = expected == 0 ? 1 : checked(expected + 1), ModifiedUtc = now < previous.ModifiedUtc ? previous.ModifiedUtc : now };
    }

    private void PublishStatusChanges(IReadOnlyList<Transaction> updates, ReconciliationBatch batch)
    {
        var proposed = new Dictionary<Guid, TransactionChange>(_pending);
        var values = updates.ToDictionary(value => value.Id);
        var totals = _totals;
        foreach (var value in updates)
        {
            FinancialRules.ValidateTransaction(value, _document!);
            var original = _pending.TryGetValue(value.Id, out var existing) ? existing.Value
                : _rows.FirstOrDefault(row => row.Transaction.Id == value.Id)?.Transaction;
            if (original is null) throw new InvalidOperationException("Opération absente de la page courante.");
            var expected = existing?.ExpectedRevision ?? original.Revision;
            proposed[value.Id] = new TransactionChange(value, expected);
            if (totals is not null && (value.Status == ReconciliationStatus.Unmarked) != (original.Status == ReconciliationStatus.Unmarked))
            {
                var delta = value.Status == ReconciliationStatus.Unmarked ? -FinancialRules.SignedAmount(value) : FinancialRules.SignedAmount(value);
                totals = totals with { PointedBalance = checked(totals.PointedBalance + delta), UnmarkedAmount = checked(totals.UnmarkedAmount - delta) };
            }
        }
        var rows = Array.AsReadOnly(_rows.Select(row => values.TryGetValue(row.Transaction.Id, out var value)
            ? new LedgerRow(value, row.Balance, _showMarkingDates) : row).ToArray());
        _pending = proposed;
        _pendingReconciliations = batch;
        _totals = totals;
        PublishRows(rows, _selectedRow?.Transaction.Id);
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingChanges)));
        NotifyReconciliationChanged();
    }

    private void PublishRows(IReadOnlyList<LedgerRow> rows, Guid? anchor)
    {
        _rows = rows;
        _selectedRow = anchor is { } id ? rows.FirstOrDefault(row => row.Transaction.Id == id) : null;
        _selectedRowDetails = _selectedRow is null ? string.Empty : FormatDetails(_selectedRow);
        foreach (var args in new[] { RowsChanged, RowChanged, DetailsChanged }) PropertyChanged?.Invoke(this, args);
    }

    private (Account Account, IReconciliationSource Source) CleanReconciliationContext()
    {
        var account = EditableAccount();
        if (!CanReconcile) throw new InvalidOperationException("Enregistrez d’abord le dossier et tous ses brouillons avant cette action de rapprochement.");
        return (account, ReconciliationSource!);
    }

    public ReconciliationSession StageReconciliation(ReconciliationRequest request, IReadOnlyCollection<Guid> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(selectedIds);
        var (account, source) = CleanReconciliationContext();
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (request.StatementDate == default || request.StatementDate > today)
            throw new ArgumentException("La date du relevé ne peut pas être future.");
        if (request.StatementNumber is null || request.StatementNumber.Length > 120)
            throw new ArgumentException("Le numéro de relevé est limité à 120 caractères.");
        _ = ExactMoney.ToMinorUnits(request.ExpectedClosingBalance, account.Currency);
        var ids = selectedIds.ToHashSet();
        if (ids.Count != selectedIds.Count) throw new ArgumentException("La sélection contient des identifiants dupliqués.");
        var preview = source.ReadReconciliationCandidates(account, request.StatementDate, ids, 0, PageSize);
        if (preview.LegacyReconciledCount != 0)
            throw new InvalidOperationException("Libérez explicitement les rapprochements hérités avant de valider un relevé.");
        var originals = source.ReadTransactions(ids.ToArray());
        if (originals.Count != ids.Count || originals.Select(value => value.Id).Distinct().Count() != ids.Count || originals.Any(value => !ids.Contains(value.Id)))
            throw new InvalidOperationException("La sélection du relevé a changé ou contient une opération absente.");
        var previous = preview.PreviousSessionId is { } previousId ? source.ReadReconciliation(previousId).Session : null;
        if (previous is { State: not ReconciliationSessionState.Validated }) throw new InvalidOperationException("Le relevé précédent n’est plus actif.");
        var now = DateTimeOffset.UtcNow;
        var session = new ReconciliationSession(Guid.NewGuid(), account.Id, request.StatementDate, request.StatementNumber.Trim(),
            preview.OpeningBalance, request.ExpectedClosingBalance, checked(preview.OpeningBalance + preview.SelectedNet), previous?.Id, now);
        var entries = Array.AsReadOnly(originals.Select(value => new ReconciliationEntry(session.Id, value)).ToArray());
        ReconciliationRules.ValidateSession(session, account, previous, entries);
        if (preview.SelectedCount != ids.Count || originals.Aggregate(0m, (sum, value) => checked(sum + FinancialRules.SignedAmount(value))) != preview.SelectedNet)
            throw new InvalidOperationException("La sélection a changé depuis le calcul du relevé.");
        foreach (var visible in preview.Transactions.Where(value => ids.Contains(value.Id)))
        {
            var actual = originals.Single(value => value.Id == visible.Id);
            if (actual.Revision != visible.Revision || actual.Status != visible.Status || actual.MarkedOn != visible.MarkedOn ||
                !ReconciliationRules.FinancialPayloadEquals(actual, visible)) throw new InvalidOperationException("Une opération du relevé a changé.");
        }
        var updates = originals.Select(value => NextRevision(value with
        { Status = ReconciliationStatus.Reconciled, ReconciliationId = session.Id, MarkedOn = value.MarkedOn ?? today }, value)).ToArray();
        var batch = new ReconciliationBatch([new(session, 0, entries)],
            [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Validated, now, string.Empty)]);
        PublishSessionChanges(updates, batch);
        return session;
    }

    public void ReopenReconciliation(Guid sessionId, long expectedRevision, string reason)
    {
        var (account, source) = CleanReconciliationContext();
        ReconciliationRules.ValidateReason(reason);
        var details = source.ReadReconciliation(sessionId);
        var session = details.Session;
        if (session.AccountId != account.Id || session.Revision != expectedRevision || session.State != ReconciliationSessionState.Validated)
            throw new InvalidOperationException("Ce relevé a changé ou n’est plus actif.");
        var latest = source.ReadReconciliationCandidates(account, DateOnly.MaxValue, new HashSet<Guid>(), 0, 1);
        if (latest.PreviousSessionId != session.Id) throw new InvalidOperationException("Rouvrez d’abord les relevés actifs plus récents.");
        var entries = details.Entries;
        var ids = entries.Select(entry => entry.Original.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length || entries.Any(entry => entry.SessionId != sessionId))
            throw new InvalidOperationException("Les instantanés du relevé sont incohérents.");
        var current = source.ReadTransactions(ids).ToDictionary(value => value.Id);
        if (current.Count != ids.Length) throw new InvalidOperationException("Une opération du relevé est absente.");
        var updates = new List<Transaction>(entries.Count);
        foreach (var entry in entries)
        {
            if (!current.TryGetValue(entry.Original.Id, out var value) || value.Status != ReconciliationStatus.Reconciled ||
                value.ReconciliationId != session.Id || !ReconciliationRules.FinancialPayloadEquals(value, entry.Original))
                throw new InvalidOperationException("Le contenu financier d’une opération rapprochée a changé.");
            updates.Add(NextRevision(value with { Status = entry.Original.Status, MarkedOn = entry.Original.MarkedOn, ReconciliationId = null }, value));
        }
        var now = DateTimeOffset.UtcNow;
        if (now < session.ValidatedUtc) now = session.ValidatedUtc;
        var reopened = session with { State = ReconciliationSessionState.Reopened, Revision = checked(session.Revision + 1), ReopenedUtc = now, ReopenReason = reason.Trim() };
        PublishSessionChanges(updates, new ReconciliationBatch([new(reopened, expectedRevision, Array.Empty<ReconciliationEntry>())],
            [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Reopened, now, reason.Trim())]));
    }

    public void ReleaseLegacyReconciliation(TransactionSelection selection, string reason)
    {
        var (account, source) = CleanReconciliationContext();
        ReconciliationRules.ValidateReason(reason);
        var values = source.ReadTransactions([selection.Id]);
        if (values.Count != 1) throw new InvalidOperationException("L’opération héritée est absente.");
        var original = values[0];
        if (original.Id != selection.Id || original.AccountId != account.Id || original.Revision != selection.Revision || original.IsDeleted ||
            original.Status != ReconciliationStatus.Reconciled || original.ReconciliationId is not null)
            throw new InvalidOperationException("Cette opération n’est pas un rapprochement hérité libérable.");
        var value = NextRevision(original with { Status = ReconciliationStatus.Unmarked, MarkedOn = null, ReconciliationId = null }, original);
        var entry = new ReconciliationEvent(Guid.NewGuid(), null, original.Id, ReconciliationEventKind.LegacyReleased, value.ModifiedUtc, reason.Trim()) { OperationBefore = original };
        PublishSessionChanges([value], new ReconciliationBatch(Array.Empty<ReconciliationSessionChange>(), [entry]));
    }

    private void PublishSessionChanges(IReadOnlyList<Transaction> updates, ReconciliationBatch batch)
    {
        var proposed = new Dictionary<Guid, TransactionChange>(_pending);
        foreach (var value in updates)
        {
            FinancialRules.ValidateTransaction(value, _document!);
            proposed.Add(value.Id, new TransactionChange(value, checked(value.Revision - 1)));
        }
        // A full page refresh is appropriate for a formal session, never for a status click.
        // Read and construct everything before publishing either half of the atomic draft.
        var page = ReadPage(_selectedAccount!.Account, _pageOffset, proposed.Values.ToArray());
        var rows = BuildRows(page);
        _pending = proposed;
        _pendingReconciliations = batch;
        ApplyPage(page, rows, _selectedRow?.Transaction.Id);
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingChanges)));
        NotifyReconciliationChanged();
    }
}
