namespace Moneta.Core;

public enum ScheduleKind { Expense, Income, Transfer }
public enum ScheduleUnit { Once, Day, Week, Month, Year }
public enum ScheduleOutcome { Deferred, Skipped, Posted }

public sealed record ScheduleRule(ScheduleUnit Unit, int Interval = 1, bool EndOfMonth = false)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Unit) || Interval is < 1 or > 366 ||
            (Unit == ScheduleUnit.Once && Interval != 1) ||
            (EndOfMonth && Unit is not (ScheduleUnit.Month or ScheduleUnit.Year)))
            throw new ArgumentException(L.Get("Scheduling.InvalidFrequencyOrInterval"));
    }

    public DateOnly? At(DateOnly anchor, int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        var step = (long)Interval * index;
        if (Unit == ScheduleUnit.Once) return index == 0 ? anchor : null;
        if (Unit is ScheduleUnit.Day or ScheduleUnit.Week)
        {
            var day = anchor.DayNumber + step * (Unit == ScheduleUnit.Week ? 7 : 1);
            return day <= DateOnly.MaxValue.DayNumber ? DateOnly.FromDayNumber((int)day) : null;
        }
        var month = (anchor.Year - 1) * 12 + anchor.Month - 1 + step * (Unit == ScheduleUnit.Year ? 12 : 1);
        if (month >= 9999 * 12) return null;
        var year = (int)(month / 12 + 1);
        var monthOfYear = (int)(month % 12 + 1);
        var last = DateTime.DaysInMonth(year, monthOfYear);
        return new DateOnly(year, monthOfYear, EndOfMonth ? last : Math.Min(anchor.Day, last));
    }

    // First recurrence index strictly after a date, including the exhausted sentinel.
    public int After(DateOnly anchor, DateOnly date)
    {
        var low = 0;
        var high = DateOnly.MaxValue.DayNumber + 1;
        while (low < high)
        {
            var index = (low + high) / 2;
            if (At(anchor, index) is { } current && current <= date) low = index + 1;
            else high = index;
        }
        return low;
    }
}

public sealed record ScheduleTerms(ScheduleKind Kind, Guid AccountId, long AmountMinor, string Label,
    string Notes = "", string? Payee = null, string? Category = null, string? Subcategory = null, string? Method = null,
    Guid? DestinationId = null, long? DestinationMinor = null, Guid? FeeAccountId = null, long FeeMinor = 0)
{
    public bool Uses(Guid accountId) => AccountId == accountId || DestinationId == accountId || FeeAccountId == accountId;

    public void Validate(IReadOnlyDictionary<Guid, Account> accounts, bool active = false, bool posting = false)
    {
        Account Find(Guid id)
        {
            if (!accounts.TryGetValue(id, out var account)) throw new ArgumentException(L.Get("Scheduling.ADueDateAccountIsNotAvailable"));
            if (active && account.IsArchived)
                throw new InvalidOperationException(L.Format("ScheduleWindow.TheAccount0IsArchivedReactivateItBefore", (account.Name)));
            return account;
        }
        var source = Find(AccountId);
        if (!Enum.IsDefined(Kind) || AmountMinor < 0 || FeeMinor < 0)
            throw new ArgumentException(L.Get("Scheduling.InvalidScheduledTransactionTypeOrAmount"));
        Text(Label, Kind == ScheduleKind.Transfer ? 120 : 240, required: true);
        Text(Notes, 2000, required: true, allowEmpty: true);
        foreach (var value in new[] { Payee, Category, Subcategory, Method }) Text(value, 120);
        if (Subcategory is not null && Category is null) throw new ArgumentException(L.Get("Scheduling.ChooseTheCategoryInTheSubcategory"));
        if (Kind != ScheduleKind.Transfer)
        {
            if (DestinationId is not null || DestinationMinor is not null || FeeAccountId is not null || FeeMinor != 0)
                throw new ArgumentException(L.Get("Scheduling.ARecipeOrExpenseUsesASingleAccount"));
            return;
        }

        if (Payee is not null || Category is not null || Subcategory is not null || Method is not null ||
            DestinationId is not { } destinationId || destinationId == AccountId || AmountMinor == 0 ||
            DestinationMinor <= 0 || ((FeeAccountId is null) != (FeeMinor == 0)))
            throw new ArgumentException(L.Get("Scheduling.InformTwoSeparateAccountsAndPositiveTransferAmounts"));
        var destination = Find(destinationId);
        if (source.Currency.Code == destination.Currency.Code)
        {
            if (DestinationMinor is { } received && received != AmountMinor)
                throw new ArgumentException(L.Get("Scheduling.ATransferIntoASingleCurrencyRequiresTwo"));
        }
        else if (posting && DestinationMinor is null)
            throw new ArgumentException(L.Get("Scheduling.EnterTheActualAmountReceivedInTheCurrency"));
        if (FeeAccountId is { } feeId) _ = Find(feeId);
    }

    internal static void Text(string? value, int max, bool required = false, bool allowEmpty = false)
    {
        if ((required && value is null) || value is not null &&
            (value.Length > max || value.Contains('\0') || (!allowEmpty && string.IsNullOrWhiteSpace(value))))
            throw new ArgumentException(L.Get("Scheduling.AScheduledTransactionFieldIsEmptyOrLonger"));
    }
}

