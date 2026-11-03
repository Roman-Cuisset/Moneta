using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;

namespace Moneta.Tests;

[TestClass]
public sealed class ExchangeRateSessionTests
{
    private static readonly DateOnly Day = new(2026, 4, 3);
    private static readonly DateTimeOffset Now = new(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

    internal static RateDownload Sample(RateProvider provider = RateProvider.Nbrb, DateOnly? requested = null,
        DateOnly? effective = null, DateTimeOffset? retrieved = null)
    {
        var query = requested ?? Day; var date = effective ?? query;
        var endpoint = provider == RateProvider.Nbrb
            ? $"https://api.nbrb.by/exrates/rates?ondate={query:yyyy-MM-dd}&periodicity=0"
            : $"https://www.cbr.ru/scripts/XML_daily.asp?date_req={query:dd/MM/yyyy}";
        var body = provider == RateProvider.Nbrb
            ? $$"""[{"Cur_ID":1,"Date":"{{date:yyyy-MM-dd}}T00:00:00","Cur_Abbreviation":"EUR","Cur_Scale":1,"Cur_OfficialRate":3},{"Cur_ID":2,"Date":"{{date:yyyy-MM-dd}}T00:00:00","Cur_Abbreviation":"RUB","Cur_Scale":100,"Cur_OfficialRate":5}]"""
            : $$"""<?xml version="1.0" encoding="utf-8"?><ValCurs Date="{{date:dd.MM.yyyy}}"><Valute ID="R1"><NumCode>978</NumCode><CharCode>EUR</CharCode><Nominal>1</Nominal><Name>Euro</Name><Value>90,0000</Value></Valute><Valute ID="R2"><NumCode>933</NumCode><CharCode>BYN</CharCode><Nominal>1</Nominal><Name>BYN</Name><Value>30,0000</Value></Valute></ValCurs>""";
        return OfficialRateParsers.Parse(provider, query, retrieved ?? Now, endpoint, Encoding.UTF8.GetBytes(body));
    }

    [TestMethod]
    public async Task DisabledStartupUsesPersistentCacheWithoutCallingEitherProvider()
    {
        var path = Path.Combine(Path.GetTempPath(), "Moneta-session-" + Guid.NewGuid(), "rates.sqlite");
        try
        {
            new SqliteRateCache(path).Store(Sample());
            var nbrb = new Source(RateProvider.Nbrb); var cbr = new Source(RateProvider.Cbr);
            using var session = new ExchangeRateSession(new SqliteRateCache(path), [nbrb, cbr], new Clock());
            await session.InitializeAsync(RateProvider.Nbrb, false);
            Assert.AreEqual(0, nbrb.Dates.Count + cbr.Dates.Count);
            Assert.AreEqual(333.33m, RateConverter.ConvertAmount(1000m, "BYN", new("EUR", "Euro", 2), session.State.Rates!));
            Assert.IsTrue(session.State.FromCache);
            Assert.IsFalse(session.State.IsRefreshing);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [TestMethod]
    public async Task FreshAutomaticCacheAvoidsDownloadButManualRefreshIsExplicit()
    {
        var cache = new Cache(Sample().Rates);
        var source = new Source(RateProvider.Nbrb);
        using var session = new ExchangeRateSession(cache, [source], new Clock());
        await session.InitializeAsync(RateProvider.Nbrb, true);
        Assert.AreEqual(0, source.Dates.Count);
        await session.RefreshAsync();
        Assert.AreEqual(1, source.Dates.Count);
        Assert.IsFalse(session.State.FromCache);
        Assert.AreEqual(1, cache.Stored.Count);
    }

    [DataTestMethod]
    [DataRow(RateFailure.Network)]
    [DataRow(RateFailure.Http)]
    [DataRow(RateFailure.Timeout)]
    [DataRow(RateFailure.InvalidResponse)]
    public async Task FailedRefreshKeepsApplicableOldCacheAndNeverFallsBackToOtherBank(RateFailure failure)
    {
        var old = Sample(requested: Day.AddDays(-2), retrieved: Now.AddDays(-2));
        var nbrb = new Source(RateProvider.Nbrb) { Failure = failure };
        var cbr = new Source(RateProvider.Cbr);
        using var session = new ExchangeRateSession(new Cache(old.Rates), [nbrb, cbr], new Clock());
        await session.InitializeAsync(RateProvider.Nbrb, true);
        Assert.AreEqual(1, nbrb.Dates.Count);
        Assert.AreEqual(0, cbr.Dates.Count);
        Assert.AreEqual(old.Rates, session.State.Rates);
        Assert.IsTrue(session.State.FromCache);
        Assert.IsFalse(session.State.IsRefreshing);
    }

    [TestMethod]
    public async Task MissingPublicationsBacktrackOnlyEightDatesAndKeepActualEffectDate()
    {
        var source = new Source(RateProvider.Nbrb) { MissingBefore = 3 };
        using var session = new ExchangeRateSession(new Cache(), [source], new Clock());
        await session.InitializeAsync(RateProvider.Nbrb, true);
        Assert.AreEqual(Day.AddDays(-2), session.State.Rates!.EffectiveDate);
        CollectionAssert.AreEqual(new[] { Day, Day.AddDays(-1), Day.AddDays(-2) }, source.Dates.ToArray());
        source.MissingBefore = int.MaxValue;
        source.Dates.Clear();
        await session.RefreshAsync();
        Assert.AreEqual(8, source.Dates.Count);
        Assert.AreEqual(Day.AddDays(-2), session.State.Rates!.EffectiveDate);
    }

    [TestMethod]
    public async Task AdvancePublicationIsCachedWithoutBeingUsedForEarlierValuation()
    {
        var future = Sample(effective: Day.AddDays(1));
        var cache = new Cache();
        var source = new Source(RateProvider.Nbrb) { Reply = future };
        using var session = new ExchangeRateSession(cache, [source], new Clock());
        await session.InitializeAsync(RateProvider.Nbrb, true);
        Assert.IsNull(session.State.Rates);
        Assert.AreEqual(future, cache.Stored.Single());
        await session.SelectAsync(RateProvider.Nbrb, Day.AddDays(1));
        Assert.AreEqual(future.Rates, session.State.Rates);
    }

    [TestMethod]
    public async Task ProviderChangeCancelsOldRequestAndLateCompletionCannotReplaceNewSelection()
    {
        var delayed = new DelayedSource();
        var cbr = Sample(RateProvider.Cbr);
        using var session = new ExchangeRateSession(new Cache(cbr.Rates), [delayed, new Source(RateProvider.Cbr)], new Clock());
        var old = session.InitializeAsync(RateProvider.Nbrb, true);
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SelectAsync(RateProvider.Cbr, Day);
        delayed.Completion.SetResult(Sample());
        await old;
        Assert.AreEqual(RateProvider.Cbr, session.State.Provider);
        Assert.AreEqual(cbr.Rates, session.State.Rates);
        Assert.IsFalse(session.State.IsRefreshing);
    }

    [TestMethod]
    public async Task CancellationAndGlobalDeadlineLeaveNoBusySession()
    {
        var delayed = new DelayedSource { HonorCancellation = true };
        using var session = new ExchangeRateSession(new Cache(Sample().Rates), [delayed], new Clock(), TimeSpan.FromMilliseconds(50));
        await session.InitializeAsync(RateProvider.Nbrb, false);
        await session.RefreshAsync();
        Assert.IsFalse(session.State.IsRefreshing);
        Assert.IsNotNull(session.State.Rates);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await session.RefreshAsync(cancelled.Token);
        Assert.IsFalse(session.State.IsRefreshing);
        Assert.IsTrue(session.State.FromCache);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Source(RateProvider provider) : IOfficialRateSource
    {
        public RateProvider Provider => provider;
        public List<DateOnly> Dates { get; } = [];
        public RateFailure? Failure { get; set; }
        public int MissingBefore { get; set; }
        public RateDownload? Reply { get; set; }
        public Task<RateDownload> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Dates.Add(date);
            if (Failure is { } failure) throw new RateException(failure, "Synthetic provider failure");
            if (Dates.Count < MissingBefore) throw new RateException(RateFailure.Unavailable, "No publication");
            return Task.FromResult(Reply ?? Sample(provider, date));
        }
    }
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ValidInMemoryPublicationSurvivesFailedCacheWriteAndLaterNetworkFailure(bool olderDiskRow)
    {
        var latest = Sample();
        var cache = olderDiskRow ? new Cache(Sample(requested: Day.AddDays(-1)).Rates) : new Cache();
        cache.FailStore = true;
        var source = new Source(RateProvider.Nbrb) { Reply = latest };
        using var session = new ExchangeRateSession(cache, [source], new Clock());
        await session.InitializeAsync(RateProvider.Nbrb, true);
        Assert.AreEqual(latest.Rates, session.State.Rates);
        source.Failure = RateFailure.Network;
        await session.RefreshAsync();
        Assert.AreEqual(latest.Rates, session.State.Rates);
        Assert.IsFalse(session.State.IsRefreshing);
        Assert.IsFalse(session.State.IsPersistent);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledOrSupersededDateRefreshCannotStartASecondDownload(bool changeProvider)
    {
        var nbrb = new Source(RateProvider.Nbrb); var cbr = new Source(RateProvider.Cbr);
        var cache = new GateCache();
        using var session = new ExchangeRateSession(cache, [nbrb, cbr], new Clock());
        var pending = session.RefreshAsync(RateProvider.Nbrb, Day);
        await cache.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (changeProvider) await session.SelectAsync(RateProvider.Cbr, Day);
        else session.Cancel();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, nbrb.Dates.Count + cbr.Dates.Count);
        Assert.IsFalse(session.State.IsRefreshing);
        if (changeProvider) Assert.AreEqual(RateProvider.Cbr, session.State.Provider);
    }

    private sealed class GateCache : IRateCache
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OfficialRateSet? Find(RateProvider provider, DateOnly date, int maximumAgeDays = 31, CancellationToken cancellationToken = default)
        {
            if (provider == RateProvider.Cbr) return null;
            Entered.TrySetResult();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Test did not cancel its cache read");
        }
        public void Store(RateDownload download, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public IReadOnlyList<RateHistoryEntry> ReadHistory(RateProvider provider, int offset = 0, int count = 100, CancellationToken cancellationToken = default) => [];
    }

    private sealed class DelayedSource : IOfficialRateSource
    {
        public RateProvider Provider => RateProvider.Nbrb;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<RateDownload> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HonorCancellation { get; init; }
        public Task<RateDownload> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return HonorCancellation ? Completion.Task.WaitAsync(cancellationToken) : Completion.Task;
        }
    }
    private sealed class Cache(params OfficialRateSet[] seed) : IRateCache
    {
        private readonly List<OfficialRateSet> rows = [.. seed];
        public List<RateDownload> Stored { get; } = [];
        public bool FailStore { get; set; }
        public OfficialRateSet? Find(RateProvider provider, DateOnly date, int maximumAgeDays = 31, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (rows) return rows.Where(row => row.Provider == provider && row.EffectiveDate <= date && date.DayNumber - row.EffectiveDate.DayNumber <= maximumAgeDays)
                .OrderByDescending(row => row.EffectiveDate).ThenByDescending(row => row.RetrievedUtc).FirstOrDefault();
        }
        public void Store(RateDownload download, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailStore) throw new RateException(RateFailure.Cache, "Synthetic cache write failure");
            lock (rows) { rows.Add(download.Rates); Stored.Add(download); }
        }
        public IReadOnlyList<RateHistoryEntry> ReadHistory(RateProvider provider, int offset = 0, int count = 100, CancellationToken cancellationToken = default) => [];
    }
}
