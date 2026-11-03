using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;

namespace Moneta.Tests;

[TestClass]
public sealed class OfficialRateCacheTests
{
    private string directory = null!;
    private string CachePath => Path.Combine(directory, "public", "exchange-rates.sqlite");
    private static readonly DateOnly Day = new(2026, 4, 3);
    private static readonly DateTimeOffset Retrieved = new(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonetaRateCacheTests-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void MissingCacheFindAndHistoryDoNotCreateFileOrDirectory()
    {
        var cache = new SqliteRateCache(CachePath);
        Assert.IsNull(cache.Find(RateProvider.Nbrb, Day));
        Assert.AreEqual(0, cache.ReadHistory(RateProvider.Cbr).Count);
        Assert.IsFalse(File.Exists(CachePath));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(CachePath)));
    }


    [TestMethod]
    public void RestartPreservesStableIdentityMetadataAndOriginalRawBytes()
    {
        var download = Nbrb(Day);
        new SqliteRateCache(CachePath).Store(download);
        var reopened = new SqliteRateCache(CachePath);
        var found = reopened.Find(RateProvider.Nbrb, Day)!;
        AssertRates(download.Rates, found);
        Assert.AreEqual(download.Rates.Id, reopened.ReadHistory(RateProvider.Nbrb).Single().Id);
        using var raw = Raw();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT payload,sha256 FROM RateSets;";
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        var bytes = (byte[])reader.GetValue(0);
        CollectionAssert.AreEqual(download.Payload.ToArray(), bytes);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), reader.GetString(1));
        Assert.IsFalse(reader.Read());
    }

    [DataTestMethod]
    [DataRow(RateProvider.Nbrb)]
    [DataRow(RateProvider.Cbr)]
    public void AuthenticPublicPayloadSurvivesRestartByteForByte(RateProvider provider)
    {
        var payload = provider == RateProvider.Nbrb ? OfficialRateFixtures.Nbrb() : OfficialRateFixtures.Cbr();
        var expectedHash = provider == RateProvider.Nbrb ? OfficialRateFixtures.NbrbSha256 : OfficialRateFixtures.CbrSha256;
        var endpoint = provider == RateProvider.Nbrb
            ? "https://api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0"
            : "https://www.cbr.ru/scripts/XML_daily.asp?date_req=03/04/2026";
        var download = OfficialRateParsers.Parse(provider, Day, Retrieved, endpoint, payload);
        new SqliteRateCache(CachePath).Store(download);
        var found = new SqliteRateCache(CachePath).Find(provider, Day)!;
        AssertRates(download.Rates, found);
        Assert.AreEqual(expectedHash, found.PayloadSha256);
        using var raw = Raw();
        CollectionAssert.AreEqual(payload, (byte[])Scalar(raw, "SELECT payload FROM RateSets;")!);
    }

    [TestMethod]
    public void ReturnedQuotesAreFrozenAndCannotMutateLaterReads()
    {
        var download = Nbrb(Day);
        var cache = new SqliteRateCache(CachePath);
        cache.Store(download);
        var found = cache.Find(RateProvider.Nbrb, Day)!;
        Assert.IsFalse(found.Quotes is OfficialRateQuote[]);
        if (found.Quotes is IList<OfficialRateQuote> list)
            Assert.ThrowsException<NotSupportedException>(() => list[0] = list[0] with { OfficialValue = 999m });
        AssertRates(download.Rates, cache.Find(RateProvider.Nbrb, Day)!);
    }

    [TestMethod]
    public void ProviderDateAndInclusiveThirtyOneDayBoundaryAreIndependent()
    {
        var cache = new SqliteRateCache(CachePath);
        var nbrb = Nbrb(Day);
        cache.Store(nbrb);
        var cbr = Cbr(Day.AddDays(2));
        cache.Store(cbr);
        Assert.AreEqual(nbrb.Rates.Id, cache.Find(RateProvider.Nbrb, Day.AddDays(31))!.Id);
        Assert.IsNull(cache.Find(RateProvider.Nbrb, Day.AddDays(32)));
        Assert.IsNull(cache.Find(RateProvider.Cbr, Day));
        Assert.AreEqual(cbr.Rates.Id, cache.Find(RateProvider.Cbr, Day.AddDays(2))!.Id);
        Assert.IsNull(cache.Find(RateProvider.Nbrb, Day.AddDays(-1)));
        Assert.IsNull(cache.Find(RateProvider.Nbrb, Day.AddDays(1), maximumAgeDays: 0));
        Assert.AreEqual(nbrb.Rates.Id, cache.Find(RateProvider.Nbrb, Day, maximumAgeDays: 0)!.Id);
    }

    [TestMethod]
    public void AdvancePublicationStaysInHistoryAndDoesNotLeakBeforeEffectiveDate()
    {
        var cache = new SqliteRateCache(CachePath);
        var current = Cbr(Day);
        var future = Cbr(Day.AddDays(1), requested: Day);
        cache.Store(current);
        cache.Store(future);
        Assert.AreEqual(current.Rates.Id, cache.Find(RateProvider.Cbr, Day)!.Id);
        Assert.AreEqual(future.Rates.Id, cache.Find(RateProvider.Cbr, Day.AddDays(1))!.Id);
        var history = cache.ReadHistory(RateProvider.Cbr);
        Assert.AreEqual(future.Rates.Id, history[0].Id);
        Assert.AreEqual(Day, history[0].RequestedDate);
        Assert.AreEqual(Day.AddDays(1), history[0].EffectiveDate);
    }

    [TestMethod]
    public void OlderRowsRemainInHistoryWithoutExtendingConversionWindow()
    {
        var cache = new SqliteRateCache(CachePath);
        var old = Nbrb(Day.AddDays(-90));
        cache.Store(old);
        Assert.IsNull(cache.Find(RateProvider.Nbrb, Day));
        Assert.AreEqual(old.Rates.Id, cache.ReadHistory(RateProvider.Nbrb).Single().Id);
    }

    [TestMethod]
    public void LatestEffectiveDateWinsBeforeLatestRetrievalAndCorrectionsStayImmutable()
    {
        var cache = new SqliteRateCache(CachePath);
        var original = Nbrb(Day, retrieved: Retrieved);
        var correction = Nbrb(Day, value: "3.5000", retrieved: Retrieved.AddHours(1));
        var lateOlder = Nbrb(Day.AddDays(-1), value: "3.7000", retrieved: Retrieved.AddDays(2));
        cache.Store(correction);
        cache.Store(original);
        cache.Store(lateOlder);
        Assert.AreEqual(correction.Rates.Id, cache.Find(RateProvider.Nbrb, Day)!.Id);
        var history = cache.ReadHistory(RateProvider.Nbrb);
        CollectionAssert.AreEqual(new[] { correction.Rates.Id, original.Rates.Id, lateOlder.Rates.Id }, history.Select(row => row.Id).ToArray());
        Assert.AreNotEqual(original.Rates.PayloadSha256, correction.Rates.PayloadSha256);
        using var raw = Raw();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT payload FROM RateSets WHERE id=$id;";
        command.Parameters.AddWithValue("$id", original.Rates.Id.ToString("D"));
        CollectionAssert.AreEqual(original.Payload.ToArray(), (byte[])command.ExecuteScalar()!);
    }

    [TestMethod]
    public void HistoryPagesHaveStableIdTieBreakAndProviderFilter()
    {
        var cache = new SqliteRateCache(CachePath);
        var rates = Enumerable.Range(0, 5).Select(_ => Nbrb(Day)).ToArray();
        foreach (var download in rates.Reverse()) cache.Store(download);
        cache.Store(Cbr(Day.AddDays(1)));
        var expected = rates.Select(row => row.Rates.Id).OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var page1 = cache.ReadHistory(RateProvider.Nbrb, count: 2);
        var page2 = cache.ReadHistory(RateProvider.Nbrb, offset: 2, count: 2);
        var page3 = cache.ReadHistory(RateProvider.Nbrb, offset: 4, count: 2);
        CollectionAssert.AreEqual(expected, page1.Concat(page2).Concat(page3).Select(row => row.Id).ToArray());
        Assert.AreEqual(0, cache.ReadHistory(RateProvider.Nbrb, offset: 5).Count);
        Assert.AreEqual(expected[0], cache.Find(RateProvider.Nbrb, Day)!.Id);
    }

    [TestMethod]
    public void MinimumDateWindowClampsWithoutUnderflow()
    {
        var date = new DateOnly(1, 1, 2);
        var download = Nbrb(Day);
        var cache = new SqliteRateCache(CachePath);
        cache.Store(download);
        Assert.IsNull(cache.Find(RateProvider.Nbrb, date));
        Assert.AreEqual(download.Rates.Id, cache.Find(RateProvider.Nbrb, Day)!.Id);
    }

    [TestMethod]
    public void InvalidArgumentsNeverCreateCache()
    {
        var cache = new SqliteRateCache(CachePath);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Find((RateProvider)99, Day));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Find(RateProvider.Nbrb, default));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Find(RateProvider.Nbrb, Day, -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Find(RateProvider.Nbrb, Day, 32));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.ReadHistory(RateProvider.Nbrb, -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.ReadHistory(RateProvider.Nbrb, count: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.ReadHistory(RateProvider.Nbrb, count: 101));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(CachePath)));
    }

    [TestMethod]
    public void IndependentIdentitySchemaAndDeleteFullConfigurationHaveNoRealValues()
    {
        new SqliteRateCache(CachePath).Store(Nbrb(Day));
        using var raw = Raw();
        Assert.AreEqual(0x4D465831L, Scalar(raw, "PRAGMA application_id;"));
        Assert.AreEqual(1L, Scalar(raw, "PRAGMA user_version;"));
        Assert.AreEqual("delete", Scalar(raw, "PRAGMA journal_mode;"));
        Assert.AreEqual("ok", Scalar(raw, "PRAGMA integrity_check;"));
        Assert.AreEqual(1L, Scalar(raw, "SELECT count(*) FROM sqlite_schema WHERE type='table';"));
        Assert.AreEqual(0L, Scalar(raw, "SELECT count(*) FROM pragma_table_info('RateSets') WHERE type='REAL';"));
        Assert.AreEqual(8L, Scalar(raw, "SELECT count(*) FROM pragma_table_info('RateSets');"));
    }

    [TestMethod]
    public void CancelBeforeFirstStoreFindOrHistoryNeverPublishesFiles()
    {
        var cache = new SqliteRateCache(CachePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => cache.Store(Nbrb(Day), cancellation.Token));
        Assert.ThrowsException<OperationCanceledException>(() => cache.Find(RateProvider.Nbrb, Day, cancellationToken: cancellation.Token));
        Assert.ThrowsException<OperationCanceledException>(() => cache.ReadHistory(RateProvider.Nbrb, cancellationToken: cancellation.Token));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(CachePath)));
    }

    [TestMethod]
    public void DuplicateIdentityFailsAtomicallyWithoutReplacingHistoricalBatch()
    {
        var cache = new SqliteRateCache(CachePath);
        var original = Nbrb(Day);
        cache.Store(original);
        var correction = Nbrb(Day, value: "9.25");
        correction = correction with { Rates = correction.Rates with { Id = original.Rates.Id } };
        AssertCacheFailure(() => cache.Store(correction));
        AssertRates(original.Rates, cache.Find(RateProvider.Nbrb, Day)!);
        Assert.AreEqual(1, cache.ReadHistory(RateProvider.Nbrb).Count);
    }

    [TestMethod]
    public void SourceBodyMismatchIsRejectedBeforeAnyCacheCreation()
    {
        var original = Nbrb(Day);
        var cache = new SqliteRateCache(CachePath);
        var forgedQuotes = original.Rates.Quotes.Select(quote => quote with { OfficialValue = 9.99m }).ToArray();
        AssertCacheFailure(() => cache.Store(original with { Rates = original.Rates with { Quotes = forgedQuotes } }));
        AssertCacheFailure(() => cache.Store(original with { Rates = original.Rates with { EffectiveDate = Day.AddDays(1) } }));
        AssertCacheFailure(() => cache.Store(original with { Rates = original.Rates with { PayloadSha256 = new string('0', 64) } }));
        var different = Nbrb(Day, value: "8.00");
        AssertCacheFailure(() => cache.Store(original with { Payload = different.Payload }));
        Assert.IsFalse(File.Exists(CachePath));
    }

    [DataTestMethod]
    [DataRow("PRAGMA application_id=1296979009;", false)]
    [DataRow("PRAGMA user_version=2;", true)]
    [DataRow("CREATE TABLE Unexpected(value TEXT) STRICT;", false)]
    [DataRow("DROP INDEX RateSets_ProviderDate;", false)]
    [DataRow("UPDATE RateSets SET sha256='0000000000000000000000000000000000000000000000000000000000000000';", false)]
    [DataRow("UPDATE RateSets SET effective_date='2026-04-04';", false)]
    [DataRow("UPDATE RateSets SET retrieved_utc='2026-04-03T12:00:00.0000000+01:00';", false)]
    [DataRow("UPDATE RateSets SET id='00000000-0000-0000-0000-000000000000';", false)]
    [DataRow("UPDATE RateSets SET payload=x'7B7D';", false)]
    public void InvalidSelectedCacheIsReportedAndPreserved(string mutation, bool unsupported)
    {
        var cache = new SqliteRateCache(CachePath);
        cache.Store(Nbrb(Day));
        using (var raw = Raw()) Execute(raw, mutation);
        var before = File.ReadAllBytes(CachePath);
        var expected = unsupported ? RateFailure.UnsupportedVersion : RateFailure.Cache;
        Assert.AreEqual(expected, Assert.ThrowsException<RateException>(() => cache.Find(RateProvider.Nbrb, Day.AddDays(1))).Failure);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(CachePath));
        Assert.AreEqual(expected, Assert.ThrowsException<RateException>(() => cache.ReadHistory(RateProvider.Nbrb)).Failure);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(CachePath));
        if (!mutation.StartsWith("UPDATE", StringComparison.Ordinal))
        {
            Assert.AreEqual(expected, Assert.ThrowsException<RateException>(() => cache.Store(Nbrb(Day.AddDays(1)))).Failure);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(CachePath));
        }
    }

    [TestMethod]
    public void UnselectedHistoryIsNotParsedButCorruptSelectedRowNeverFallsBack()
    {
        var cache = new SqliteRateCache(CachePath);
        var valid = Nbrb(Day);
        cache.Store(valid);
        cache.Store(Nbrb(Day.AddDays(1)));
        cache.Store(Cbr(Day));
        using (var raw = Raw()) Execute(raw, "UPDATE RateSets SET sha256='0000000000000000000000000000000000000000000000000000000000000000' WHERE provider=0 AND effective_date='2026-04-04';");
        Assert.AreEqual(valid.Rates.Id, cache.Find(RateProvider.Nbrb, Day)!.Id);
        Assert.AreEqual(1, cache.ReadHistory(RateProvider.Cbr).Count);
        Assert.AreEqual(valid.Rates.Id, cache.ReadHistory(RateProvider.Nbrb, offset: 1, count: 1).Single().Id);
        AssertCacheFailure(() => cache.Find(RateProvider.Nbrb, Day.AddDays(1)));
        AssertCacheFailure(() => cache.ReadHistory(RateProvider.Nbrb, count: 1));
    }

    [TestMethod]
    public void ArbitraryFileAndEmptySqliteAreNeverAdoptedOrReset()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllBytes(CachePath, "not a SQLite database"u8.ToArray());
        var before = File.ReadAllBytes(CachePath);
        var cache = new SqliteRateCache(CachePath);
        AssertCacheFailure(() => cache.Find(RateProvider.Nbrb, Day));
        AssertCacheFailure(() => cache.Store(Nbrb(Day)));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(CachePath));
        File.Delete(CachePath);
        using (var raw = Raw(create: true)) Execute(raw, "PRAGMA user_version=1;");
        before = File.ReadAllBytes(CachePath);
        AssertCacheFailure(() => cache.Store(Nbrb(Day)));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(CachePath));
    }

    [TestMethod]
    public async Task ConcurrentCreatorsAndWritersRetainEveryImmutableBatch()
    {
        var downloads = Enumerable.Range(0, 6).Select(index => Nbrb(Day.AddDays(-index), retrieved: Retrieved.AddMinutes(index))).ToArray();
        using var start = new ManualResetEventSlim();
        var writers = downloads.Select(download => Task.Run(() =>
        {
            start.Wait();
            new SqliteRateCache(CachePath).Store(download);
        })).ToArray();
        start.Set();
        await Task.WhenAll(writers);
        var history = new SqliteRateCache(CachePath).ReadHistory(RateProvider.Nbrb);
        CollectionAssert.AreEquivalent(downloads.Select(download => download.Rates.Id).ToArray(), history.Select(row => row.Id).ToArray());
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(CachePath)!).Length);
    }

    [TestMethod]
    public void BusyWriterIsBoundedAndDoesNotPublishPartialRow()
    {
        var cache = new SqliteRateCache(CachePath);
        var original = Nbrb(Day);
        cache.Store(original);
        using (var raw = Raw())
        using (var locked = raw.BeginTransaction())
        {
            var elapsed = Stopwatch.StartNew();
            AssertCacheFailure(() => cache.Store(Nbrb(Day.AddDays(1))));
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), "SQLite lock contention must be bounded.");
            locked.Rollback();
        }
        Assert.AreEqual(1, cache.ReadHistory(RateProvider.Nbrb).Count);
        AssertRates(original.Rates, cache.Find(RateProvider.Nbrb, Day)!);
    }

    [TestMethod]
    public async Task CancellationWhileWriterIsBlockedLeavesCommittedHistoryUnchanged()
    {
        var cache = new SqliteRateCache(CachePath);
        var original = Nbrb(Day);
        cache.Store(original);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using (var raw = Raw())
        using (var locked = raw.BeginTransaction())
        {
            var writer = Task.Run(() =>
            {
                started.Set();
                Assert.ThrowsException<OperationCanceledException>(() => cache.Store(Nbrb(Day.AddDays(1)), cancellation.Token));
            });
            started.Wait();
            cancellation.Cancel();
            await writer;
            locked.Rollback();
        }
        Assert.AreEqual(1, cache.ReadHistory(RateProvider.Nbrb).Count);
        AssertRates(original.Rates, cache.Find(RateProvider.Nbrb, Day)!);
    }

    private static RateDownload Nbrb(DateOnly effective, string value = "3.3851", DateTimeOffset? retrieved = null, DateOnly? requested = null)
    {
        var date = requested ?? effective;
        var endpoint = "https://api.nbrb.by/exrates/rates?ondate=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "&periodicity=0";
        var json = "[{\"Cur_ID\":451,\"Date\":\"" + effective.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "T00:00:00\",\"Cur_Abbreviation\":\"EUR\",\"Cur_Scale\":1,\"Cur_Name\":\"Euro\",\"Cur_OfficialRate\":" + value + "}]";
        return OfficialRateParsers.Parse(RateProvider.Nbrb, date, retrieved ?? Retrieved, endpoint, Encoding.UTF8.GetBytes(json));
    }

    private static RateDownload Cbr(DateOnly effective, DateOnly? requested = null)
    {
        var date = requested ?? effective;
        var endpoint = "https://www.cbr.ru/scripts/XML_daily.asp?date_req=" + date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ValCurs Date=\"" + effective.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) +
            "\" name=\"Foreign Currency Market\"><Valute ID=\"R01239\"><NumCode>978</NumCode><CharCode>EUR</CharCode><Nominal>1</Nominal><Name>Euro</Name><Value>92,7326</Value></Valute></ValCurs>";
        return OfficialRateParsers.Parse(RateProvider.Cbr, date, Retrieved, endpoint, Encoding.UTF8.GetBytes(xml));
    }

    private SqliteConnection Raw(bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = CachePath, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 1
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AssertCacheFailure(Action action) => Assert.AreEqual(RateFailure.Cache, Assert.ThrowsException<RateException>(action).Failure);

    private static void AssertRates(OfficialRateSet expected, OfficialRateSet actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Provider, actual.Provider);
        Assert.AreEqual(expected.ReferenceCurrency, actual.ReferenceCurrency);
        Assert.AreEqual(expected.RequestedDate, actual.RequestedDate);
        Assert.AreEqual(expected.EffectiveDate, actual.EffectiveDate);
        Assert.AreEqual(expected.RetrievedUtc, actual.RetrievedUtc);
        Assert.AreEqual(expected.Endpoint, actual.Endpoint);
        Assert.AreEqual(expected.PayloadSha256, actual.PayloadSha256);
        CollectionAssert.AreEqual(expected.Quotes.ToArray(), actual.Quotes.ToArray());
    }
}
