namespace Moneta.Core;

public sealed record HistoricalRateResolution(OfficialRateSet Rates, bool FromCache, string? CacheNotice = null);

public sealed class HistoricalRateResolver(IHistoricalRateCache cache, IEnumerable<IOfficialRateSource> sources)
{
    private readonly IReadOnlyDictionary<RateProvider, IOfficialRateSource> _sources = sources.ToDictionary(source => source.Provider);

    public async Task<HistoricalRateResolution> ResolveAsync(RateProvider provider, DateOnly date, HistoricalRateAccess access,
        CancellationToken cancellationToken = default)
    {
        _ = RateRules.ReferenceCurrency(provider);
        if (date == default || !Enum.IsDefined(access)) throw new ArgumentException("Demande de cours invalide.");
        cancellationToken.ThrowIfCancellationRequested();
        string? notice = null;
        if (access != HistoricalRateAccess.Refresh)
        {
            try
            {
                var stored = await Task.Run(() => cache.FindForRequestedDate(provider, date, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (stored is not null)
                {
                    Check(stored, provider, date);
                    return new(stored, true);
                }
            }
            catch (RateException error) when (access == HistoricalRateAccess.FetchMissing) { notice = error.Message; }
            if (access == HistoricalRateAccess.CacheOnly)
                throw new RateException(RateFailure.Unavailable, "Aucune réponse vérifiée en cache pour cette source et cette date.");
        }

        if (!_sources.TryGetValue(provider, out var source))
            throw new RateException(RateFailure.Unavailable, "Cette source officielle n’est pas disponible.");
        var download = await source.FetchAsync(date, cancellationToken).ConfigureAwait(false);
        Check(download.Rates, provider, date);
        try { await Task.Run(() => cache.Store(download, cancellationToken), cancellationToken).ConfigureAwait(false); }
        catch (RateException error) { notice = "Cours reçu mais cache non enregistré. " + error.Message; }
        return new(download.Rates, false, notice);
    }

    private static void Check(OfficialRateSet rates, RateProvider provider, DateOnly date)
    {
        RateRules.Validate(rates);
        if (rates.Provider != provider) throw new RateException(RateFailure.InvalidResponse, "La provenance diffère de la source demandée.");
        HistoricalValuation.ValidateDate(provider, date, rates.RequestedDate, rates.EffectiveDate, rates.RetrievedUtc);
    }
}

public sealed class HistoricalValuation(HistoricalRateResolver resolver, TimeProvider? clock = null)
{
    public const int MaximumBatchSize = 50_000;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<HistoricalResult> ValueAsync(HistoricalInput input, Currency target, RateProvider provider,
        HistoricalRateAccess access = HistoricalRateAccess.FetchMissing, CancellationToken cancellationToken = default)
    {
        var result = await ValueBatchAsync([input], target, provider, access, cancellationToken).ConfigureAwait(false);
        return result[0];
    }

    public async Task<IReadOnlyList<HistoricalResult>> ValueBatchAsync(IReadOnlyList<HistoricalInput> inputs, Currency target,
        RateProvider provider, HistoricalRateAccess access = HistoricalRateAccess.FetchMissing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _ = RateRules.ReferenceCurrency(provider);
        if (!Enum.IsDefined(access) || inputs.Count > MaximumBatchSize)
            throw new ArgumentException("Limitez chaque lot à 50 000 valorisations et choisissez un accès aux cours valide.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var result = new HistoricalResult[inputs.Count];
        var dates = new Dictionary<DateOnly, List<(int Index, HistoricalInput Input)>>();
        for (var index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[index];
            Validate(input, target);
            if (input.Use == HistoricalUse.Forecast || input.Date > today)
                result[index] = new(input, null, HistoricalFailure.Future, "Une date future exige une méthode prévisionnelle explicite.");
            else if (input.SourceCode == target.Code) result[index] = Calculate(input, target, null);
            else
            {
                if (!dates.TryGetValue(input.Date, out var group)) dates.Add(input.Date, group = []);
                group.Add((index, input));
            }
        }
        foreach (var (date, rows) in dates)
        {
            try
            {
                var resolved = await resolver.ResolveAsync(provider, date, access, cancellationToken).ConfigureAwait(false);
                var rates = resolved.Rates;
                var quotes = rates.Quotes.ToDictionary(quote => quote.CurrencyCode, StringComparer.Ordinal);
                var paths = new Dictionary<string, HistoricalQuotation?>(StringComparer.Ordinal);
                foreach (var (index, input) in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!paths.TryGetValue(input.SourceCode, out var path))
                    {
                        var from = quotes.GetValueOrDefault(input.SourceCode);
                        var to = quotes.GetValueOrDefault(target.Code);
                        if ((from is not null || input.SourceCode == rates.ReferenceCurrency) && (to is not null || target.Code == rates.ReferenceCurrency))
                            path = new(rates.Id, rates.Provider, rates.ReferenceCurrency, rates.RequestedDate, rates.EffectiveDate,
                                rates.RetrievedUtc, rates.Endpoint, rates.PayloadSha256, resolved.FromCache, from, to);
                        paths.Add(input.SourceCode, path);
                    }
                    result[index] = path is null
                        ? new(input, null, HistoricalFailure.MissingRate, $"Paire {input.SourceCode}/{target.Code} absente de ce lot officiel.")
                        : Calculate(input, target, path, resolved.CacheNotice);
                }
            }
            catch (RateException error)
            {
                var failure = error.Failure switch
                {
                    RateFailure.Unavailable => HistoricalFailure.MissingRate,
                    RateFailure.InvalidResponse => HistoricalFailure.InvalidRate,
                    RateFailure.Cache or RateFailure.UnsupportedVersion => HistoricalFailure.Cache,
                    _ => HistoricalFailure.Provider
                };
                foreach (var (index, input) in rows) result[index] = new(input, null, failure, error.Message);
            }
        }
        return Array.AsReadOnly(result);
    }

    private static HistoricalResult Calculate(HistoricalInput input, Currency target, HistoricalQuotation? quotation, string? notice = null)
    {
        try
        {
            var amount = RateConverter.ConvertQuoted(input.NativeAmount, target, quotation?.Source, quotation?.Target);
            return new(input, new(HistoricalProof.CurrentRules, input, target.Code, target.DecimalPlaces,
                Method(quotation), MidpointRounding.ToEven, quotation, amount), Detail: notice);
        }
        catch (OverflowException error) { return new(input, null, HistoricalFailure.Overflow, error.Message); }
    }

    internal static HistoricalMethod Method(HistoricalQuotation? quote) => quote switch
    {
        null => HistoricalMethod.Identity,
        { Target: null } => HistoricalMethod.Direct,
        { Source: null } => HistoricalMethod.Inverse,
        _ => HistoricalMethod.Cross
    };

    internal static void Validate(HistoricalInput input, Currency target)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(input.Use) || input.Date == default || !RateRules.IsCurrencyCode(input.SourceCode) ||
            !RateRules.IsCurrencyCode(target.Code) || target.DecimalPlaces is < 0 or > 28)
            throw new ArgumentException("Date, devise ou usage de valorisation invalide.");
        if (input.Use == HistoricalUse.Flow && (input.SubjectId is null || input.SubjectId == Guid.Empty ||
            input.Revision is null or <= 0 || input.FlowKind is not { } kind || !Enum.IsDefined(kind) ||
            (kind is HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit or HistoricalFlowKind.Fee) != input.TransferId.HasValue ||
            input.TransferId == Guid.Empty || (kind is HistoricalFlowKind.Income or HistoricalFlowKind.TransferCredit ? input.NativeAmount < 0 : input.NativeAmount > 0)))
            throw new ArgumentException("Identité, nature ou signe du flux invalide.");
        if (input.Use == HistoricalUse.BalanceAtDate && (input.SubjectId is null || input.SubjectId == Guid.Empty ||
            input.BalanceOperationCount is null or < 0 || input.BalanceSha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit)))
            throw new ArgumentException("L’arrêté doit identifier le compte et le périmètre de son calcul.");
    }

    internal static void ValidateDate(RateProvider provider, DateOnly date, DateOnly requested, DateOnly effective, DateTimeOffset retrieved)
    {
        // XML_daily supplies the official quotation in force on date_req, including extended holidays.
        // NBRB's ondate rows must attest the date itself. An empty reply proves no earlier rate.
        if (date != requested || effective > date || (provider == RateProvider.Nbrb && effective != date) ||
            (effective < date && DateOnly.FromDateTime(retrieved.UtcDateTime) < date))
            throw new RateException(RateFailure.InvalidResponse, "La réponse n’atteste pas un cours applicable à la date demandée.");
    }
}
