using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class SchedulingTests
{
    private static readonly DateOnly January = new(2024, 1, 31);
    private string _directory = null!;
    private string Source => Path.Combine(_directory, "actif.moneta");

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("MonetaSchedule-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void RecurrencesAreAnchoredAndKeepCalendarDatesAndBoundaries()
    {
        Assert.AreEqual(new DateOnly(2024, 2, 1), new ScheduleRule(ScheduleUnit.Day).At(January, 1));
        Assert.AreEqual(new DateOnly(2024, 2, 21), new ScheduleRule(ScheduleUnit.Week).At(January, 3));
        Assert.AreEqual(new DateOnly(2024, 2, 14), new ScheduleRule(ScheduleUnit.Week, 2).At(January, 1));
        var monthly = new ScheduleRule(ScheduleUnit.Month);
        CollectionAssert.AreEqual(new DateOnly?[] { January, new(2024, 2, 29), new(2024, 3, 31), new(2024, 4, 30) },
            Enumerable.Range(0, 4).Select(index => monthly.At(January, index)).ToArray());
        Assert.AreEqual(new DateOnly(2024, 3, 31), new ScheduleRule(ScheduleUnit.Month, 2).At(January, 1));
        Assert.AreEqual(new DateOnly(2024, 4, 30), new ScheduleRule(ScheduleUnit.Month, 3).At(January, 1));
        Assert.AreEqual(new DateOnly(2024, 7, 31), new ScheduleRule(ScheduleUnit.Month, 6).At(January, 1));
        Assert.AreEqual(new DateOnly(2024, 2, 29), new ScheduleRule(ScheduleUnit.Month, 1, true).At(new(2024, 1, 12), 1));
        var annual = new ScheduleRule(ScheduleUnit.Year);
        Assert.AreEqual(new DateOnly(2025, 2, 28), annual.At(new(2024, 2, 29), 1));
        Assert.AreEqual(new DateOnly(2028, 2, 29), annual.At(new(2024, 2, 29), 4));
        Assert.AreEqual(new DateOnly(2025, 1, 2), new ScheduleRule(ScheduleUnit.Day, 3).At(new(2024, 12, 30), 1));
        Assert.AreEqual(DayOfWeek.Sunday, monthly.At(January, 2)!.Value.DayOfWeek);
        Assert.IsNull(new ScheduleRule(ScheduleUnit.Once).At(January, 1));
        Assert.IsNull(annual.At(DateOnly.MaxValue, 1));
        Assert.AreEqual(2, monthly.After(January, new(2024, 3, 1)));
    }

    [TestMethod]
    public void CalendarCountsLargeRangesAndFirstPageDoesNotConsumeOtherOccurrences()
    {
        var terms = new ScheduleTerms(ScheduleKind.Expense, Guid.NewGuid(), 50, "Charge fictive");
        var plans = Enumerable.Range(0, 1000).Select(_ => new SchedulePlan(Guid.NewGuid(), terms,
            new(ScheduleUnit.Day), January, January.AddDays(9))).ToArray();
        var calendar = new ScheduleCalendar(plans, []);
        Assert.AreEqual(10000L, calendar.Count(DateOnly.MinValue, DateOnly.MaxValue));
        Assert.AreEqual(1000L, calendar.Count(January, January));
        var page = calendar.Between(January, DateOnly.MaxValue).Skip(1000).Take(100).ToArray();
        Assert.AreEqual(100, page.Length);
        Assert.IsTrue(page.All(item => item.Date == January.AddDays(1) && !item.Actionable && item.Number == 2));
        Assert.AreEqual(9000L, calendar.Count(January.AddDays(1), DateOnly.MaxValue));
    }

    [TestMethod]
    public void ConfirmationAndSaveAreSeparateAndIdentitySurvivesRestartAndNormalLedgerDeletion()
    {
        using var work = Workspace();
        var model = work.Model!;
        var eur = model.Accounts[0].Account;
        var rent = model.AddSchedule(new(ScheduleKind.Expense, eur.Id, 12345, "Loyer", "Test", "Bailleur", "Logement", "Loyer", "Virement"),
            new(ScheduleUnit.Month), January);
        var salary = model.AddSchedule(new(ScheduleKind.Income, eur.Id, 7500, "Salaire"), new(ScheduleUnit.Once), January);
        work.Save();
        Assert.AreEqual(0L, MonetaFile.Inspect(Source).OperationCount);
        Assert.AreEqual(0m, model.Accounts[0].Balance);

        var item = Occurrence(model, rent.Id);
        model.PostScheduleOccurrence(item, item.Terms with { AmountMinor = 13000, Label = "Loyer ajusté" }, January.AddDays(1));
        var expense = model.ScheduleHistory.Single();
        Assert.IsFalse(model.IsScheduleDecisionSaved(expense));
        Assert.AreEqual(0L, MonetaFile.Inspect(Source).OperationCount);
        Assert.ThrowsException<InvalidOperationException>(() => model.PostScheduleOccurrence(item, item.Terms, item.Date));
        var income = Occurrence(model, salary.Id);
        model.PostScheduleOccurrence(income, income.Terms, income.Date);
        work.Save();
        Assert.AreEqual(2L, MonetaFile.Inspect(Source).OperationCount);
        Assert.AreEqual(-55m, MonetaFile.Inspect(Source).AccountBalances![eur.Id]);
        Assert.IsTrue(model.IsScheduleDecisionSaved(expense));
        Assert.ThrowsException<ArgumentException>(() => model.EditSchedule(rent.Id, rent.Terms, rent.Rule, January, null));
        work.New("Fermer");
        work.Open(Source);
        model = work.Model!;
        Assert.ThrowsException<InvalidOperationException>(() => model.PostScheduleOccurrence(item, item.Terms, item.Date));
        Assert.AreEqual(12345L, Occurrence(model, rent.Id).Terms.AmountMinor);
        var expenseRow = model.Rows.Single(row => row.Transaction.Id == expense.TransactionId).Transaction;
        Assert.AreEqual("Loyer ajusté", expenseRow.Label);
        Assert.AreEqual(130m, expenseRow.Amount);
        model.DeleteTransaction(expenseRow.Id, expenseRow.Revision);
        work.Save();
        Assert.AreEqual(expense.TransactionId, model.ScheduleHistory.First(entry => entry.PlanId == rent.Id).TransactionId);
        Assert.ThrowsException<InvalidOperationException>(() => model.PostScheduleOccurrence(item, item.Terms, item.Date));
    }

    [TestMethod]
    public void ReportsKeepIdentityAndDoNotBlockTheNextOccurrenceOrChangeTheFutureTemplate()
    {
        using var work = Workspace();
        var model = work.Model!;
        var plan = model.AddSchedule(new(ScheduleKind.Expense, model.Accounts[0].Account.Id, 123, "Mensuelle"),
            new(ScheduleUnit.Month), January);
        var initial = Occurrence(model, plan.Id);
        model.DeferScheduleOccurrence(initial, January.AddMonths(3));
        var next = Occurrence(model, plan.Id);
        Assert.AreEqual(new DateOnly(2024, 2, 29), next.Date);
        Assert.AreEqual(2L, next.Number);
        model.SkipScheduleOccurrence(next);
        model.EditSchedule(plan.Id, plan.Terms with { AmountMinor = 234 }, plan.Rule, new(2024, 3, 31), null);
        var delayed = model.ScheduleCalendar().Between(January, January.AddYears(1))
            .Single(item => item.PlanId == plan.Id && item.Number == 1);
        Assert.AreEqual(123L, delayed.Terms.AmountMinor);
        Assert.AreEqual(January, delayed.OriginalDate);
        model.SuspendSchedule(plan.Id, true);
        Assert.AreEqual(0L, model.ScheduleCalendar().Count(January, January.AddYears(1)));
        Assert.ThrowsException<InvalidOperationException>(() => model.PostScheduleOccurrence(delayed, delayed.Terms, delayed.Date));
        model.SuspendSchedule(plan.Id, false);
        model.DeferScheduleOccurrence(delayed, delayed.Date.AddDays(1));
        Assert.ThrowsException<InvalidOperationException>(() => model.SkipScheduleOccurrence(delayed));
        work.Save();
        model.RetireSchedule(plan.Id);
        work.Save();
        Assert.AreEqual(3, MonetaFile.Inspect(Source).ScheduleHistory.Count);
        Assert.AreEqual(0L, MonetaFile.Inspect(Source).OperationCount);
        Assert.IsTrue(MonetaFile.Inspect(Source).Schedules.Single().Retired);
    }

    [TestMethod]
    public void CrossCurrencyTransferRequiresActualAmountsAndArchivedSourceDestinationOrFeesRefusePosting()
    {
        using var work = Workspace();
        var model = work.Model!;
        var (eur, byn, rub) = (model.Accounts[0].Account, model.Accounts[1].Account, model.Accounts[2].Account);
        var plan = model.AddSchedule(new(ScheduleKind.Transfer, eur.Id, 1099, "Épargne", DestinationId: byn.Id,
            FeeAccountId: rub.Id, FeeMinor: 27), new(ScheduleUnit.Month), January);
        var occurrence = Occurrence(model, plan.Id);
        Assert.ThrowsException<ArgumentException>(() => model.PostScheduleOccurrence(occurrence, plan.Terms, January));
        var actual = plan.Terms with { DestinationMinor = 3650 };
        foreach (var account in new[] { eur, byn, rub })
        {
            model.SetAccountArchived(account.Id, true);
            Assert.ThrowsException<InvalidOperationException>(() => model.PostScheduleOccurrence(occurrence, actual, January));
            model.SetAccountArchived(account.Id, false);
        }
        model.PostScheduleOccurrence(occurrence, actual, January, "Taux déclaré fictif");
        var history = model.ScheduleHistory.Single();
        Assert.IsNotNull(history.TransferId);
        Assert.AreEqual(3, model.PendingChanges.Count);
        work.Save();
        var transfer = model.GetTransfer(history.TransferId!.Value);
        Assert.AreEqual(10.99m, transfer.Debit.Amount);
        Assert.AreEqual(36.50m, transfer.Credit.Amount);
        Assert.AreEqual(0.27m, transfer.Fee!.Amount);
        Assert.AreEqual("Taux déclaré fictif", transfer.Value.RateSource);
        Assert.AreEqual(1L, MonetaFile.Inspect(Source).TransferCount);
        Assert.IsNull(model.Schedules.Single().Terms.DestinationMinor);
    }

    [TestMethod]
    public void SameCurrencyTransferAndNewFileSaveAsKeepLinkedDecision()
    {
        using var work = Workspace(save: false);
        var model = work.Model!;
        var eur = model.Accounts[0].Account;
        var savings = model.AddAccount("Épargne", eur.Currency, null, 0, null).Account;
        var plan = model.AddSchedule(new(ScheduleKind.Transfer, eur.Id, 1000, "Virement EUR", DestinationId: savings.Id),
            new(ScheduleUnit.Once), January);
        var occurrence = Occurrence(model, plan.Id);
        model.PostScheduleOccurrence(occurrence, occurrence.Terms, January);
        work.Save(Source);
        Assert.AreEqual(1L, MonetaFile.Inspect(Source).TransferCount);
        var result = work.Model!.GetTransfer(work.Model.ScheduleHistory.Single().TransferId!.Value);
        Assert.AreEqual(result.Debit.Amount, result.Credit.Amount);
        Assert.IsNull(result.Value.RateSource);
        var other = model.AddSchedule(new(ScheduleKind.Income, eur.Id, 1, "Branche"), new(ScheduleUnit.Once), January);
        var additional = Occurrence(model, other.Id);
        model.PostScheduleOccurrence(additional, additional.Terms, January);
        var copy = Path.Combine(_directory, "copie.moneta");
        work.Save(copy);
        Assert.AreEqual(1, MonetaFile.Inspect(Source).ScheduleHistory.Count);
        Assert.AreEqual(2, MonetaFile.Inspect(copy).ScheduleHistory.Count);
    }

    [TestMethod]
    public void ScheduledAmountsRoundTripAtZeroAndAtTheExactStorageBoundary()
    {
        using var work = Workspace();
        var model = work.Model!;
        var account = model.Accounts[0].Account;
        var terms = new ScheduleTerms(ScheduleKind.Expense, account.Id, long.MaxValue, "Montant maximal");
        Assert.ThrowsException<ArgumentException>(() => model.AddSchedule(terms with { AmountMinor = -1 }, new(ScheduleUnit.Once), January));
        var debit = model.AddSchedule(terms, new(ScheduleUnit.Once), January);
        var credit = model.AddSchedule(new(ScheduleKind.Income, account.Id, 0, "Recette nulle"), new(ScheduleUnit.Once), January);
        var spending = Occurrence(model, debit.Id);
        var income = Occurrence(model, credit.Id);
        model.PostScheduleOccurrence(spending, spending.Terms, spending.Date);
        model.PostScheduleOccurrence(income, income.Terms, income.Date);
        work.Save();
        work.New("Recharger");
        work.Open(Source);
        var saved = work.Model!.ScheduleHistory.ToDictionary(item => item.PlanId);
        Assert.AreEqual(long.MaxValue, saved[debit.Id].Terms.AmountMinor);
        Assert.AreEqual(0L, saved[credit.Id].Terms.AmountMinor);
        Assert.AreEqual(-92233720368547758.07m, work.Model.Accounts.Single(item => item.Account.Id == account.Id).Balance);
    }

    [TestMethod]
    public void RejectedSqlPostingRollsBackLedgerAndOriginalDraftCanBeSaved()
    {
        using var work = Workspace();
        work.New("Libérer le dossier");
        using var file = MonetaFile.Open(Source);
        var model = new MainViewModel(file.Snapshot, file);
        var plan = model.AddSchedule(new(ScheduleKind.Expense, model.Accounts[0].Account.Id, 499, "Concurrence"),
            new(ScheduleUnit.Once), January);
        var occurrence = Occurrence(model, plan.Id);
        model.PostScheduleOccurrence(occurrence, occurrence.Terms, January);
        var desired = model.CaptureDocument();
        var falseLink = desired with { ScheduleHistory = [desired.ScheduleHistory.Single() with { TransactionId = Guid.NewGuid() }] };
        Assert.ThrowsException<DocumentException>(() => file.Save(falseLink, model.PendingChanges));
        Assert.AreEqual(0L, MonetaFile.Inspect(Source).OperationCount);
        Assert.AreEqual(0, MonetaFile.Inspect(Source).Schedules.Count);
        Assert.AreEqual(DocumentError.InUse, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(Source)).Error);
        file.Save(desired, model.PendingChanges);
        model.AcceptSaved(file.Snapshot);
        Assert.ThrowsException<DocumentException>(() => file.Save(desired, [new(model.Rows.Single().Transaction, 0)]));
        Assert.AreEqual(1L, MonetaFile.Inspect(Source).OperationCount);
    }

    [TestMethod]
    public void ClosingWithoutSavingDiscardsStagedDecisionAndAllowsOneConfirmationOnReopen()
    {
        using var work = Workspace();
        var model = work.Model!;
        var plan = model.AddSchedule(new(ScheduleKind.Income, model.Accounts[0].Account.Id, 123, "Annulation"),
            new(ScheduleUnit.Day), January);
        work.Save();
        var occurrence = Occurrence(model, plan.Id);
        model.PostScheduleOccurrence(occurrence, occurrence.Terms, occurrence.Date);
        work.New("Abandon");
        work.Open(Source);
        Assert.AreEqual(0, work.Model!.ScheduleHistory.Count);
        var reopened = Occurrence(work.Model, plan.Id);
        Assert.AreEqual(occurrence, reopened);
        work.Model.PostScheduleOccurrence(reopened, reopened.Terms, reopened.Date);
        work.Save();
        Assert.AreEqual(1L, MonetaFile.Inspect(Source).OperationCount);
    }

    [TestMethod]
    public void RestoreKeepsHistoryAndPreventsRepostingAnOccurrenceAlreadyInTheArchive()
    {
        using var work = Workspace();
        var model = work.Model!;
        var plan = model.AddSchedule(new(ScheduleKind.Expense, model.Accounts[0].Account.Id, 1, "Déjà traitée"),
            new(ScheduleUnit.Month), January);
        var occurrence = Occurrence(model, plan.Id);
        model.PostScheduleOccurrence(occurrence, occurrence.Terms, occurrence.Date);
        var suspended = model.AddSchedule(new(ScheduleKind.Income, model.Accounts[1].Account.Id, 1, "Suspendue"),
            new(ScheduleUnit.Day), January);
        model.SuspendSchedule(suspended.Id, true);
        _ = model.AddSchedule(new(ScheduleKind.Transfer, model.Accounts[0].Account.Id, 1, "À confirmer",
            DestinationId: model.Accounts[1].Account.Id), new(ScheduleUnit.Month), January);
        work.Save();
        var expected = MonetaFile.Inspect(Source);
        var archived = Path.Combine(_directory, "archive.moneta");
        work.Backup(archived);
        var restored = Path.Combine(_directory, "restauré.moneta");
        DocumentWorkspace.Restore(archived, restored);
        work.Open(restored);
        var actual = MonetaFile.Inspect(restored);
        CollectionAssert.AreEqual(expected.Schedules.ToArray(), actual.Schedules.ToArray());
        CollectionAssert.AreEqual(expected.ScheduleHistory.ToArray(), actual.ScheduleHistory.ToArray());
        Assert.AreEqual(expected.LedgerDigest, actual.LedgerDigest);
        Assert.ThrowsException<InvalidOperationException>(() => work.Model!.PostScheduleOccurrence(occurrence, occurrence.Terms, occurrence.Date));
    }

    [TestMethod]
    public void Schema6CopyGetsASeparateCertifiedRecoveryArchiveAndPreservesSource()
    {
        using (var work = Workspace()) { }
        using (var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Source, Pooling = false }.ToString()))
        {
            sql.Open();
            using var command = sql.CreateCommand();
            command.CommandText = """
                DROP TABLE BudgetCategories; DROP TABLE BudgetAccounts; DROP TABLE Budgets; DROP TABLE FrozenReports;
                DROP TABLE ScheduleDecisions; DROP TABLE SchedulePlans;
                UPDATE Document SET schema_version=6,min_reader=6,min_writer=6,creator_version='Moneta-6'; PRAGMA user_version=6;
                UPDATE MigrationHistory SET version=6,migration_id='create-v6',definition='native-accounts-v6';
                """;
            command.ExecuteNonQuery();
        }
        var before = SHA256.HashData(File.ReadAllBytes(Source));
        using var reopen = new DocumentWorkspace();
        var destination = Path.Combine(_directory, "copie.moneta");
        reopen.OpenMigratedCopy(Source, destination);
        var archive = MonetaBackup.Inspect(Path.Combine(_directory, "copie.avant-migration.moneta"));
        Assert.AreEqual(6, archive.Snapshot.SchemaVersion);
        var upgraded = MonetaFile.Inspect(destination);
        Assert.AreEqual(8, upgraded.SchemaVersion);
        CollectionAssert.AreEqual(archive.Snapshot.Accounts.ToArray(), upgraded.Accounts.ToArray());
        Assert.AreEqual(archive.Snapshot.LedgerDigest, upgraded.LedgerDigest);
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(Source)));
    }

    private DocumentWorkspace Workspace(bool save = true)
    {
        var work = new DocumentWorkspace();
        work.New("Échéancier fictif");
        var model = work.Model!;
        foreach (var code in new[] { "EUR", "BYN", "RUB" })
            model.AddAccount(code, model.CurrencyRegistry.Get(code), null, 0, null);
        model.SelectedAccount = model.Accounts[0];
        if (save) work.Save(Source);
        return work;
    }

    private static ScheduleOccurrence Occurrence(MainViewModel model, Guid plan) =>
        model.ScheduleCalendar().Between(DateOnly.MinValue, January.AddYears(1))
            .First(item => item.PlanId == plan && item.Actionable);
}
