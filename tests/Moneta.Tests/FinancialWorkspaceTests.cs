using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class FinancialWorkspaceTests
{
    private string directory = null!;
    private string FileName(string name) => Path.Combine(directory, name + ".moneta");
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "Moneta-financial-workspace-" + Guid.NewGuid()));
    [TestCleanup] public void Cleanup() => Directory.Delete(directory, true);

    private static TransactionDraft Entry(decimal amount, TransactionDirection direction, string label) =>
        new(new DateOnly(2026, 5, 12), amount, direction, label, "Tiers synthétique", "Vie courante", "Exemple", "Carte", "Notes synthétiques");

    [TestMethod]
    public void FinancialDraftsCommitTogetherThenReopenWithoutMixingAccounts()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Registre synthétique");
        var model = workspace.Model!;
        var first = model.AddAccount("EUR A", model.CurrencyRegistry.Get("EUR"), "Courant", 100.01m, null);
        model.AddAccount("EUR B", model.CurrencyRegistry.Get("EUR"), "Épargne", 20.02m, null);
        var byn = model.AddAccount("BYN", model.CurrencyRegistry.Get("BYN"), "Courant", 8000.03m, null);
        model.SelectedAccount = first;
        var income = model.AddTransaction(Entry(50.02m, TransactionDirection.Credit, "Recette synthétique"));
        var expense = model.AddTransaction(Entry(12.34m, TransactionDirection.Debit, "Dépense synthétique"));
        Assert.AreEqual(137.69m, first.Balance);
        Assert.IsFalse(File.Exists(FileName("registre")));
        workspace.Save(FileName("registre"));
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.AreEqual(expense.Id, model.SelectedRow!.Transaction.Id);
        var edited = model.EditTransaction(expense.Id, expense.Revision, Entry(15.67m, TransactionDirection.Debit, "Dépense corrigée"));
        Assert.AreEqual(134.36m, first.Balance);
        Assert.AreEqual(expense.Id, edited.Id);
        workspace.Save();
        Assert.AreEqual(expense.Id, model.SelectedRow!.Transaction.Id);
        model.DeleteTransaction(income.Id, income.Revision);
        Assert.AreEqual(84.34m, first.Balance);
        workspace.Save();
        var folderId = model.DocumentId;
        workspace.New("Autre dossier vierge");
        Assert.AreEqual(0, workspace.Model!.Rows.Count);
        workspace.Open(FileName("registre"));
        model = workspace.Model!;
        Assert.AreEqual(folderId, model.DocumentId);
        Assert.AreEqual(3, model.Accounts.Count);
        Assert.AreEqual(84.34m, model.SelectedAccount!.Balance);
        Assert.AreEqual(expense.Id, model.Rows.Single().Transaction.Id);
        Assert.AreEqual("Dépense corrigée", model.Rows.Single().Label);
        Assert.AreEqual("Notes synthétiques", model.Rows.Single().Transaction.Details);
        model.SelectedAccount = model.Accounts.Single(a => a.Account.Id == byn.Account.Id);
        Assert.AreEqual(8000.03m, model.SelectedAccount.Balance);
        Assert.AreEqual(0, model.Rows.Count);
        workspace.Backup(FileName("archive"));
        DocumentWorkspace.Restore(FileName("archive"), FileName("restaure"));
        using var restored = MonetaFile.Open(FileName("restaure"));
        Assert.AreEqual(folderId, restored.Snapshot.Id);
        Assert.AreEqual(2L, restored.Snapshot.OperationCount); // Includes the retained tombstone.
        Assert.IsTrue(restored.GetTransaction(income.Id, includeDeleted: true)!.IsDeleted);
        var restoredExpense = restored.GetTransaction(expense.Id)!;
        Assert.AreEqual(15.67m, restoredExpense.Amount);
        Assert.AreEqual(TransactionDirection.Debit, restoredExpense.Direction);
        Assert.AreEqual(edited.CreatedUtc, restoredExpense.CreatedUtc);
        Assert.AreEqual(edited.Revision, restoredExpense.Revision);
        Assert.AreEqual(edited.PayeeId, restoredExpense.PayeeId);
        Assert.AreEqual(edited.SubcategoryId, restoredExpense.SubcategoryId);
    }

    [TestMethod]
    public void SaveAsRetainsTheFullLedgerAndAppliesDraftOnlyToNewFile()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Copie financière");
        var model = workspace.Model!;
        model.AddAccount("EUR", model.CurrencyRegistry.Get("EUR"), null, 0m, null);
        var value = model.AddTransaction(Entry(42.03m, TransactionDirection.Credit, "Avant copie"));
        workspace.Save(FileName("source"));
        model.EditTransaction(value.Id, value.Revision, Entry(43.04m, TransactionDirection.Credit, "Après copie"));
        var second = model.AddTransaction(Entry(0.01m, TransactionDirection.Debit, "Nouvelle ligne"));
        workspace.Save(FileName("copie"));
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(43.03m, model.SelectedAccount!.Balance);
        using (var original = MonetaFile.Open(FileName("source")))
        {
            Assert.AreEqual(42.03m, original.GetTransaction(value.Id)!.Amount);
            Assert.AreEqual("Avant copie", original.GetTransaction(value.Id)!.Label);
            Assert.IsNull(original.GetTransaction(second.Id));
        }
        workspace.OpenDemo();
        Assert.AreEqual(3200, workspace.Model!.Rows.Count);
        Assert.ThrowsException<InvalidOperationException>(() => workspace.Model.AddTransaction(Entry(1m, TransactionDirection.Credit, "Interdit")));
        workspace.Open(FileName("copie"));
        Assert.AreEqual(2, workspace.Model!.Rows.Count);
        Assert.AreEqual(43.03m, workspace.Model.SelectedAccount!.Balance);
        Assert.AreEqual(0, workspace.Model.PendingChanges.Count);
    }

    [TestMethod]
    public void ExternalOperationChangeRejectsSaveAndRetainsTheDraft()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Conflit synthétique");
        var model = workspace.Model!;
        model.AddAccount("EUR", model.CurrencyRegistry.Get("EUR"), null, 0m, null);
        var value = model.AddTransaction(Entry(40m, TransactionDirection.Credit, "Original"));
        workspace.Save(FileName("source"));
        model.EditTransaction(value.Id, value.Revision, Entry(41m, TransactionDirection.Credit, "Brouillon conservé"));
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FileName("source"), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "UPDATE Transactions SET label='Modification externe';";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }
        Assert.ThrowsException<DocumentException>(() => workspace.Save());
        Assert.AreSame(model, workspace.Model);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreEqual(1, model.PendingChanges.Count);
        Assert.AreEqual("Brouillon conservé", model.Rows.Single().Label);
        Assert.AreEqual(41m, model.SelectedAccount!.Balance);
    }
}
