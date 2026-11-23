using System.Globalization;

namespace Moneta.Core;

public sealed record BudgetPlan(Guid Id, string Name, DateOnly Month, string CurrencyCode, long PlannedMinor,
    IReadOnlyList<Guid> Accounts, IReadOnlyList<Guid> Categories)
{
    public DateOnly End => Month.AddDays(DateTime.DaysInMonth(Month.Year, Month.Month) - 1);

    public void Validate(FinancialDocument document)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Contains('\0') ||
            Month == default || Month.Day != 1 || PlannedMinor < 0 ||
            !document.Currencies.Any(item => item.Code == CurrencyCode) || Accounts.Count == 0 || Categories.Count == 0 ||
            Accounts.Distinct().Count() != Accounts.Count || Categories.Distinct().Count() != Categories.Count ||
            Accounts.Except(document.Accounts.Select(item => item.Id)).Any() ||
            Categories.Except(document.Categories.Select(item => item.Id)).Any())
            throw new ArgumentException(L.Get("FinancialReports.CheckTheNameMonthCurrencyAccountsAndCategories"));
    }
}

public sealed record ReportCriteria(DateOnly From, DateOnly Through, IReadOnlyList<Guid> Accounts, bool IncludeArchived,
    Guid? CategoryId, Currency Target, RateProvider Provider, DateOnly? ObservedThrough = null)
{
    public const int MaximumMonths = 120;

    public void Validate()
    {
        if (From == default || Through < From || (Through.Year - From.Year) * 12 + Through.Month - From.Month >= MaximumMonths ||
            Accounts.Count == 0 || Accounts.Contains(Guid.Empty) || Accounts.Distinct().Count() != Accounts.Count ||
            CategoryId == Guid.Empty || Target is null || !RateRules.IsCurrencyCode(Target.Code) || Target.DecimalPlaces is < 0 or > 28 ||
            !Enum.IsDefined(Provider) || ObservedThrough == default(DateOnly))
            throw new ArgumentException(L.Get("FinancialReports.ChooseAccountsACurrencyAndAMaximumPeriod"));
    }
}

public sealed record ReportSubject(Guid AccountId, string Account, Guid? CategoryId = null, string? Category = null,
    Guid? SubcategoryId = null, string? Subcategory = null);

public sealed record ReportInput(ReportSubject Subject, HistoricalInput Input);

public sealed record ReportSource(IReadOnlyList<ReportInput> Flows, IReadOnlyList<ReportInput> Balances);

public sealed record ReportLine(ReportSubject Subject, HistoricalInput Input, decimal? Converted,
    int? Quotation, HistoricalFailure? Failure, string? Detail);

public enum ReportGrouping { Category, Subcategory, Account, NativeCurrency, Month }

public sealed record ReportTotal(decimal Income, decimal Expense, int Eligible, int Missing)
{
    public decimal Net => RateConverter.Difference(Income, Expense);
    public bool Complete => Missing == 0;
    public int Valued => Eligible - Missing;
}

public sealed record ReportGroup(string Key, string Label, ReportTotal Total);

public sealed record BudgetUsage(BudgetPlan Plan, decimal Planned, decimal Known, int Missing, bool HasPastDates)
{
    public decimal? Difference => Missing == 0 && HasPastDates ? RateConverter.Difference(Planned, Known) : null;
    public bool Overrun => Known > Planned;
    public decimal? Percent
    {
        get
        {
            if (Planned == 0 || !HasPastDates) return null;
            try { return RateConverter.Percentage(Known, Planned); }
            catch (OverflowException) { return null; }
        }
    }
}

