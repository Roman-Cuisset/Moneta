using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    private FinancialDocument? _savedScheduleState;

    public IReadOnlyList<SchedulePlan> Schedules => _document?.Schedules ?? [];
    public IReadOnlyList<ScheduleDecision> ScheduleHistory => _document?.ScheduleHistory ?? [];
    public ScheduleCalendar ScheduleCalendar() => new(Schedules, ScheduleHistory);

    public bool IsScheduleDecisionSaved(ScheduleDecision decision) =>
        _savedScheduleState?.ScheduleHistory.Contains(decision) == true;

    public SchedulePlan AddSchedule(ScheduleTerms terms, ScheduleRule rule, DateOnly first, DateOnly? end = null)
    {
        var document = CaptureDocument();
        var plan = new SchedulePlan(Guid.NewGuid(), terms, rule, first, end);
        var accounts = document.Accounts.ToDictionary(account => account.Id);
        plan.Validate(accounts);
        if (plan.Next is null) throw new ArgumentException("La fin précède la première échéance.");
        terms.Validate(accounts, active: true);
        StageSchedule(document with { Schedules = [.. Schedules, plan] });
        return plan;
    }

    public void EditSchedule(Guid id, ScheduleTerms terms, ScheduleRule rule, DateOnly next, DateOnly? end)
    {
        var document = CaptureDocument();
        var existing = FindSchedule(id);
        var reanchor = existing.Rule != rule || existing.Next != next;
        var changed = existing with
        {
            Terms = terms,
            Rule = rule,
            Anchor = reanchor ? next : existing.Anchor,
            Cursor = reanchor ? 0 : existing.Cursor,
            End = end
        };
        changed.Validate(document.Accounts.ToDictionary(account => account.Id));
        var handled = ScheduleHistory.Where(item => item.PlanId == id && item.Step == 1)
            .Select(item => item.OriginalDate).DefaultIfEmpty().Max();
        if (changed.Next is not { } date) throw new ArgumentException("La fin précède la prochaine échéance.");
        if (date <= handled) throw new ArgumentException("La prochaine date doit suivre les occurrences ayant déjà reçu une décision.");
        if (changed != existing) StageSchedule(ReplacePlan(document, changed));
    }

    public void SuspendSchedule(Guid id, bool suspended)
    {
        var plan = FindSchedule(id);
        if (plan.Suspended != suspended)
            StageSchedule(ReplacePlan(CaptureDocument(), plan with { Suspended = suspended }));
    }

    public void RetireSchedule(Guid id)
    {
        var plan = FindSchedule(id);
        StageSchedule(ReplacePlan(CaptureDocument(), plan with { Retired = true }));
    }

    public void DeferScheduleOccurrence(ScheduleOccurrence occurrence, DateOnly date)
    {
        var plan = CheckOccurrence(occurrence);
        if (date <= occurrence.Date) throw new ArgumentException("Choisissez une date de report postérieure à l’échéance.");
        StageSchedule(RecordDecision(CaptureDocument(), plan, occurrence, date, ScheduleOutcome.Deferred, occurrence.Terms));
    }

    public void SkipScheduleOccurrence(ScheduleOccurrence occurrence)
    {
        var plan = CheckOccurrence(occurrence);
        StageSchedule(RecordDecision(CaptureDocument(), plan, occurrence, occurrence.Date, ScheduleOutcome.Skipped, occurrence.Terms));
    }

    public void PostScheduleOccurrence(ScheduleOccurrence occurrence, ScheduleTerms terms, DateOnly date, string? rateSource = null)
    {
        var plan = CheckOccurrence(occurrence);
        var document = TransferContext();
        if (date == default || terms.Kind != occurrence.Terms.Kind || terms.AccountId != occurrence.Terms.AccountId ||
            terms.DestinationId != occurrence.Terms.DestinationId)
            throw new ArgumentException("Confirmez une date et conservez les comptes de cette occurrence.");
        var accounts = document.Accounts.ToDictionary(account => account.Id);
        terms.Validate(accounts, active: true, posting: true);
        var source = accounts[terms.AccountId];
        var amount = ExactMoney.FromMinorUnits(terms.AmountMinor, source.Currency);
        if (terms.Kind == ScheduleKind.Transfer)
        {
            var destination = accounts[terms.DestinationId!.Value];
            var received = ExactMoney.FromMinorUnits(terms.DestinationMinor ?? terms.AmountMinor, destination.Currency);
            var fee = terms.FeeAccountId is { } feeId ? ExactMoney.FromMinorUnits(terms.FeeMinor, accounts[feeId].Currency) : 0m;
            var transfer = BuildTransfer(new(source.Id, destination.Id, date, amount, received, terms.Label, terms.Notes,
                RateSource: rateSource, FeeAccountId: terms.FeeAccountId, FeeAmount: fee), document, null);
            var metadata = RecordDecision(document, plan, occurrence, date, ScheduleOutcome.Posted, terms,
                transfer: transfer.Value.Id, rateSource: transfer.Value.RateSource);
            StageTransfer(metadata, transfer, 0);
        }
        else
        {
            var catalog = new DraftCatalog(document);
            var transaction = catalog.Build(source, new(date, amount,
                terms.Kind == ScheduleKind.Income ? TransactionDirection.Credit : TransactionDirection.Debit,
                terms.Label, terms.Payee, terms.Category, terms.Subcategory, terms.Method, terms.Notes));
            var metadata = RecordDecision(catalog.Capture(), plan, occurrence, date, ScheduleOutcome.Posted, terms,
                transaction: transaction.Id);
            Stage(metadata, new(transaction, 0), transaction.Id);
        }
        NotifySchedules();
    }

    private SchedulePlan FindSchedule(Guid id) => Schedules.SingleOrDefault(plan => plan.Id == id && !plan.Retired)
        ?? throw new InvalidOperationException("Cette programmation est absente ou supprimée.");

    private SchedulePlan CheckOccurrence(ScheduleOccurrence occurrence)
    {
        var plan = FindSchedule(occurrence.PlanId);
        if (plan.Suspended) throw new InvalidOperationException("Reprenez la programmation avant de traiter ses échéances.");
        if (occurrence.PreviousStep == 0 && occurrence.Number == plan.NextNumber &&
            plan.Next == occurrence.Date && occurrence.OriginalDate == occurrence.Date && occurrence.Terms == plan.Terms)
            return plan;
        var last = ScheduleHistory.Where(item => item.PlanId == plan.Id && item.Number == occurrence.Number)
            .MaxBy(item => item.Step);
        if (last is { Outcome: ScheduleOutcome.Deferred } && last.Step == occurrence.PreviousStep &&
            last.Date == occurrence.Date && last.OriginalDate == occurrence.OriginalDate && last.Terms == occurrence.Terms)
            return plan;
        throw new InvalidOperationException("Cette occurrence a changé, a déjà été traitée ou attend une échéance précédente.");
    }

    private static FinancialDocument ReplacePlan(FinancialDocument document, SchedulePlan plan) =>
        document with { Schedules = document.Schedules.Select(item => item.Id == plan.Id ? plan : item).ToArray() };

    private static FinancialDocument RecordDecision(FinancialDocument document, SchedulePlan plan, ScheduleOccurrence occurrence,
        DateOnly date, ScheduleOutcome outcome, ScheduleTerms terms, Guid? transaction = null, Guid? transfer = null, string? rateSource = null)
    {
        if (!occurrence.IsDeferred)
            document = ReplacePlan(document, plan with { Cursor = checked(plan.Cursor + 1), NextNumber = checked(plan.NextNumber + 1) });
        return document with { ScheduleHistory = [.. document.ScheduleHistory,
            new(plan.Id, occurrence.Number, checked(occurrence.PreviousStep + 1), occurrence.OriginalDate, date,
                outcome, DateTimeOffset.UtcNow, terms, transaction, transfer, rateSource)] };
    }

    private void StageSchedule(FinancialDocument document)
    {
        _document = document;
        MarkDirty();
        NotifySchedules();
    }

    private void NotifySchedules() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Schedules)));

    private bool ScheduleUsesAccount(Guid id) =>
        Schedules.Any(plan => plan.Terms.Uses(id)) || ScheduleHistory.Any(item => item.Terms.Uses(id)) ||
        _savedScheduleState?.Schedules.Any(plan => plan.Terms.Uses(id)) == true;

    private void ProtectScheduledDraft(Guid id, bool transfer)
    {
        if (ScheduleHistory.Any(decision => (transfer ? decision.TransferId : decision.TransactionId) == id &&
            !IsScheduleDecisionSaved(decision)))
            throw new InvalidOperationException("Ce brouillon appartient à l’échéancier. Enregistrez-le avant de le modifier dans le registre.");
    }
}