public sealed record SchedulePlan(Guid Id, ScheduleTerms Terms, ScheduleRule Rule, DateOnly Anchor,
    DateOnly? End = null, int Cursor = 0, long NextNumber = 1, bool Suspended = false, bool Retired = false)
{
    public DateOnly? At(int index) => Rule.At(Anchor, index) is { } date && (End is null || date <= End) ? date : null;
    [System.Text.Json.Serialization.JsonIgnore]
    public DateOnly? Next => At(Cursor);

    public void Validate(IReadOnlyDictionary<Guid, Account> accounts)
    {
        if (Id == Guid.Empty || Anchor == default || End < Anchor || Cursor is < 0 or > 3652059 || NextNumber < 1)
            throw new ArgumentException(L.Get("Scheduling.IdentityPeriodOrInvalidProgrammingPosition"));
        Rule.Validate();
        Terms.Validate(accounts);
    }
}

public sealed record ScheduleDecision(Guid PlanId, long Number, int Step, DateOnly OriginalDate, DateOnly Date,
    ScheduleOutcome Outcome, DateTimeOffset RecordedUtc, ScheduleTerms Terms,
    Guid? TransactionId = null, Guid? TransferId = null, string? RateSource = null);

public sealed record ScheduleOccurrence(Guid PlanId, long Number, DateOnly OriginalDate, DateOnly Date,
    ScheduleTerms Terms, int PreviousStep, bool Actionable)
{
    public bool IsDeferred => PreviousStep > 0;
}

public sealed class ScheduleCalendar
{
    private readonly IReadOnlyList<SchedulePlan> plans;
    private readonly Dictionary<Guid, SchedulePlan> byId;
    private readonly ScheduleDecision[] deferred;

    public ScheduleCalendar(IReadOnlyList<SchedulePlan> plans, IReadOnlyList<ScheduleDecision> decisions)
    {
        this.plans = plans;
        byId = plans.ToDictionary(plan => plan.Id);
        deferred = decisions.GroupBy(item => (item.PlanId, item.Number))
            .Select(items => items.MaxBy(item => item.Step)!)
            .Where(item => item.Outcome == ScheduleOutcome.Deferred).ToArray();
    }

    public long Count(DateOnly from, DateOnly through)
    {
        if (from > through) return 0;
        long total = deferred.LongCount(item => IsActive(byId[item.PlanId]) && item.Date >= from && item.Date <= through);
        foreach (var plan in plans.Where(IsActive))
        {
            var index = First(plan, from);
            var end = plan.End is { } limit && limit < through ? limit : through;
            total += Math.Max(0, plan.Rule.After(plan.Anchor, end) - index);
        }
        return total;
    }

    public IEnumerable<ScheduleOccurrence> Between(DateOnly from, DateOnly through)
    {
        if (from > through) yield break;
        // Merge one cursor per series and the reported dates instead of materializing all dates.
        var queue = new PriorityQueue<(ScheduleOccurrence Occurrence, int Index), (DateOnly, Guid, long)>();
        void Add(ScheduleOccurrence item, int index) => queue.Enqueue((item, index), (item.Date, item.PlanId, item.Number));
        void AddRegular(SchedulePlan plan, int index)
        {
            if (plan.At(index) is not { } date || date > through) return;
            var number = checked(plan.NextNumber + index - plan.Cursor);
            Add(new(plan.Id, number, date, date, plan.Terms, 0, index == plan.Cursor), index);
        }
        foreach (var plan in plans.Where(IsActive)) AddRegular(plan, First(plan, from));
        foreach (var item in deferred)
            if (IsActive(byId[item.PlanId]) && item.Date >= from && item.Date <= through)
                Add(new(item.PlanId, item.Number, item.OriginalDate, item.Date, item.Terms, item.Step, true), -1);
        while (queue.TryDequeue(out var item, out _))
        {
            yield return item.Occurrence;
            if (item.Index >= 0) AddRegular(byId[item.Occurrence.PlanId], item.Index + 1);
        }
    }

    private static int First(SchedulePlan plan, DateOnly from) => from == DateOnly.MinValue
        ? plan.Cursor : Math.Max(plan.Cursor, plan.Rule.After(plan.Anchor, from.AddDays(-1)));
    private static bool IsActive(SchedulePlan plan) => !plan.Suspended && !plan.Retired;
}
