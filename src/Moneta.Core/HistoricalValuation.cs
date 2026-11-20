namespace Moneta.Core;

public enum HistoricalUse { Conversion, Flow, BalanceAtDate, Forecast }
public enum HistoricalFlowKind { Income, Expense, TransferDebit, TransferCredit, Fee }
public enum HistoricalRateAccess { CacheOnly, FetchMissing, Refresh }
public enum HistoricalMethod { Identity, Direct, Inverse, Cross }
public enum HistoricalFailure { Future, MissingRate, Provider, InvalidRate, Cache, Overflow }

public sealed record HistoricalFlow(Transaction Transaction, Guid? TransferId = null, LedgerTransferLeg? Role = null)
{
    public HistoricalInput Input()
    {
        if (Transaction.IsDeleted || (TransferId is null) != (Role is null) || TransferId == Guid.Empty)
            throw new ArgumentException("Choisissez une opération active et ses liens enregistrés.");
        var kind = Role switch
        {
            LedgerTransferLeg.Debit => HistoricalFlowKind.TransferDebit,
            LedgerTransferLeg.Credit => HistoricalFlowKind.TransferCredit,
            LedgerTransferLeg.Fee => HistoricalFlowKind.Fee,
            null => Transaction.Direction == TransactionDirection.Debit ? HistoricalFlowKind.Expense : HistoricalFlowKind.Income,
            _ => throw new ArgumentException("Jambe de virement inconnue.")
        };
        return new(HistoricalUse.Flow, Transaction.Date, FinancialRules.SignedAmount(Transaction), Transaction.Currency.Code,
            Transaction.Id, Transaction.Revision, kind, TransferId);
    }
}

public sealed record HistoricalBalance(Guid AccountId, DateOnly Cutoff, Currency Currency, decimal Amount,
    long OperationCount, string InputSha256)
{
    public HistoricalInput Input() => new(HistoricalUse.BalanceAtDate, Cutoff, Amount, Currency.Code,
        SubjectId: AccountId, BalanceSha256: InputSha256, BalanceOperationCount: OperationCount);
}

public sealed record HistoricalInput(HistoricalUse Use, DateOnly Date, decimal NativeAmount, string SourceCode,
    Guid? SubjectId = null, long? Revision = null, HistoricalFlowKind? FlowKind = null, Guid? TransferId = null,
    string? BalanceSha256 = null, long? BalanceOperationCount = null);

// The exact operands and their publication travel with the proof, independently of the renewable cache.
// A null leg represents one unit of the publication's reference currency.
public sealed record HistoricalQuotation(Guid PublicationId, RateProvider Provider, string ReferenceCurrency,
    DateOnly RequestedDate, DateOnly EffectiveDate, DateTimeOffset RetrievedUtc, string Endpoint, string PayloadSha256,
    bool FromCache, OfficialRateQuote? Source, OfficialRateQuote? Target, DateOnly? PublishedDate = null);

public sealed record HistoricalProof(int RulesVersion, HistoricalInput Input, string TargetCode, int TargetDecimals,
    HistoricalMethod Method, MidpointRounding Rounding, HistoricalQuotation? Quotation, decimal Value)
{
    public const int CurrentRules = 1;

    public decimal Reproduce()
    {
        if (RulesVersion != CurrentRules || Rounding != MidpointRounding.ToEven)
            throw new ArgumentException("Version de calcul ou convention d’arrondi inconnue.");
        HistoricalValuation.Validate(Input, new Currency(TargetCode, TargetCode, TargetDecimals));
        if (Input.Use == HistoricalUse.Forecast) throw new ArgumentException("Une projection ne constitue pas une preuve historique.");
        var target = new Currency(TargetCode, TargetCode, TargetDecimals);
        if (Quotation is { } quote)
        {
            HistoricalValuation.ValidateDate(quote.Provider, Input.Date, quote.RequestedDate, quote.EffectiveDate, quote.RetrievedUtc);
            var operands = new[] { quote.Source, quote.Target }.OfType<OfficialRateQuote>().ToArray();
            RateRules.Validate(new(quote.PublicationId, quote.Provider, quote.ReferenceCurrency, quote.RequestedDate,
                quote.EffectiveDate, quote.RetrievedUtc, quote.Endpoint, quote.PayloadSha256, operands));
            if ((quote.Source?.CurrencyCode ?? quote.ReferenceCurrency) != Input.SourceCode ||
                (quote.Target?.CurrencyCode ?? quote.ReferenceCurrency) != TargetCode || quote.PublishedDate is not null)
                throw new ArgumentException("Cotations incompatibles avec cette preuve.");
        }
        if ((Input.SourceCode == TargetCode) != (Quotation is null) || Method != HistoricalValuation.Method(Quotation))
            throw new ArgumentException("Chemin de conversion incohérent.");
        var amount = RateConverter.ConvertQuoted(Input.NativeAmount, target, Quotation?.Source, Quotation?.Target);
        if (amount != Value) throw new ArgumentException("Le résultat enregistré diffère du calcul reproduit.");
        return amount;
    }
}

public sealed record HistoricalResult(HistoricalInput Input, HistoricalProof? Proof, HistoricalFailure? Failure = null,
    string? Detail = null);

public sealed record HistoricalFlowTotals(decimal Income, decimal Expenses, int UnvaluedIncome, int UnvaluedExpenses)
{
    // Aggregates the displayed rounded rows and exposes incomplete totals explicitly.
    public static HistoricalFlowTotals From(IEnumerable<HistoricalResult> rows, Currency target)
    {
        var gains = new List<decimal>();
        var costs = new List<decimal>();
        var seen = new HashSet<Guid>();
        RateProvider? source = null;
        var missingGains = 0;
        var missingCosts = 0;
        foreach (var row in rows)
        {
            if (row.Input.Use != HistoricalUse.Flow || row.Input.SubjectId is not { } id || !seen.Add(id) ||
                seen.Count > HistoricalValuation.MaximumBatchSize)
                throw new ArgumentException("Le total attend des flux enregistrés distincts.");
            if (row.Input.FlowKind is HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit) continue;
            var income = row.Input.FlowKind == HistoricalFlowKind.Income;
            if (row.Proof is { } proof)
            {
                if (proof.TargetCode != target.Code || proof.TargetDecimals != target.DecimalPlaces || proof.Input != row.Input)
                    throw new ArgumentException("Le total attend une devise cible et une preuve par flux cohérentes.");
                if (proof.Quotation is { } quote)
                {
                    if (source is { } existing && existing != quote.Provider)
                        throw new ArgumentException("Les flux agrégés doivent employer la même source officielle.");
                    source = quote.Provider;
                }
                (income ? gains : costs).Add(income ? proof.Value : -proof.Value);
            }
            else if (income) missingGains++;
            else missingCosts++;
        }
        return new(RateConverter.SumRounded(gains, target), RateConverter.SumRounded(costs, target), missingGains, missingCosts);
    }
}
