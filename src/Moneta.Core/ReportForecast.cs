namespace Moneta.Core;

public enum ForecastValuation { Native, Estimated, Missing, Transfer, Draft }

public sealed record ForecastRow(DateOnly Date, string Label, Guid AccountId, string Account, decimal Native, string Currency,
    bool IsIncome, bool IsFee, ForecastValuation Status, decimal? Value = null, DateOnly? Effective = null,
    RateProvider? Provider = null, string? Detail = null, decimal? TransferCredit = null, string? TransferCurrency = null);

public sealed record ForecastMonth(DateOnly Month, Currency Currency, decimal Income, decimal Expense, int Missing, bool IncludesEstimates);

public sealed class ReportForecast(HistoricalValuation valuation)
{
    public const int MaximumRows = 50_000;

    public async Task<IReadOnlyList<ForecastRow>> GenerateAsync(FinancialDocument saved, ReportCriteria criteria, DateOnly today,
        bool estimate, HistoricalRateAccess access, CancellationToken token = default, IReadOnlyList<ScheduleDecision>? draftPosts = null)
    {
        criteria.Validate();
        if (today == DateOnly.MaxValue) return [];
        var tomorrow = today.AddDays(1);
        var endOfHorizon = DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, today.DayNumber + 365));
        var start = criteria.From > tomorrow ? criteria.From : tomorrow;
        var end = criteria.Through < endOfHorizon ? criteria.Through : endOfHorizon;
        var selection = criteria.Accounts.ToHashSet();
        var accounts = saved.Accounts.ToDictionary(account => account.Id);
        var draftByOccurrence = (draftPosts ?? []).Where(item => item.Outcome == ScheduleOutcome.Posted)
            .ToDictionary(item => (item.PlanId, item.Number));
        var pending = saved.ScheduleHistory.Where(item => item.Outcome == ScheduleOutcome.Deferred).Select(item => item.PlanId).ToHashSet();
        var relevant = saved.Schedules.Where(plan => InScope(plan.Terms) || pending.Contains(plan.Id)).ToArray();
        var planIds = relevant.Select(plan => plan.Id).ToHashSet();
        var decisions = saved.ScheduleHistory.Where(item => planIds.Contains(item.PlanId)).ToArray();
        var calendar = new ScheduleCalendar(relevant, decisions);
        if (calendar.Count(start, end) > MaximumRows)
            throw new ArgumentException("Plus de 50 000 échéances dans ce périmètre. Réduisez la période de prévision.");
        var result = new List<ForecastRow>();
        foreach (var item in calendar.Between(start, end))
        {
            token.ThrowIfCancellationRequested();
            var terms = item.Terms;
            if (!InScope(terms)) continue;
            if (draftByOccurrence.TryGetValue((item.PlanId, item.Number), out var draft))
            {
                var account = accounts[terms.AccountId];
                var native = ExactMoney.FromMinorUnits(draft.Terms.AmountMinor, account.Currency);
                result.Add(new(item.Date, draft.Terms.Label, account.Id, account.Name, native, account.Currency.Code,
                    terms.Kind == ScheduleKind.Income, false, ForecastValuation.Draft, Detail: "Écriture en brouillon · en attente de Ctrl+S."));
                continue;
            }
            if (terms.Kind == ScheduleKind.Transfer)
            {
                var debit = selection.Contains(terms.AccountId);
                var hasDestination = terms.DestinationId is { } destination && selection.Contains(destination);
                if (debit || hasDestination)
                {
                    var source = accounts[terms.AccountId];
                    var target = accounts[terms.DestinationId!.Value];
                    var native = ExactMoney.FromMinorUnits(terms.AmountMinor, source.Currency);
                    var credit = terms.DestinationMinor is { } received ? ExactMoney.FromMinorUnits(received, target.Currency)
                        : source.Currency.Code == target.Currency.Code ? native : (decimal?)null;
                    result.Add(new(item.Date, terms.Label, source.Id, $"{source.Name} → {target.Name}", native, source.Currency.Code,
                        false, false, ForecastValuation.Transfer, Detail: "Virement interne, hors prévision des recettes et dépenses.",
                        TransferCredit: credit, TransferCurrency: target.Currency.Code));
                }
                if (terms.FeeAccountId is { } fee && selection.Contains(fee))
                    Add(item.Date, terms.Label + " · frais", fee, terms.FeeMinor, false, true);
            }
            else if (selection.Contains(terms.AccountId)) Add(item.Date, terms.Label, terms.AccountId, terms.AmountMinor,
                terms.Kind == ScheduleKind.Income, false);
            if (result.Count > MaximumRows)
                throw new ArgumentException("Plus de 50 000 lignes de prévision. Réduisez la période.");
        }

        if (!estimate) return result.AsReadOnly();
        var foreign = result.Select((row, index) => (row, index)).Where(item => item.row.Status == ForecastValuation.Missing).ToArray();
        var estimated = await valuation.ValueBatchAsync(foreign.Select(item => new HistoricalInput(HistoricalUse.Conversion,
            today, item.row.Native, item.row.Currency)).ToArray(), criteria.Target, criteria.Provider, access, token).ConfigureAwait(false);
        for (var index = 0; index < foreign.Length; index++)
        {
            var (row, position) = foreign[index];
            var value = estimated[index];
            result[position] = value.Proof is { Quotation: { } quote } proof
                ? row with { Status = ForecastValuation.Estimated, Value = proof.Value, Effective = quote.EffectiveDate, Provider = quote.Provider, Detail = null }
                : row with { Detail = value.Detail };
        }
        return result.AsReadOnly();

        bool InScope(ScheduleTerms terms) => (selection.Contains(terms.AccountId) ||
            terms.DestinationId is { } destination && selection.Contains(destination) ||
            terms.FeeAccountId is { } fee && selection.Contains(fee)) && !accounts[terms.AccountId].IsArchived &&
            !(terms.DestinationId is { } to && accounts[to].IsArchived) && !(terms.FeeAccountId is { } feeId && accounts[feeId].IsArchived);

        void Add(DateOnly date, string label, Guid id, long minor, bool income, bool fee)
        {
            var account = accounts[id];
            var native = ExactMoney.FromMinorUnits(minor, account.Currency);
            var sameCurrency = account.Currency.Code == criteria.Target.Code;
            result.Add(new(date, label, id, account.Name, native, account.Currency.Code, income, fee,
                sameCurrency ? ForecastValuation.Native : ForecastValuation.Missing, sameCurrency ? native : null,
                Detail: sameCurrency ? null : "Estimation multidevise non activée."));
        }
    }

    public static IReadOnlyList<ForecastMonth> Monthly(IReadOnlyList<ForecastRow> rows, IReadOnlyList<Currency> currencies,
        Currency target, bool estimates)
    {
        var byCode = currencies.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var result = new List<ForecastMonth>();
        foreach (var month in rows.Where(row => row.Status is not (ForecastValuation.Transfer or ForecastValuation.Draft))
            .GroupBy(row => new DateOnly(row.Date.Year, row.Date.Month, 1)).OrderBy(group => group.Key))
        {
            foreach (var native in month.GroupBy(row => row.Currency).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var currency = byCode[native.Key];
                result.Add(new(month.Key, currency,
                    RateConverter.SumRounded(native.Where(row => row.IsIncome).Select(row => row.Native), currency),
                    RateConverter.SumRounded(native.Where(row => !row.IsIncome).Select(row => row.Native), currency), 0, false));
            }
            if (estimates && month.Any(row => row.Currency != target.Code))
                result.Add(new(month.Key, target,
                    RateConverter.SumRounded(month.Where(row => row.IsIncome && row.Value.HasValue).Select(row => row.Value!.Value), target),
                    RateConverter.SumRounded(month.Where(row => !row.IsIncome && row.Value.HasValue).Select(row => row.Value!.Value), target),
                    month.Count(row => !row.Value.HasValue), true));
        }
        return result.AsReadOnly();
    }
}
