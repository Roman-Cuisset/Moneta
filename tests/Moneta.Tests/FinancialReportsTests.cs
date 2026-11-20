using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class FinancialReportsTests
{
    private static readonly DateOnly First = new(2026, 1, 1);
    private static readonly DateOnly Day = new(2026, 1, 12);
    private static readonly DateOnly End = new(2026, 1, 31);
    private static readonly DateOnly ClockDay = new(2026, 2, 1);
    private static readonly CurrencyRegistry Currencies = new();
    private string _folder = null!;
    private string PathToDocument => Path.Combine(_folder, "fictif.moneta");

    [TestInitialize]
    public void Init() => _folder = Directory.CreateTempSubdirectory("MonetaReport-").FullName;
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public async Task OneSnapshotValuesEachDateKeepsFeesAndExcludesInternalLegsAndArchivedAccountsByScope()
    {
        using var work = CreateRegistry();
        var model = work.Model!;
        var ledger = Seed(work);
        model.SetAccountArchived(ledger.Rub, true);
        work.Save();
        var snapshot = MonetaFile.Inspect(PathToDocument);
        var cache = OpenCache();
        cache.Store(HistoricalValuationTests.Nbrb(Day, 3));
        cache.Store(HistoricalValuationTests.Nbrb(Day.AddDays(1), 4));
        cache.Store(HistoricalValuationTests.Nbrb(End, 5));
        var criteria = Criteria([ledger.Eur, ledger.Byn, ledger.Rub]);
        Assert.ThrowsException<ArgumentException>(() => MonetaFile.ReadReport(PathToDocument, snapshot, criteria));
        var source = MonetaFile.ReadReport(PathToDocument, snapshot, criteria with { IncludeArchived = true });
        Assert.AreEqual(6, source.Flows.Count, "Les deux jambes sont exclues, le frais reste éligible.");
        Assert.AreEqual(1, source.Flows.Count(row => row.Input.FlowKind == HistoricalFlowKind.Fee));
        Assert.IsTrue(source.Flows.All(row => row.Input.FlowKind is not (HistoricalFlowKind.TransferDebit or HistoricalFlowKind.TransferCredit)));
        var report = await Engine(cache).GenerateAsync(criteria with { IncludeArchived = true }, source, HistoricalRateAccess.CacheOnly);
        Assert.AreEqual(new ReportTotal(100m, 22.03m, 6, 0), report.Totals());
        Assert.AreEqual(77.97m, report.Totals().Net);
        Assert.AreEqual(3, report.Balances.Count);
        var byn = report.Balances.Single(row => row.Subject.AccountId == ledger.Byn);
        Assert.AreEqual(99m, byn.Input.NativeAmount);
        Assert.AreEqual(19.8m, byn.Converted);
        Assert.AreEqual(End, report.QuoteFor(byn)!.EffectiveDate);
        Assert.IsNotNull(report.ConsolidatedBalance());
        report.Verify();
        Assert.IsFalse(model.IsDirty);
        Assert.AreEqual(snapshot.LedgerDigest, MonetaFile.Inspect(PathToDocument).LedgerDigest);
    }

    [TestMethod]
    public async Task CategoryIdsParentUnionMonthlyBudgetAndIncompleteTotalsKeepTheirMeaning()
    {
        using var work = CreateRegistry();
        var ledger = Seed(work);
        var snapshot = MonetaFile.Inspect(PathToDocument);
        var cache = OpenCache();
        cache.Store(HistoricalValuationTests.Nbrb(Day, 3));
        var criteria = Criteria([ledger.Eur, ledger.Byn, ledger.Rub]);
        var source = MonetaFile.ReadReport(PathToDocument, snapshot, criteria, balances: false);
        var partial = await Engine(cache).GenerateAsync(criteria, source, HistoricalRateAccess.CacheOnly);
        Assert.AreEqual(new ReportTotal(100m, 17.03m, 6, 1), partial.Totals());
        var categories = partial.Group(ReportGrouping.Subcategory).Where(row => row.Label.EndsWith("Quotidien", StringComparison.Ordinal));
        Assert.AreEqual(2, categories.Count());
        var envelope = new BudgetPlan(Guid.NewGuid(), "Maison", First, "EUR", 1000L, criteria.Accounts,
            [ledger.Parent1, ledger.Leaf1]);
        var usage = partial.Budget(envelope, ClockDay);
        Assert.AreEqual(7m, usage.Known);
        Assert.AreEqual(1, usage.Missing);
        Assert.IsNull(usage.Difference);
        Assert.AreEqual(70m, usage.Percent);
        Assert.IsFalse(usage.Overrun);
        var filtered = MonetaFile.ReadReport(PathToDocument, snapshot, criteria with { CategoryId = ledger.Leaf2 }, balances: false);
        Assert.AreEqual(1, filtered.Flows.Count);
        Assert.AreEqual(ledger.Parent2, filtered.Flows.Single().Subject.CategoryId);

        cache.Store(HistoricalValuationTests.Nbrb(Day.AddDays(1), 4));
        var complete = await Engine(cache).GenerateAsync(criteria, source, HistoricalRateAccess.CacheOnly);
        var finished = complete.Budget(envelope, ClockDay);
        Assert.AreEqual(12m, finished.Known);
        Assert.AreEqual(-2m, finished.Difference);
        Assert.AreEqual(120m, finished.Percent);
        Assert.IsTrue(finished.Overrun);
        Assert.IsNull(complete.Budget(envelope with { PlannedMinor = 0 }, ClockDay).Percent);
        Assert.AreEqual(complete.Totals().Expense, complete.ExpenseDistribution(2).Sum(group => group.Total.Expense));
        var months = (await Engine(cache).GenerateAsync(criteria with { Through = new(2026, 3, 31) }, source,
            HistoricalRateAccess.CacheOnly)).Group(ReportGrouping.Month);
        CollectionAssert.AreEqual(new[] { "2026-01", "2026-02", "2026-03" }, months.Select(row => row.Key).ToArray());
        Assert.AreEqual(0, months[1].Total.Eligible);
        Assert.ThrowsException<OverflowException>(() => _ = new ReportTotal(decimal.MaxValue, 0.01m, 2, 0).Net);
    }

    [TestMethod]
    public async Task FigsAndBudgetsCommitTogetherReproduceOfflineAndSurviveBackupRestorationAndRevisions()
    {
        using var work = CreateRegistry();
        var ledger = Seed(work);
        var model = work.Model!;
        var cache = OpenCache();
        cache.Store(HistoricalValuationTests.Nbrb(Day, 3));
        cache.Store(HistoricalValuationTests.Nbrb(Day.AddDays(1), 4));
        cache.Store(HistoricalValuationTests.Nbrb(End, 5));
        var criteria = Criteria([ledger.Eur, ledger.Byn, ledger.Rub]);
        var initialSnapshot = MonetaFile.Inspect(PathToDocument);
        var initial = await Engine(cache).GenerateAsync(criteria, MonetaFile.ReadReport(PathToDocument, initialSnapshot, criteria), HistoricalRateAccess.CacheOnly);
        var frozen = FrozenReport.Create("Janvier fictif", initial);
        var plan = new BudgetPlan(Guid.NewGuid(), "Alimentation fictive", First, "EUR", 5000, [ledger.Byn, ledger.Eur], [ledger.Parent1]);
        model.PutBudget(plan);
        model.AddFrozenReport(frozen);
        Assert.AreEqual(0, MonetaFile.Inspect(PathToDocument).Budgets.Count);
        Assert.AreEqual(0, MonetaFile.Inspect(PathToDocument).FrozenReports.Count);
        work.Save();
        var revision = plan with { PlannedMinor = 4300 };
        model.PutBudget(revision);
        var copiedBudget = plan with { Id = Guid.NewGuid(), Month = First.AddMonths(1) };
        model.PutBudget(copiedBudget);
        model.RemoveBudget(copiedBudget.Id);
        work.Save();
        cache.Store(HistoricalValuationTests.Nbrb(Day, 2, retrievedLater: true));
        model.SelectedAccount = model.Accounts.Single(item => item.Account.Id == ledger.Byn);
        var old = model.Rows.First(item => item.Transaction.Id == ledger.ExpenseId).Transaction;
        model.EditTransaction(old.Id, old.Revision, new(Day, 100, TransactionDirection.Debit, "Correction fictive", CategoryName: "Maison", SubcategoryName: "Quotidien"));
        work.Save();
        var changedSnapshot = MonetaFile.Inspect(PathToDocument);
        var changed = await Engine(cache).GenerateAsync(criteria, MonetaFile.ReadReport(PathToDocument, changedSnapshot, criteria), HistoricalRateAccess.CacheOnly);
        Assert.AreNotEqual(initial.Totals(), changed.Totals());
        var next = FrozenReport.Create("Janvier corrigé", changed, frozen.Id);
        model.AddFrozenReport(next);
        work.Save();
        var archive = Path.Combine(_folder, "archive.moneta");
        var restored = Path.Combine(_folder, "restaure.moneta");
        work.Backup(archive);
        DocumentWorkspace.Restore(archive, restored);
        var check = MonetaFile.Inspect(restored);
        Assert.AreEqual(4300L, check.Budgets.Single().PlannedMinor);
        CollectionAssert.AreEquivalent(plan.Accounts.ToArray(), check.Budgets.Single().Accounts.ToArray());
        Assert.AreEqual(frozen.Id, check.FrozenReports.Single(report => report.Id == next.Id).PredecessorId);
        Assert.AreEqual(frozen.Sha256, check.FrozenReports.Single(report => report.Id == frozen.Id).Sha256);
        Assert.AreEqual(changed.Totals(), MonetaFile.ReadFrozenReport(restored, check.Id, next.Id).Totals());
        Assert.AreEqual(initial.Totals(), MonetaFile.ReadFrozenReport(restored, check.Id, frozen.Id).Totals());
        Assert.AreEqual(changedSnapshot.LedgerDigest, check.LedgerDigest);
        work.New("Libérer");
        work.Open(PathToDocument);
        Assert.AreEqual(2, work.Model!.FrozenReports.Count);
    }

    [TestMethod]
    public async Task ForecastUsesOptionalAnchorDistinguishesDraftAndExcludesCommittedOccurrences()
    {
        using var work = CreateRegistry();
        var model = work.Model!;
        var eur = model.Accounts[0].Account;
        var byn = model.Accounts[1].Account;
        var rub = model.Accounts[2].Account;
        var due = ClockDay.AddDays(3);
        var schedule = model.AddSchedule(new(ScheduleKind.Expense, byn.Id, 3000, "Abonnement fictif"), new(ScheduleUnit.Once), due);
        _ = model.AddSchedule(new(ScheduleKind.Income, eur.Id, 1100, "Recette fictive"), new(ScheduleUnit.Once), due);
        _ = model.AddSchedule(new(ScheduleKind.Transfer, eur.Id, 1000, "Interne", DestinationId: byn.Id, FeeAccountId: rub.Id, FeeMinor: 200),
            new(ScheduleUnit.Once), due);
        work.Save();
        var cache = OpenCache();
        var service = new ReportForecast(Valuation(cache));
        var criteria = Criteria([eur.Id, byn.Id, rub.Id]) with { From = ClockDay, Through = ClockDay.AddMonths(1) };
        var snapshot = MonetaFile.Inspect(PathToDocument);
        var plain = await service.GenerateAsync(snapshot, criteria, ClockDay, false, HistoricalRateAccess.CacheOnly);
        Assert.AreEqual(4, plain.Count);
        Assert.IsNull(plain.Single(row => row.Label == "Abonnement fictif").Value);
        var missing = await service.GenerateAsync(snapshot, criteria, ClockDay, true, HistoricalRateAccess.CacheOnly);
        Assert.AreEqual(ForecastValuation.Missing, missing.Single(row => row.Label == "Abonnement fictif").Status);
        cache.Store(HistoricalValuationTests.Nbrb(ClockDay));
        var estimated = await service.GenerateAsync(snapshot, criteria, ClockDay, true, HistoricalRateAccess.CacheOnly);
        var entry = estimated.Single(row => row.Label == "Abonnement fictif");
        Assert.AreEqual((10m, ForecastValuation.Estimated, ClockDay), (entry.Value!.Value, entry.Status, entry.Effective!.Value));
        Assert.IsNull(estimated.Single(row => row.Status == ForecastValuation.Transfer).Value);
        Assert.AreEqual(1, estimated.Count(row => row.IsFee));
        var months = ReportForecast.Monthly(estimated, Currencies.Currencies, criteria.Target, estimates: true);
        Assert.AreEqual(4, months.Count);
        Assert.AreEqual(10.03m, months.Single(item => item.IncludesEstimates).Expense);
        var selected = model.ScheduleCalendar().Between(due, due).Single(row => row.PlanId == schedule.Id);
        model.PostScheduleOccurrence(selected, selected.Terms, due);
        var drafts = model.ScheduleHistory.Where(item => !model.IsScheduleDecisionSaved(item)).ToArray();
        var pending = await service.GenerateAsync(snapshot, criteria, ClockDay, true, HistoricalRateAccess.CacheOnly, draftPosts: drafts);
        Assert.AreEqual(ForecastValuation.Draft, pending.Single(row => row.Label == "Abonnement fictif").Status);
        work.Save();
        var saved = MonetaFile.Inspect(PathToDocument);
        var committed = await service.GenerateAsync(saved, criteria, ClockDay, true, HistoricalRateAccess.CacheOnly);
        Assert.IsFalse(committed.Any(row => row.Label == "Abonnement fictif"));
        Assert.AreEqual(0, MonetaFile.ReadReport(PathToDocument, saved, criteria, balances: false).Flows.Count,
            "L’écriture future enregistrée reste hors réalisé jusqu’à sa date.");
    }

    [TestMethod]
    public async Task ArchiveRejectsEditedPayloadAndSqliteBackupCannotCertifyIt()
    {
        FinancialDocument original;
        using (var work = CreateRegistry())
        {
            var ledger = Seed(work);
            var snapshot = MonetaFile.Inspect(PathToDocument);
            var criteria = Criteria([ledger.Eur]);
            var report = await Engine(OpenCache()).GenerateAsync(criteria, MonetaFile.ReadReport(PathToDocument, snapshot, criteria), HistoricalRateAccess.CacheOnly);
            work.Model!.AddFrozenReport(FrozenReport.Create("Test fictif", report));
            work.Save();
            original = MonetaFile.Inspect(PathToDocument);
        }
        using (var sql = Raw())
        {
            using var command = sql.CreateCommand();
            command.CommandText = "UPDATE FrozenReports SET payload=payload || ' ';";
            command.ExecuteNonQuery();
        }
        Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(PathToDocument));
        var policy = new BackupPolicy { DirectoryPath = Path.Combine(_folder, "versions"), Enabled = true };
        Assert.ThrowsException<DocumentException>(() => new BackupLibrary().Run(policy, BackupKind.Manual, PathToDocument, original));
    }

    [TestMethod]
    public void MigrationFromSevenKeepsScheduleAndCertifiedOriginal()
    {
        Guid schedule;
        using (var work = CreateRegistry())
        {
            var account = work.Model!.Accounts[0].Account;
            schedule = work.Model.AddSchedule(new(ScheduleKind.Expense, account.Id, 100L, "Ancienne échéance"), new(ScheduleUnit.Month), Day).Id;
            work.Save();
        }
        using (var sql = Raw())
        {
            using var command = sql.CreateCommand();
            command.CommandText = """
                DROP TABLE BudgetCategories; DROP TABLE BudgetAccounts; DROP TABLE Budgets; DROP TABLE FrozenReports;
                UPDATE Document SET schema_version=7,min_reader=7,min_writer=7,creator_version='Moneta-7';
                UPDATE MigrationHistory SET version=7,migration_id='create-v7',definition='native-accounts-v7';
                PRAGMA user_version=7;
                """;
            command.ExecuteNonQuery();
        }
        var before = SHA256.HashData(File.ReadAllBytes(PathToDocument));
        using var reopen = new DocumentWorkspace();
        var destination = Path.Combine(_folder, "copie.moneta");
        reopen.OpenMigratedCopy(PathToDocument, destination);
        var original = MonetaBackup.Inspect(Path.Combine(_folder, "copie.avant-migration.moneta"));
        var upgraded = MonetaFile.Inspect(destination);
        Assert.AreEqual(7, original.Snapshot.SchemaVersion);
        Assert.AreEqual(8, upgraded.SchemaVersion);
        Assert.AreEqual(schedule, upgraded.Schedules.Single().Id);
        Assert.AreEqual(original.Snapshot.LedgerDigest, upgraded.LedgerDigest);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(PathToDocument)));
    }

    private DocumentWorkspace CreateRegistry()
    {
        var work = new DocumentWorkspace();
        work.New("Dossier fictif");
        foreach (var code in new[] { "EUR", "BYN", "RUB" })
            work.Model!.AddAccount(code, Currencies.Get(code), null, code == "BYN" ? 100 : 0, null);
        work.Save(PathToDocument);
        return work;
    }

    private static Seeded Seed(DocumentWorkspace work)
    {
        var model = work.Model!;
        var eur = model.Accounts[0]; var byn = model.Accounts[1]; var rub = model.Accounts[2];
        model.SelectedAccount = eur;
        _ = model.AddTransaction(new(Day, 100m, TransactionDirection.Credit, "Salaire fictif"));
        _ = model.AddTransaction(new(Day, 10m, TransactionDirection.Debit, "Sortie fictive", CategoryName: "Loisirs", SubcategoryName: "Quotidien"));
        model.SelectedAccount = byn;
        var expense = model.AddTransaction(new(Day, 15m, TransactionDirection.Debit, "Dépense fictive", CategoryName: "Maison", SubcategoryName: "Quotidien"));
        _ = model.AddTransaction(new(Day.AddDays(1), 20m, TransactionDirection.Debit, "Dépense lendemain", CategoryName: "Maison", SubcategoryName: "Quotidien"));
        _ = model.AddTransaction(new(Day, 6m, TransactionDirection.Debit, "Dépense racine", CategoryName: "Maison"));
        _ = model.CreateTransfer(new(eur.Account.Id, byn.Account.Id, Day, 10, 40, "Interne fictif", FeeAccountId: rub.Account.Id, FeeAmount: 2));
        work.Save();
        var categories = model.Categories;
        var parent2 = categories.Single(item => item.Name == "Loisirs").Id;
        return new(eur.Account.Id, byn.Account.Id, rub.Account.Id, expense.CategoryId!.Value, expense.SubcategoryId!.Value,
            parent2, categories.Single(item => item.ParentId == parent2).Id, expense.Id);
    }

    private sealed record Seeded(Guid Eur, Guid Byn, Guid Rub, Guid Parent1, Guid Leaf1, Guid Parent2, Guid Leaf2, Guid ExpenseId);

    private SqliteRateCache OpenCache() => new(Path.Combine(_folder, "change.sqlite"));
    private SqliteConnection Raw()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = PathToDocument, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static ReportCriteria Criteria(IReadOnlyList<Guid> accounts) => new(First, End, accounts, false, null, Currencies.Get("EUR"), RateProvider.Nbrb, ClockDay);
    private static FinancialReportEngine Engine(SqliteRateCache cache) => new(Valuation(cache));
    private static HistoricalValuation Valuation(SqliteRateCache cache) => new(new(cache, []), new ReportClock());

    private sealed class ReportClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(ClockDay.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
