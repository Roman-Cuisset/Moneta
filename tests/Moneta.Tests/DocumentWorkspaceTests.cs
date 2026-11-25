using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class DocumentWorkspaceTests
{
    private string _directory = null!;
    private string FileName(string name) => Path.Combine(_directory, name + ".moneta");
    [TestInitialize] public void Initialize() { _directory = Path.Combine(Path.GetTempPath(), "Moneta-workspace-" + Guid.NewGuid()); Directory.CreateDirectory(_directory); }
    [TestCleanup] public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void ManualSaveCommitsOnlyOnRequestAndReloadDoesNotMergeFolders()
    {
        using var workspace = new DocumentWorkspace();
        Assert.IsFalse(workspace.CanSave);
        workspace.New("Essai vierge");
        Assert.IsTrue(workspace.HasUnsavedChanges);
        var model = workspace.Model!;
        var eur = model.CurrencyRegistry.Get("EUR");
        var first = model.AddAccount("Premier", eur, "Courant", 1234.56m, "Note fictive");
        Assert.IsFalse(File.Exists(FileName("premier")));
        workspace.Save(FileName("premier"));
        var folderId = model.DocumentId;
        Assert.IsFalse(workspace.HasUnsavedChanges);
        model.AddAccount("Non enregistré", eur, null, -0.01m, null);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        workspace.New("Autre dossier"); // Caller has explicitly chosen to discard.
        Assert.AreEqual(0, workspace.Model!.Accounts.Count);
        workspace.Open(FileName("premier"));
        Assert.AreEqual(folderId, workspace.Model!.DocumentId);
        Assert.AreEqual(1, workspace.Model.Accounts.Count);
        Assert.AreEqual(first.Account.Id, workspace.Model.SelectedAccountId);
        Assert.AreEqual(1234.56m, workspace.Model.SelectedAccount!.Balance);
        Assert.AreEqual("Note fictive", workspace.Model.SelectedAccount.Account.Notes);
    }

    [TestMethod]
    public void FailedSaveAndFailedOpenKeepTheCurrentStagedData()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("À conserver");
        var model = workspace.Model!;
        model.AddAccount("EUR fictif", model.CurrencyRegistry.Get("EUR"), null, -42.03m, null);
        File.WriteAllText(FileName("existant"), "Ne pas écraser");
        Assert.ThrowsException<DocumentException>(() => workspace.Save(FileName("existant")));
        Assert.AreSame(model, workspace.Model);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreEqual("Ne pas écraser", File.ReadAllText(FileName("existant")));
        Assert.ThrowsException<DocumentException>(() => workspace.Open(FileName("absent")));
        Assert.AreSame(model, workspace.Model);
        Assert.AreEqual(-42.03m, model.Accounts.Single().Balance);
        Assert.IsNull(workspace.FilePath);
    }

    [TestMethod]
    public void SharedTransactionEntryStagesExactOperationsAndCustomValuesUntilExplicitSave()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Saisie rapide fictive");
        var model = workspace.Model!;
        var euro = model.CurrencyRegistry.Get("EUR");
        var account = model.AddAccount("Courant fictif", euro, null, 5m, null).Account;
        var path = FileName("Saisie fictive é Ω");
        workspace.Save(path);
        var culture = System.Globalization.CultureInfo.GetCultureInfo(L.French);
        var input = new TransactionEntry("12/04/2026", 0, "1.10", "Premier", "Mode libre", "Tiers libre", "Catégorie libre", "Enfant");
        var debit = model.AddTransaction(input.CreateDraft(euro, culture));
        var credit = model.AddTransaction((input with { Direction = 1, Amount = "2,20", Label = "Deuxième" }).CreateDraft(euro, culture));
        Assert.AreEqual(debit.SubcategoryId, credit.SubcategoryId);
        Assert.AreEqual(debit.PayeeId, credit.PayeeId);
        Assert.AreEqual(debit.PaymentMethodId, credit.PaymentMethodId);
        Assert.AreEqual(6.10m, model.SelectedAccount!.Balance);
        Assert.ThrowsException<ArgumentException>(() => (input with { Amount = "1,234" }).CreateDraft(euro, culture));
        Assert.AreEqual(0, MonetaFile.Inspect(path).OperationCount);
        Assert.IsTrue(workspace.HasUnsavedChanges);

        workspace.Save();
        Assert.IsFalse(workspace.HasUnsavedChanges);
        workspace.New("Autre dossier fictif");
        workspace.Open(path);
        Assert.AreEqual(account.Id, workspace.Model!.SelectedAccountId);
        Assert.AreEqual(2, workspace.Model.Rows.Count);
        Assert.AreEqual(6.10m, workspace.Model.SelectedAccount!.Balance);
    }

    [TestMethod]
    public void SaveAsPreservesTheSourceAndDemoCannotBeSaved()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Copie");
        var model = workspace.Model!;
        model.AddAccount("Premier", model.CurrencyRegistry.Get("BYN"), null, 8000.01m, null);
        workspace.Save(FileName("source"));
        var id = model.DocumentId;
        model.AddAccount("Deuxième", model.CurrencyRegistry.Get("RUB"), null, -987.65m, null);
        workspace.Save(FileName("copie"));
        var original = MonetaFile.Inspect(FileName("source"));
        Assert.AreEqual(id, original.Id);
        Assert.AreEqual(1, original.Accounts.Count);
        Assert.AreEqual(8000.01m, original.Accounts.Single().OpeningBalance);
        workspace.OpenDemo();
        Assert.IsFalse(workspace.CanSave);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(3200, workspace.Model!.Rows.Count);
        Assert.ThrowsException<InvalidOperationException>(() => workspace.Save(FileName("demo")));
        Assert.IsFalse(File.Exists(FileName("demo")));
        workspace.Open(FileName("copie"));
        Assert.AreEqual(id, workspace.Model!.DocumentId);
        Assert.AreEqual(2, workspace.Model.Accounts.Count);
        Assert.AreEqual(-987.65m, workspace.Model.SelectedAccount!.Balance);
    }

    [TestMethod]
    public void BackupRequiresCommittedDataAndRestoreLeavesCurrentWorkspaceUntouched()
    {
        using var workspace = new DocumentWorkspace();
        workspace.New("Sauvegarde");
        var model = workspace.Model!;
        model.AddAccount("EUR", model.CurrencyRegistry.Get("EUR"), null, 10.01m, null);
        Assert.ThrowsException<InvalidOperationException>(() => workspace.Backup(FileName("trop-tot")));
        workspace.Save(FileName("source"));
        workspace.Backup(FileName("sauvegarde"));
        model.AddAccount("BYN en attente", model.CurrencyRegistry.Get("BYN"), null, 20.02m, null);
        DocumentWorkspace.Restore(FileName("sauvegarde"), FileName("restauration"));
        Assert.AreSame(model, workspace.Model);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreEqual(2, model.Accounts.Count);
        var restored = MonetaFile.Inspect(FileName("restauration"));
        Assert.AreEqual(model.DocumentId, restored.Id);
        Assert.AreEqual(10.01m, restored.Accounts.Single().OpeningBalance);
    }
}