public sealed record ReportContent(int Version, ReportCriteria Criteria, IReadOnlyList<HistoricalQuotation> Quotations,
    IReadOnlyList<ReportLine> Flows, IReadOnlyList<ReportLine> Balances)
{
    public const int CurrentVersion = 1;
    public const int MaximumBalanceAccounts = 1000;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public HistoricalQuotation? QuoteFor(ReportLine value) => value.Quotation is { } index ? Quotations[index] : null;

    public ReportTotal Totals() => Sum(Flows, Criteria.Target);

    public decimal? ConsolidatedBalance() => Balances.Count > 0 && Balances.All(row => row.Converted.HasValue)
        ? RateConverter.SumRounded(Balances.Select(row => row.Converted!.Value), Criteria.Target) : null;

    public IReadOnlyList<ReportGroup> Group(ReportGrouping mode)
    {
        var values = Flows.GroupBy(row => GroupKey(row, mode))
            .ToDictionary(group => group.Key.Key, group => new ReportGroup(group.Key.Key, group.Key.Label, Sum(group, Criteria.Target)));
        if (mode == ReportGrouping.Month)
        {
            var date = new DateOnly(Criteria.From.Year, Criteria.From.Month, 1);
            while (date <= Criteria.Through)
            {
                var key = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                values.TryAdd(key, new ReportGroup(key, date.ToString("MMM yyyy", French), new(0m, 0m, 0, 0)));
                if (date.Year == 9999 && date.Month == 12) break;
                date = date.AddMonths(1);
            }
        }
        return values.Values.OrderBy(item => mode == ReportGrouping.Month ? item.Key : item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
    }

    public BudgetUsage Budget(BudgetPlan plan, DateOnly asOf)
    {
        if (plan.Month != Criteria.From || Criteria.Through != plan.End || Criteria.Target.Code != plan.CurrencyCode ||
            !plan.Accounts.Order().SequenceEqual(Criteria.Accounts.Order()) || Criteria.CategoryId is not null)
            throw new ArgumentException(L.Get("FinancialReports.TheScopeOfTheReportShouldCoverExactly"));
        var categories = plan.Categories.ToHashSet();
        var costs = Flows.Where(row => row.Input.Date <= asOf && row.Input.FlowKind != HistoricalFlowKind.Income &&
            ((row.Subject.CategoryId is { } root && categories.Contains(root)) ||
             (row.Subject.SubcategoryId is { } leaf && categories.Contains(leaf))));
        var amounts = Sum(costs, Criteria.Target);
        return new(plan, ExactMoney.FromMinorUnits(plan.PlannedMinor, Criteria.Target), amounts.Expense, amounts.Missing, plan.Month <= asOf);
    }

    public void Verify()
    {
        Criteria.Validate();
        if (Version != CurrentVersion || Flows.Count > HistoricalValuation.MaximumBatchSize || Balances.Count > MaximumBalanceAccounts ||
            Quotations.Count > Flows.Count + Balances.Count)
            throw new ArgumentException(L.Get("FinancialReports.VersionOrDimensionsOfTheFixedInvalidRatio"));
        var accounts = Criteria.Accounts.ToHashSet();
        var ids = new HashSet<Guid>();
        var balanceIds = new HashSet<Guid>();
        var used = new HashSet<int>();
        foreach (var line in Flows.Concat(Balances))
        {
            var input = line.Input;
            HistoricalValuation.Validate(input, Criteria.Target);
            var flow = input.Use == HistoricalUse.Flow;
            if (!accounts.Contains(line.Subject.AccountId) || string.IsNullOrWhiteSpace(line.Subject.Account) ||
                input.SubjectId is not { } id || (flow ? !ids.Add(id) || input.Date < Criteria.From || input.Date > Criteria.Through ||
                    input.Date > Criteria.ObservedThrough ||
                    input.FlowKind is HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit ||
                    (Criteria.CategoryId is { } filter && line.Subject.CategoryId != filter && line.Subject.SubcategoryId != filter)
                    : input.Use != HistoricalUse.BalanceAtDate || id != line.Subject.AccountId || !balanceIds.Add(id) || input.Date != Criteria.Through))
                throw new ArgumentException(L.Get("FinancialReports.ALineInTheReportDoesNotMatch"));
            if (line.Converted is { } value)
            {
                if (line.Failure is not null || line.Quotation is { } index && (index < 0 || index >= Quotations.Count))
                    throw new ArgumentException(L.Get("FinancialReports.InvalidRatingResultOrReference"));
                var quote = QuoteFor(line);
                if (quote is not null && quote.Provider != Criteria.Provider) throw new ArgumentException(L.Get("FinancialReports.IncompatibleSupplier"));
                if (line.Quotation is { } key) used.Add(key);
                var proof = new HistoricalProof(HistoricalProof.CurrentRules, input, Criteria.Target.Code, Criteria.Target.DecimalPlaces,
                    HistoricalValuation.Method(quote), MidpointRounding.ToEven, quote, value);
                _ = proof.Reproduce();
            }
            else if (line.Quotation is not null || line.Failure is not { } failure || !Enum.IsDefined(failure) || string.IsNullOrWhiteSpace(line.Detail))
                throw new ArgumentException(L.Get("FinancialReports.ALineWithoutAnAmountMustRetainThe"));
        }
        if (Flows.Any(value => value.Input.Use != HistoricalUse.Flow) || Balances.Any(value => value.Input.Use != HistoricalUse.BalanceAtDate) ||
            used.Count != Quotations.Count || (Balances.Count != 0 && !balanceIds.SetEquals(accounts)))
            throw new ArgumentException(L.Get("FinancialReports.TheReportContainsDataOutsideItsScope"));
        _ = Totals().Net;
        _ = ConsolidatedBalance();
    }

    public IReadOnlyList<ReportGroup> ExpenseDistribution(int maxCategories = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCategories, 2);
        var categories = Group(ReportGrouping.Category).Where(row => row.Total.Expense > 0)
            .OrderByDescending(row => row.Total.Expense).ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        if (categories.Length <= maxCategories) return categories;
        var top = categories.Take(maxCategories - 1);
        var remainder = RateConverter.SumRounded(categories.Skip(maxCategories - 1).Select(row => row.Total.Expense), Criteria.Target);
        return top.Append(new ReportGroup("others", "Autres catégories", new(0m, remainder, 0, 0))).ToArray();
    }

    private static ReportTotal Sum(IEnumerable<ReportLine> rows, Currency target)
    {
        var income = new List<decimal>();
        var expenses = new List<decimal>();
        var eligible = 0;
        var missing = 0;
        foreach (var row in rows)
        {
            eligible++;
            if (row.Converted is not { } value) missing++;
            else if (row.Input.FlowKind == HistoricalFlowKind.Income) income.Add(value);
            else expenses.Add(-value);
        }
        return new(RateConverter.SumRounded(income, target), RateConverter.SumRounded(expenses, target), eligible, missing);
    }

    private static (string Key, string Label) GroupKey(ReportLine row, ReportGrouping mode)
    {
        var subject = row.Subject;
        return mode switch
        {
            ReportGrouping.Month => (row.Input.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), row.Input.Date.ToString("MMM yyyy", French)),
            ReportGrouping.Account => (subject.AccountId.ToString("D"), subject.Account),
            ReportGrouping.NativeCurrency => (row.Input.SourceCode, row.Input.SourceCode),
            ReportGrouping.Subcategory when subject.SubcategoryId is { } leaf => (leaf.ToString("D"), $"{subject.Category} › {subject.Subcategory}"),
            _ when subject.CategoryId is { } root => (root.ToString("D"), subject.Category ?? "Catégorie indisponible"),
            _ => ("uncategorized", "Sans catégorie")
        };
    }
}

