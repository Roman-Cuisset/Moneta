using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class PointingWorkspaceTests
{
    private string directory = null!;
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Today).AddDays(-3);
    private string FileName(string name) => Path.Combine(directory, name + ".moneta");
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "Moneta-pointing-workspace-" + Guid.NewGuid()));
    [TestCleanup] public void Cleanup() => Directory.Delete(directory, true);
    private static TransactionDraft Draft(decimal amount, TransactionDirection direction, string label) => new(Day, amount, direction, label, "Tiers synthétique", "Vie courante", "Exemple", "Carte", "Notes synthétiques");
    private static TransactionSelection Select(Transaction value) => new(value.Id, value.Revision);
    private static Transaction Row(MainViewModel model, Guid id) => model.Rows.Single(row => row.Transaction.Id == id).Transaction;

    private static void AssertDraftRejectsLedgerChanges(MainViewModel model, Guid id)
    {
        var batch = model.PendingReconciliations;
        var changes = model.PendingChanges.ToArray();
        var rows = model.Rows;
        var selected = model.SelectedRow;
        var pointed = model.PointedBalance;
        var unmarked = model.UnmarkedAmount;
        var balance = model.SelectedAccount!.Balance;
        var value = Row(model, id);
        var draft = Draft(value.Amount + 1m, value.Direction, "Modification refusée");
        Action[] attempts =
        [
            () => model.AddTransaction(draft),
            () => model.EditTransaction(id, value.Revision, draft),
            () => model.DeleteTransaction(id, value.Revision),
            () => model.SetPointing([Select(value)], true),
            () => model.TogglePointing([Select(value)]),
            () => model.ChangeMarkingDate(Select(value), Day.AddDays(-1))
        ];
        foreach (var attempt in attempts)
        {
            var error = Assert.ThrowsException<InvalidOperationException>(attempt);
            StringAssert.Contains(error.Message, "Ctrl+S");
            Assert.AreSame(batch, model.PendingReconciliations);
            CollectionAssert.AreEqual(changes, model.PendingChanges.ToArray());
            Assert.AreSame(rows, model.Rows);
            Assert.AreSame(selected, model.SelectedRow);
            Assert.AreEqual(pointed, model.PointedBalance);
            Assert.AreEqual(unmarked, model.UnmarkedAmount);
            Assert.AreEqual(balance, model.SelectedAccount.Balance);
            Assert.IsTrue(model.IsDirty);
        }
    }

    [TestMethod]
    public void PointingStatementReopeningAndRestorationPreserveTheCompleteFinancialHistory()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Pointage synthétique");
        var model = workspace.Model!;
        model.AddAccount("EUR", model.CurrencyRegistry.Get("EUR"), null, 100m, null);
        var income = model.AddTransaction(Draft(50m, TransactionDirection.Credit, "Recette"));
        var expense = model.AddTransaction(Draft(20m, TransactionDirection.Debit, "Dépense"));
        var outstanding = model.AddTransaction(Draft(5m, TransactionDirection.Debit, "En attente"));
        workspace.Save(FileName("source"));
        model.SetPointing([Select(income), Select(expense)], true);
        Assert.AreEqual(130m, model.PointedBalance);
        Assert.AreEqual(-5m, model.UnmarkedAmount);
        Assert.AreEqual(125m, model.SelectedAccount!.Balance);
        var markedIncome = Row(model, income.Id);
        Assert.AreEqual(income with { Status = ReconciliationStatus.Marked, MarkedOn = markedIncome.MarkedOn,
            Revision = markedIncome.Revision, ModifiedUtc = markedIncome.ModifiedUtc }, markedIncome);
        model.SetPointing([Select(Row(model, expense.Id))], false);
        Assert.AreEqual(150m, model.PointedBalance);
        Assert.AreEqual(-25m, model.UnmarkedAmount);
        workspace.Save();
        Assert.AreEqual(150m, MonetaFile.Inspect(FileName("source")).AccountPointedBalances![income.AccountId]);
        var session = model.StageReconciliation(new(Day.AddDays(1), 130m, "RELEVE-SYNTHETIQUE"), [income.Id, expense.Id]);
        Assert.AreEqual(100m, session.OpeningBalance);
        Assert.AreEqual(130m, session.CalculatedBalance);
        Assert.AreEqual(130m, model.PointedBalance);
        Assert.AreEqual(ReconciliationStatus.Reconciled, Row(model, expense.Id).Status);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreEqual(0L, MonetaFile.Inspect(FileName("source")).ReconciliationCount, "Validation in memory is not a SQLite commit.");
        AssertDraftRejectsLedgerChanges(model, outstanding.Id);
        workspace.Save();
        workspace.New("Autre dossier vierge");
        workspace.Open(FileName("source"));
        model = workspace.Model!;
        Assert.AreEqual(130m, model.PointedBalance);
        Assert.AreEqual(125m, model.SelectedAccount!.Balance);
        Assert.AreEqual(0, model.PendingChanges.Count);
        var protectedIncome = Row(model, income.Id);
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransaction(income.Id, protectedIncome.Revision, Draft(51m, TransactionDirection.Credit, "Interdit")));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(income.Id, protectedIncome.Revision));
        Assert.ThrowsException<InvalidOperationException>(() => model.SetPointing([Select(protectedIncome), Select(Row(model, outstanding.Id))], true));
        Assert.AreEqual(ReconciliationStatus.Unmarked, Row(model, outstanding.Id).Status);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        workspace.Backup(FileName("archive"));
        DocumentWorkspace.Restore(FileName("archive"), FileName("restaure"));
        using (var restored = MonetaFile.Open(FileName("restaure")))
        {
            var details = restored.ReadReconciliation(session.Id);
            Assert.AreEqual(session, details.Session);
            Assert.AreEqual(2, details.Entries.Count);
            Assert.AreEqual(1, details.Events.Count);
            Assert.AreEqual(ReconciliationStatus.Marked, details.Entries.Single(e => e.Original.Id == income.Id).Original.Status);
            Assert.AreEqual(ReconciliationStatus.Unmarked, details.Entries.Single(e => e.Original.Id == expense.Id).Original.Status);
            Assert.AreEqual(130m, restored.Snapshot.AccountPointedBalances![income.AccountId]);
        }
        model.ReopenReconciliation(session.Id, session.Revision, "Réexaminer le relevé synthétique");
        Assert.AreEqual(ReconciliationStatus.Marked, Row(model, income.Id).Status);
        Assert.AreEqual(ReconciliationStatus.Unmarked, Row(model, expense.Id).Status);
        Assert.AreEqual(150m, model.PointedBalance);
        Assert.AreEqual(50m, Row(model, income.Id).Amount);
        Assert.AreEqual(20m, Row(model, expense.Id).Amount);
        AssertDraftRejectsLedgerChanges(model, income.Id);
        workspace.Save();
        workspace.New("Fermeture explicite");
        workspace.Open(FileName("source"));
        model = workspace.Model!;
        var reopened = model.ReconciliationSource!.ReadReconciliation(session.Id);
        Assert.AreEqual(ReconciliationSessionState.Reopened, reopened.Session.State);
        Assert.AreEqual("Réexaminer le relevé synthétique", reopened.Session.ReopenReason);
        Assert.AreEqual(2, reopened.Events.Count);
        Assert.AreEqual(2, reopened.Entries.Count);
        var replacement = model.StageReconciliation(new(Day.AddDays(1), 130m, "RELEVE-CORRIGE"), [income.Id, expense.Id]);
        Assert.AreNotEqual(session.Id, replacement.Id);
        workspace.Save();
        Assert.AreEqual(2L, MonetaFile.Inspect(FileName("source")).ReconciliationCount);
        Assert.AreEqual(2L, model.ReconciliationSource.ReadReconciliationHistory(income.AccountId, 0, 100).TotalCount);
        workspace.Backup(FileName("archive-historique"));
        DocumentWorkspace.Restore(FileName("archive-historique"), FileName("restaure-historique"));
        using var restoredHistory = MonetaFile.Open(FileName("restaure-historique"));
        Assert.AreEqual(ReconciliationSessionState.Reopened, restoredHistory.ReadReconciliation(session.Id).Session.State);
        Assert.AreEqual(ReconciliationSessionState.Validated, restoredHistory.ReadReconciliation(replacement.Id).Session.State);
        Assert.AreEqual(3L, restoredHistory.Snapshot.ReconciliationEventCount);
    }

    [TestMethod]
    public void LockedSaveKeepsPointingDraftAndCanRetryAfterRollback()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Verrou synthétique");
        var model = workspace.Model!;
        model.AddAccount("BYN", model.CurrencyRegistry.Get("BYN"), null, 8000.01m, null);
        var value = model.AddTransaction(Draft(12.34m, TransactionDirection.Debit, "Dépense"));
        workspace.Save(FileName("source"));
        model.SetPointing([Select(value)], true);
        using (var blocker = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FileName("source"), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            blocker.Open();
            using var transaction = blocker.BeginTransaction();
            using var command = blocker.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Document SET name='Écriture non validée';";
            command.ExecuteNonQuery();
            var error = Assert.ThrowsException<DocumentException>(() => workspace.Save());
            Assert.AreEqual(DocumentError.Locked, error.Error);
            Assert.IsTrue(workspace.HasUnsavedChanges);
            Assert.AreEqual(ReconciliationStatus.Marked, Row(model, value.Id).Status);
            Assert.AreEqual(1, model.PendingChanges.Count);
            Assert.AreEqual(7987.67m, model.PointedBalance);
        }
        workspace.Save();
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(7987.67m, MonetaFile.Inspect(FileName("source")).AccountPointedBalances![value.AccountId]);
    }

    [TestMethod]
    public void UnattestedLegacyReconciliationNeedsReasonedReleaseWithoutInventingAStatement()
    {
        var metadata = FinancialDocument.New("Ancien état synthétique");
        var account = new Account(Guid.NewGuid(), "RUB", metadata.Currencies.Single(c => c.Code == "RUB"), 100m);
        metadata = metadata with { Accounts = [account], SelectedAccountId = account.Id };
        var value = new Transaction(Guid.NewGuid(), account.Id, Day, "Hérité synthétique", new Payee(""), new Category(""), 10m, account.Currency,
            TransactionDirection.Debit, ReconciliationStatus.Reconciled, "Date et relevé inconnus");
        using (MonetaFile.Create(FileName("legacy"), metadata, [new(value, 0)])) { }
        using var workspace = new DocumentWorkspace();
        workspace.Open(FileName("legacy"));
        var model = workspace.Model!;
        Assert.IsNull(Row(model, value.Id).MarkedOn);
        Assert.IsNull(Row(model, value.Id).ReconciliationId);
        Assert.AreEqual(90m, model.PointedBalance);
        Assert.ThrowsException<InvalidOperationException>(() => model.StageReconciliation(new(Day.AddDays(1), 100m), []));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(value.Id, 1));
        model.ReleaseLegacyReconciliation(Select(value), "Relevé source non disponible, nouvel examen explicite");
        Assert.AreEqual(100m, model.PointedBalance);
        Assert.AreEqual(ReconciliationStatus.Unmarked, Row(model, value.Id).Status);
        Assert.IsNull(Row(model, value.Id).MarkedOn);
        AssertDraftRejectsLedgerChanges(model, value.Id);
        workspace.Save();
        var saved = MonetaFile.Inspect(FileName("legacy"));
        Assert.AreEqual(0L, saved.ReconciliationCount);
        Assert.AreEqual(1L, saved.ReconciliationEventCount);
        Assert.AreEqual(90m, saved.AccountBalances![account.Id]);
        Assert.AreEqual(100m, saved.AccountPointedBalances![account.Id]);
    }

    [TestMethod]
    public void ReconciliationDraftAllowsAccountNavigationAndSaveButBlocksOtherAccountsMutations()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Frontière d’enregistrement synthétique");
        var model = workspace.Model!;
        model.AddAccount("Premier compte", model.CurrencyRegistry.Get("EUR"), null, 0m, null);
        var firstAccount = model.SelectedAccount!;
        var first = model.AddTransaction(Draft(2m, TransactionDirection.Credit, "Premier"));
        model.AddAccount("Deuxième compte", model.CurrencyRegistry.Get("EUR"), null, 0m, null);
        var secondAccount = model.SelectedAccount!;
        var second = model.AddTransaction(Draft(3m, TransactionDirection.Credit, "Deuxième"));
        workspace.Save(FileName("navigation"));
        model.SelectedAccount = firstAccount;
        var session = model.StageReconciliation(new(Day, 2m), [first.Id]);
        var batch = model.PendingReconciliations;
        model.SelectedAccount = secondAccount;
        Assert.AreSame(batch, model.PendingReconciliations);
        Assert.AreEqual(second.Id, model.Rows.Single().Transaction.Id);
        AssertDraftRejectsLedgerChanges(model, second.Id);
        workspace.Save();
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.AreSame(secondAccount, model.SelectedAccount);
        Assert.AreEqual(session, model.ReconciliationSource!.ReadReconciliation(session.Id).Session);
        model.SetPointing([Select(Row(model, second.Id))], true);
        workspace.Save();
        Assert.AreEqual(ReconciliationStatus.Marked, Row(model, second.Id).Status);
    }
}
