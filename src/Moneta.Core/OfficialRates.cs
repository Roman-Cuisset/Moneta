namespace Moneta.Core;

public enum RateProvider { Nbrb, Cbr }

// OfficialValue units of ReferenceCurrency for Nominal units of CurrencyCode.
public sealed record OfficialRateQuote(string CurrencyCode, decimal OfficialValue, long Nominal,
    string SourceId = "", string NumericCode = "", string Name = "");

public sealed record OfficialRateSet(Guid Id, RateProvider Provider, string ReferenceCurrency,
    DateOnly RequestedDate, DateOnly EffectiveDate, DateTimeOffset RetrievedUtc,
    string Endpoint, string PayloadSha256, IReadOnlyList<OfficialRateQuote> Quotes);

public sealed record RateDownload(OfficialRateSet Rates, ReadOnlyMemory<byte> Payload);
public sealed record CurrencyAmount(decimal Amount, string CurrencyCode);
public sealed record RateHistoryEntry(Guid Id, RateProvider Provider, DateOnly RequestedDate,
    DateOnly EffectiveDate, DateTimeOffset RetrievedUtc, string PayloadSha256);

public enum RateFailure { Network, Http, Timeout, InvalidResponse, Unavailable, Cache, UnsupportedVersion }

public sealed class RateException : Exception
{
    public RateFailure Failure { get; }
    public RateException(RateFailure failure, string message, Exception? inner = null) : base(message, inner) => Failure = failure;
}

public interface IOfficialRateSource
{
    RateProvider Provider { get; }
    Task<RateDownload> FetchAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public interface IRateCache
{
    OfficialRateSet? Find(RateProvider provider, DateOnly date, int maximumAgeDays = 31,
        CancellationToken cancellationToken = default);
    void Store(RateDownload download, CancellationToken cancellationToken = default);
    IReadOnlyList<RateHistoryEntry> ReadHistory(RateProvider provider, int offset = 0, int count = 100,
        CancellationToken cancellationToken = default);
}

public interface IHistoricalRateCache
{
    OfficialRateSet? FindForRequestedDate(RateProvider provider, DateOnly date, CancellationToken cancellationToken = default);
    void Store(RateDownload download, CancellationToken cancellationToken = default);
}