public sealed class FinancialReportEngine(HistoricalValuation valuation)
{
    public async Task<ReportContent> GenerateAsync(ReportCriteria criteria, ReportSource source, HistoricalRateAccess access,
        CancellationToken token = default)
    {
        criteria.Validate();
        var quotes = new List<HistoricalQuotation>();
        var indexes = new Dictionary<HistoricalQuotation, int>();
        if (source.Flows.Count > HistoricalValuation.MaximumBatchSize || source.Balances.Count > ReportContent.MaximumBalanceAccounts)
            throw new ArgumentException(L.Get("FinancialReports.TheReportExceeds50000FlowsOr1"));
        ReportLine[] Build(IReadOnlyList<ReportInput> rows, IReadOnlyList<HistoricalResult> values) => rows.Select((row, index) =>
        {
            var value = values[index];
            int? quotation = null;
            if (value.Proof?.Quotation is { } rate)
            {
                if (!indexes.TryGetValue(rate, out var position))
                {
                    position = quotes.Count;
                    quotes.Add(rate);
                    indexes.Add(rate, position);
                }
                quotation = position;
            }
            return new ReportLine(row.Subject, row.Input, value.Proof?.Value, quotation, value.Failure, value.Detail);
        }).ToArray();

        // Include the balances in the same valuation batch when possible so same-day rows share the same answer.
        var flows = source.Flows.Where(row => row.Input.FlowKind is not (HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit)).ToArray();
        var all = flows.Concat(source.Balances).ToArray();
        var values = await valuation.ValueBatchAsync(all.Select(row => row.Input).ToArray(), criteria.Target, criteria.Provider, access,
            HistoricalValuation.MaximumBatchSize + ReportContent.MaximumBalanceAccounts, token).ConfigureAwait(false);
        var lines = Build(all, values);
        return new(ReportContent.CurrentVersion, criteria, quotes.AsReadOnly(), Array.AsReadOnly(lines[..flows.Length]),
            Array.AsReadOnly(lines[flows.Length..]));
    }
}
