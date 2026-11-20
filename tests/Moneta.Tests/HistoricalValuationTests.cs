using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class HistoricalValuationTests
{
    private static readonly DateOnly Date = new(2026, 1, 12);
    private static readonly DateOnly Today = new(2026, 3, 1);
    private static readonly CurrencyRegistry Currencies = new();
    private string _folder = null!;
    private string CachePath => Path.Combine(_folder, "public.sqlite");

    [TestInitialize]
    public void Init() => _folder = Directory.CreateTempSubdirectory("MonetaHistorical-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public async Task DirectInverseAndCrossUseOnlyTheChosenPublicationAndItsNominals()
    {
        var bank = new Source(RateProvider.Nbrb, date => Nbrb(date));
        var other = new Source(RateProvider.Cbr, date => Cbr(date, date));
        var engine = Engine(new SqliteRateCache(CachePath), bank, other);
        var direct = (await engine.ValueAsync(Convert(-100, "RUB"), Currencies.Get("BYN"), bank.Provider)).Proof!;
        var inverse = (await engine.ValueAsync(Convert(60, "BYN"), Currencies.Get("EUR"), bank.Provider)).Proof!;
        var cross = (await engine.ValueAsync(Convert(1000, "RUB"), Currencies.Get("EUR"), bank.Provider)).Proof!;
        Assert.AreEqual((-4m, HistoricalMethod.Direct, 100L), (direct.Value, direct.Method, direct.Quotation!.Source!.Nominal));
        Assert.AreEqual((20m, HistoricalMethod.Inverse), (inverse.Value, inverse.Method));
        Assert.AreEqual((13.33m, HistoricalMethod.Cross), (cross.Value, cross.Method));
        Assert.AreEqual(13.33m, cross.Reproduce());
        var cbr = (await engine.ValueAsync(Convert(30, "BYN"), Currencies.Get("EUR"), other.Provider)).Proof!;
        Assert.AreEqual(9m, cbr.Value);
        Assert.AreEqual(10L, cbr.Quotation!.Source!.Nominal);
        Assert.AreEqual(RateProvider.Cbr, cbr.Quotation.Provider);
        Assert.AreEqual(1, bank.Calls);
        Assert.AreEqual(1, other.Calls);
    }

    [TestMethod]
    public async Task ExplicitDateCoverageHandlesWeekendsYearBoundaryAndLongInterruptionsWithoutLookAhead()
    {
        var old = new DateOnly(2025, 12, 31);
        var cache = new SqliteRateCache(CachePath);
        cache.Store(Cbr(old, old));
        var bank = new Source(RateProvider.Cbr, date => Cbr(date, old));
        var engine = Engine(cache, bank);
        foreach (var date in new[] { new DateOnly(2026, 1, 1), new(2026, 1, 10), new(2026, 1, 11), new(2026, 2, 20) })
        {
            var inDate = Convert(30, "BYN") with { Date = date };
            var offline = await engine.ValueAsync(inDate, Currencies.Get("EUR"), bank.Provider, HistoricalRateAccess.CacheOnly);
            Assert.AreEqual(HistoricalFailure.MissingRate, offline.Failure);
            var result = await engine.ValueAsync(inDate, Currencies.Get("EUR"), bank.Provider);
            Assert.AreEqual(date, result.Proof!.Quotation!.RequestedDate);
            Assert.AreEqual(old, result.Proof.Quotation.EffectiveDate);
            var saved = await Engine(new SqliteRateCache(CachePath)).ValueAsync(inDate, Currencies.Get("EUR"), bank.Provider, HistoricalRateAccess.CacheOnly);
            Assert.IsTrue(saved.Proof!.Quotation!.FromCache);
            Assert.AreEqual(result.Proof.Value, saved.Proof.Value);
        }
        var futureEffective = await Engine(cache, new Source(RateProvider.Cbr, date => Cbr(date, date.AddDays(1))))
            .ValueAsync(Convert(20, "BYN"), Currencies.Get("EUR"), RateProvider.Cbr, HistoricalRateAccess.Refresh);
        Assert.AreEqual(HistoricalFailure.InvalidRate, futureEffective.Failure);
        var earlyRetrieval = await Engine(cache, new Source(RateProvider.Cbr, date => Cbr(date, old, date.AddDays(-1))))
            .ValueAsync(Convert(20, "BYN"), Currencies.Get("EUR"), RateProvider.Cbr, HistoricalRateAccess.Refresh);
        Assert.AreEqual(HistoricalFailure.InvalidRate, earlyRetrieval.Failure);
    }

    [TestMethod]
    public async Task NbrbMissingOrMismatchedDateAndIncompatibleProviderAreUnavailable()
    {
        var cache = new SqliteRateCache(CachePath);
        cache.Store(Cbr(Date, Date));
        var wrongDate = new Source(RateProvider.Nbrb, date => Nbrb(date, effective: date.AddDays(-1)));
        var missingDate = new Source(RateProvider.Nbrb, date => throw new RateException(RateFailure.Unavailable, "Aucun cours fictif"));
        var wrongProvider = new Source(RateProvider.Nbrb, date => Cbr(date, date));
        var network = new Source(RateProvider.Nbrb, date => throw new RateException(RateFailure.Network, "Réseau fictif indisponible"));
        foreach (var (source, expected) in new[] { (wrongDate, HistoricalFailure.InvalidRate), (missingDate, HistoricalFailure.MissingRate),
            (wrongProvider, HistoricalFailure.InvalidRate), (network, HistoricalFailure.Provider) })
        {
            var failure = await Engine(cache, source).ValueAsync(Convert(20, "BYN"), Currencies.Get("EUR"), RateProvider.Nbrb);
            Assert.IsNull(failure.Proof);
            Assert.AreEqual(expected, failure.Failure);
            Assert.AreEqual(1, source.Calls);
        }
        var unknownPair = await Engine(cache, new Source(RateProvider.Nbrb, date => Nbrb(date)))
            .ValueAsync(Convert(1, "ZZZ"), Currencies.Get("EUR"), RateProvider.Nbrb);
        Assert.AreEqual(HistoricalFailure.MissingRate, unknownPair.Failure);
    }

    [TestMethod]
    public async Task RevisionExplicitlyRefreshesWhileSerializedProofReproducesWithoutCache()
    {
        var cache = new SqliteRateCache(CachePath);
        var corrected = false;
        var bank = new Source(RateProvider.Nbrb, date => Nbrb(date, corrected ? 4 : 3, retrievedLater: corrected));
        var engine = Engine(cache, bank);
        var target = Currencies.Get("EUR");
        var original = (await engine.ValueAsync(Convert(60, "BYN"), target, bank.Provider)).Proof!;
        var proofPath = Path.Combine(_folder, "preuve.fxproof.json");
        HistoricalProofFile.WriteNew(proofPath, original);
        corrected = true;
        var cached = await engine.ValueAsync(original.Input, target, bank.Provider);
        Assert.AreEqual(20m, cached.Proof!.Value);
        var revised = await engine.ValueAsync(original.Input, target, bank.Provider, HistoricalRateAccess.Refresh);
        Assert.AreEqual(15m, revised.Proof!.Value);
        Assert.AreNotEqual(original.Quotation!.PayloadSha256, revised.Proof.Quotation!.PayloadSha256);
        Assert.AreEqual(15m, (await engine.ValueAsync(original.Input, target, bank.Provider, HistoricalRateAccess.CacheOnly)).Proof!.Value);
        Assert.ThrowsException<IOException>(() => HistoricalProofFile.WriteNew(proofPath, revised.Proof!));
        File.Delete(CachePath);
        var proof = HistoricalProofFile.ReadVerified(proofPath);
        Assert.AreEqual(20m, proof.Reproduce());
        Assert.ThrowsException<ArgumentException>(() => (proof with { Value = 15m }).Reproduce());
        Assert.ThrowsException<ArgumentException>(() => (proof with { RulesVersion = 999 }).Reproduce());
    }

    [TestMethod]
    public async Task FutureIdentityZeroRoundingAndOverflowAreDistinguished()
    {
        var bank = new Source(RateProvider.Nbrb, date => Nbrb(date, 2));
        var engine = Engine(new SqliteRateCache(CachePath), bank);
        var euro = Currencies.Get("EUR");
        Assert.AreEqual(HistoricalFailure.Future, (await engine.ValueAsync(Convert(1, "EUR") with { Date = Today.AddDays(1) }, euro, bank.Provider)).Failure);
        Assert.AreEqual(HistoricalFailure.Future, (await engine.ValueAsync(Convert(1, "EUR") with { Use = HistoricalUse.Forecast }, euro, bank.Provider)).Failure);
        Assert.AreEqual(HistoricalMethod.Identity, (await engine.ValueAsync(Convert(decimal.MaxValue, "EUR"), euro, bank.Provider)).Proof!.Method);
        Assert.AreEqual(0, bank.Calls);
        foreach (var (native, expected) in new[] { (0m, 0m), (0.01m, 0m), (0.03m, 0.02m), (-0.03m, -0.02m) })
            Assert.AreEqual(expected, (await engine.ValueAsync(Convert(native, "BYN"), euro, bank.Provider)).Proof!.Value);
        Assert.AreEqual(HistoricalFailure.Overflow, (await engine.ValueAsync(Convert(decimal.MaxValue, "EUR"), Currencies.Get("BYN"), bank.Provider)).Failure);
    }

    [TestMethod]
    public async Task BatchDeduplicatesAcrossCurrenciesRetainsOrderAndUsesEachOperationDate()
    {
        var bank = new Source(RateProvider.Nbrb, day => Nbrb(day, day == Date ? 3 : 4));
        var engine = Engine(new SqliteRateCache(CachePath), bank);
        var inputs = Enumerable.Range(0, 1000).Select(index => (index % 3) switch
        {
            0 => Convert(300, "BYN"),
            1 => Convert(1000, "RUB") with { Date = Date.AddDays(1) },
            _ => Convert(300, "BYN") with { Date = Date.AddDays(1) }
        }).ToArray();
        var output = await engine.ValueBatchAsync(inputs, Currencies.Get("EUR"), bank.Provider);
        Assert.AreEqual(2, bank.Calls);
        CollectionAssert.AreEqual(inputs, output.Select(row => row.Input).ToArray());
        CollectionAssert.AreEqual(new[] { 100m, 10m, 75m }, output.Take(3).Select(row => row.Proof!.Value).ToArray());
    }

    [TestMethod]
    public async Task SavedFlowsPreserveTransferLinksFeesAndCutoffBalancesAcrossBackupAndRestore()
    {
        var path = Path.Combine(_folder, "historique.moneta");
        using var work = new DocumentWorkspace();
        work.New("Fictif");
        var model = work.Model!;
        var eur = model.AddAccount("EUR fictif", Currencies.Get("EUR"), null, 80m, null);
        var byn = model.AddAccount("BYN fictif", Currencies.Get("BYN"), null, 100m, null);
        var rub = model.AddAccount("RUB fictif", Currencies.Get("RUB"), null, 500m, null);
        model.SelectedAccount = byn;
        var expense = model.AddTransaction(new(Date, 19, TransactionDirection.Debit, "Dépense"));
        _ = model.AddTransaction(new(Date.AddDays(1), 555, TransactionDirection.Credit, "Après arrêté"));
        var tombstone = model.AddTransaction(new(Date, 20, TransactionDirection.Credit, "Annulée"));
        model.SelectedAccount = rub;
        var income = model.AddTransaction(new(Date, 500, TransactionDirection.Credit, "Recette"));
        var transfer = model.CreateTransfer(new(eur.Account.Id, byn.Account.Id, Date, 10, 35, "Interne", RateSource: "Justificatif fictif",
            FeeAccountId: rub.Account.Id, FeeAmount: 2));
        work.Save(path);
        model.SelectedAccount = byn;
        model.DeleteTransaction(tombstone.Id, tombstone.Revision);
        work.Save();
        model.SetAccountArchived(byn.Account.Id, true);
        work.Save();
        var state = MonetaFile.Inspect(path);
        var before = Hash(path);
        var balance = MonetaFile.ReadHistoricalBalance(path, state, byn.Account.Id, Date);
        Assert.AreEqual(116m, balance.Amount);
        Assert.AreEqual(2L, balance.OperationCount);
        Assert.AreEqual(100m, MonetaFile.ReadHistoricalBalance(path, state, byn.Account.Id, Date.AddDays(-1)).Amount);
        Assert.AreEqual(671m, state.AccountBalances![byn.Account.Id]);
        Assert.ThrowsException<DocumentException>(() => MonetaFile.ReadHistoricalFlow(path, state, tombstone.Id));
        var bank = new Source(RateProvider.Nbrb, day => Nbrb(day));
        var engine = Engine(new SqliteRateCache(CachePath), bank);
        var target = Currencies.Get("EUR");
        var inputs = new[] { expense.Id, income.Id, transfer.Debit.Id, transfer.Credit.Id, transfer.Fee!.Id }
            .Select(id => MonetaFile.ReadHistoricalFlow(path, state, id).Input()).ToArray();
        var valued = await engine.ValueBatchAsync(inputs, target, bank.Provider);
        var totals = HistoricalFlowTotals.From(valued, target);
        Assert.AreEqual((6.67m, 6.36m), (totals.Income, totals.Expenses));
        Assert.IsTrue(valued.Skip(2).All(item => item.Input.TransferId == transfer.Value.Id));
        var proof = (await engine.ValueAsync(balance.Input(), target, bank.Provider)).Proof!;
        Assert.AreEqual(38.67m, proof.Value);
        Assert.IsFalse(work.HasUnsavedChanges);
        CollectionAssert.AreEqual(before, Hash(path));
        Assert.AreEqual("Justificatif fictif", model.GetTransfer(transfer.Value.Id).Value.RateSource);
        var archive = Path.Combine(_folder, "archive.moneta");
        var copy = Path.Combine(_folder, "copie.moneta");
        work.Backup(archive);
        DocumentWorkspace.Restore(archive, copy);
        var restored = MonetaFile.Inspect(copy);
        Assert.AreEqual(state.LedgerDigest, restored.LedgerDigest);
        Assert.AreEqual(balance, MonetaFile.ReadHistoricalBalance(copy, restored, byn.Account.Id, Date));
        Assert.AreEqual(proof.Value, proof.Reproduce());
    }

    [TestMethod]
    public async Task AggregationRoundsEachLineAndFlagsMissingRowsAndMixedProviders()
    {
        var bank = new Source(RateProvider.Nbrb, date => Nbrb(date, 2));
        var other = new Source(RateProvider.Cbr, date => Cbr(date, date));
        var engine = Engine(new SqliteRateCache(CachePath), bank, other);
        var target = Currencies.Get("EUR");
        HistoricalInput Income(string code) => new(HistoricalUse.Flow, Date, 0.01m, code, Guid.NewGuid(), 1, HistoricalFlowKind.Income);
        var results = await engine.ValueBatchAsync([Income("BYN"), Income("BYN"), Income("ZZZ")], target, bank.Provider);
        Assert.AreEqual(new HistoricalFlowTotals(0m, 0m, 1, 0), HistoricalFlowTotals.From(results, target));
        var cbr = await engine.ValueAsync(Income("BYN"), target, other.Provider);
        Assert.ThrowsException<ArgumentException>(() => HistoricalFlowTotals.From([results[0], cbr], target));
        Assert.ThrowsException<ArgumentException>(() => HistoricalFlowTotals.From([results[0], results[0]], target));
    }

    private static HistoricalInput Convert(decimal amount, string code) => new(HistoricalUse.Conversion, Date, amount, code);

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }
    private static HistoricalValuation Engine(SqliteRateCache cache, params Source[] sources) =>
        new(new(cache, sources), new FixedClock());

    private sealed class FixedClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private sealed class Source(RateProvider provider, Func<DateOnly, RateDownload> load) : IOfficialRateSource
    {
        public RateProvider Provider { get; } = provider;
        public int Calls { get; private set; }
        public Task<RateDownload> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(load(date));
        }
    }

    internal static RateDownload Nbrb(DateOnly day, int eur = 3, DateOnly? effective = null, bool retrievedLater = false)
    {
        var rateDate = (effective ?? day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var requested = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var raw = $$"""
            [{"Date":"{{rateDate}}T00:00:00","Cur_ID":1,"Cur_Abbreviation":"EUR","Cur_Scale":1,"Cur_OfficialRate":{{eur}}},
             {"Date":"{{rateDate}}T00:00:00","Cur_ID":2,"Cur_Abbreviation":"RUB","Cur_Scale":100,"Cur_OfficialRate":4}]
            """;
        return OfficialRateParsers.Parse(RateProvider.Nbrb, day, new(Today.ToDateTime(new TimeOnly(retrievedLater ? 13 : 12, 0)), TimeSpan.Zero),
            $"https://api.nbrb.by/exrates/rates?ondate={requested}&periodicity=0", Encoding.UTF8.GetBytes(raw));
    }

    internal static RateDownload Cbr(DateOnly day, DateOnly effective, DateOnly? retrieved = null)
    {
        var raw = $"""
            <ValCurs Date="{effective:dd.MM.yyyy}">
              <Valute ID="EUR-fictif"><NumCode>978</NumCode><CharCode>EUR</CharCode><Nominal>1</Nominal><Name>Euro fictif</Name><Value>100,00</Value></Valute>
              <Valute ID="BYN-fictif"><NumCode>933</NumCode><CharCode>BYN</CharCode><Nominal>10</Nominal><Name>Rouble fictif</Name><Value>300,00</Value></Valute>
            </ValCurs>
            """;
        return OfficialRateParsers.Parse(RateProvider.Cbr, day, new((retrieved ?? Today).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            "https://www.cbr.ru/scripts/XML_daily.asp?date_req=" + day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(raw));
    }
}
