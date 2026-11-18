using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.BankPerfect;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class AccountManagementTests
{
    private string directory = null!;
    private string FileName => Path.Combine(directory, "comptes.moneta");
    private static readonly DateOnly Day = new(2025, 1, 2);

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("MonetaAccounts-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void UnicodePropertiesSameNameAccountsAndOrderRoundTripThroughSaveBackupAndRestore()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var first = Add(model, "EUR", "Compte d’origine", 12.34m);
        var second = Add(model, "EUR", "Épargne 日本語");
        var third = Add(model, "BYN", "Séparé");
        workspace.Save(FileName);
        var transaction = model.AddTransaction(new(Day, 7m, TransactionDirection.Credit, "Écriture fictive"));
        workspace.Save();
        var before = MonetaFile.Inspect(FileName);
        var draft = new AccountProperties("  Épargne 日本語  ", " Livret ", " Notes\nsecond paragraphe ", " Établissement β ", " 0017-A ", " Titulaire λ ");

        model.EditAccount(first.Account.Id, draft);
        model.MoveAccount(third.Account.Id, -1);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreNotEqual(first.DisplayName, second.DisplayName);
        Assert.AreEqual("Épargne 日本語", first.Name);
        CollectionAssert.AreEqual(before.Accounts.ToArray(), MonetaFile.Inspect(FileName).Accounts.ToArray());
        workspace.Save();

        var path = Path.Combine(directory, "backup.moneta");
        var restoredPath = Path.Combine(directory, "restored.moneta");
        workspace.Backup(path);
        DocumentWorkspace.Restore(path, restoredPath);
        workspace.New("Fermer");
        workspace.Open(restoredPath);
        var restored = workspace.Model!.CaptureDocument();
        CollectionAssert.AreEqual(new[] { first.Account.Id, third.Account.Id, second.Account.Id }, restored.Accounts.Select(value => value.Id).ToArray());
        Assert.AreEqual(first.Account, restored.Accounts[0]);
        Assert.AreEqual("Établissement β", restored.Accounts[0].BankName);
        Assert.AreEqual("0017-A", restored.Accounts[0].BankReference);
        Assert.AreEqual("Titulaire λ", restored.Accounts[0].Holder);
        Assert.AreEqual("Notes\nsecond paragraphe", restored.Accounts[0].Notes);
        Assert.AreEqual(12.34m, restored.Accounts[0].OpeningBalance);
        Assert.AreEqual(before.LedgerDigest, restored.LedgerDigest);
        Assert.AreEqual(transaction.Id, workspace.Model!.Rows.Single().Transaction.Id);
    }

    [TestMethod]
    public void InvalidPropertyDraftAndDiscardDoNotAlterTheSavedAccount()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var summary = Add(model, "EUR", "Original");
        workspace.Save(FileName);
        var original = summary.Account;
        Assert.ThrowsException<ArgumentException>(() => model.EditAccount(original.Id, new("  ", null, null)));
        Assert.ThrowsException<ArgumentException>(() => model.EditAccount(original.Id, new(new string('X', 121), null, null)));
        Assert.ThrowsException<ArgumentException>(() => model.EditAccount(original.Id, new("OK", null, null, "bad\0name")));
        Assert.AreEqual(original, summary.Account);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        model.EditAccount(original.Id, new("À abandonner", null, null));
        workspace.New("Fermer sans enregistrer");
        workspace.Open(FileName);
        Assert.AreEqual(original, workspace.Model!.SelectedAccount!.Account);
    }

    [TestMethod]
    public void ArchivedTransferAndFeesRemainReadableAndReactivateWithoutReplacingLegs()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var source = Add(model, "EUR", "Source", 200m);
        var destination = Add(model, "BYN", "Destination");
        var fee = Add(model, "RUB", "Frais", 30m);
        var draft = new TransferDraft(source.Account.Id, destination.Account.Id, Day, 20m, 60m,
            "Échange", "Note fictive", Day, "Taux déclaré", fee.Account.Id, 1m);
        var transfer = model.CreateTransfer(draft);
        Assert.ThrowsException<InvalidOperationException>(() => model.SetAccountArchived(fee.Account.Id, true));
        workspace.Save(FileName);
        var original = model.GetTransfer(transfer.Value.Id);
        model.SetAccountArchived(fee.Account.Id, true);
        model.SetAccountArchived(destination.Account.Id, true);
        model.ShowArchivedAccounts = false;
        Assert.AreEqual(1, model.VisibleAccounts.Count);
        model.NavigateToTransferCounterpart(transfer.Value.Id, original.Debit.Id);
        Assert.IsTrue(model.ShowArchivedAccounts);
        Assert.AreEqual(destination.Account.Id, model.SelectedAccountId);
        Assert.AreEqual(original.Credit.Id, model.SelectedRow!.Transaction.Id);
        Assert.IsTrue(model.FindArchivedOperation("éCHANGE"));
        Assert.ThrowsException<InvalidOperationException>(() => model.AddTransaction(new(Day, 1m, TransactionDirection.Credit, "Refus")));
        Assert.ThrowsException<InvalidOperationException>(() => model.CreateTransfer(draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(original.Value.Id, original.Value.Revision, draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransfer(original.Value.Id, original.Value.Revision));
        workspace.Save();
        workspace.New("Fermer");
        workspace.Open(FileName);
        model = workspace.Model!;
        Assert.IsTrue(model.SelectedAccount!.Account.IsArchived);
        Assert.AreEqual(original, model.GetTransfer(original.Value.Id));
        Assert.AreEqual(60m, model.SelectedAccount.Balance);
        model.SetAccountArchived(fee.Account.Id, false);
        model.SetAccountArchived(destination.Account.Id, false);
        workspace.Save();
        Assert.AreEqual(original, model.GetTransfer(original.Value.Id));
        model.EditTransfer(original.Value.Id, original.Value.Revision, draft with { Notes = "Révisé" });
        workspace.Save();
        Assert.AreEqual(original.Fee!.Id, model.GetTransfer(original.Value.Id).Fee!.Id);
    }

    [TestMethod]
    public void ArchivedFormalStatementCanBeReadAndReopenedAfterReactivation()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var account = Add(model, "EUR", "Relevé");
        var credit = model.AddTransaction(new(Day, 15m, TransactionDirection.Credit, "Appel fictif"));
        workspace.Save(FileName);
        var session = model.StageReconciliation(new(Day, 15m), [credit.Id]);
        workspace.Save();
        var details = model.ReconciliationSource!.ReadReconciliation(session.Id);
        model.SetAccountArchived(account.Account.Id, true);
        workspace.Save();
        Assert.IsFalse(model.CanReconcile);
        Assert.ThrowsException<InvalidOperationException>(() => model.ReopenReconciliation(session.Id, session.Revision, "Refus"));
        Assert.ThrowsException<InvalidOperationException>(() => model.ValidateAccountDeletion(account.Account.Id));
        var read = model.ReconciliationSource.ReadReconciliation(session.Id);
        Assert.AreEqual(details.Session, read.Session);
        CollectionAssert.AreEqual(details.Entries.ToArray(), read.Entries.ToArray());
        Assert.AreEqual(ReconciliationStatus.Reconciled, model.Rows.Single().Transaction.Status);
        model.SetAccountArchived(account.Account.Id, false);
        workspace.Save();
        model.ReopenReconciliation(session.Id, session.Revision, "Motif fictif");
        workspace.Save();
        Assert.AreEqual(ReconciliationSessionState.Reopened, model.ReconciliationSource.ReadReconciliation(session.Id).Session.State);
    }

    [TestMethod]
    public void EmptyAccountRemovalIsAtomicAndTombstonesAndOpeningBalanceBlockIt()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var empty = Add(model, "EUR", "Vierge");
        var nonzero = Add(model, "RUB", "Ouverture", 1m);
        var tombstone = Add(model, "BYN", "Mouvement supprimé");
        var row = model.AddTransaction(new(Day, 1m, TransactionDirection.Credit, "Écriture"));
        workspace.Save(FileName);
        model.DeleteTransaction(row.Id, row.Revision);
        workspace.Save();
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteEmptyAccount(nonzero.Account.Id));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteEmptyAccount(tombstone.Account.Id));
        model.SelectedAccount = empty;
        model.DeleteEmptyAccount(empty.Account.Id);
        Assert.AreEqual(3, MonetaFile.Inspect(FileName).Accounts.Count);
        workspace.Save();
        var saved = MonetaFile.Inspect(FileName);
        CollectionAssert.AreEqual(new[] { nonzero.Account.Id, tombstone.Account.Id }, saved.Accounts.Select(item => item.Id).ToArray());
        Assert.AreEqual(1L, saved.OperationCount);
        Assert.AreEqual(nonzero.Account.Id, saved.SelectedAccountId);
    }

    [TestMethod]
    public void ArchiveFilterKeepsHiddenAccountsInTheirSlotsWhenMovingVisibleAccounts()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var first = Add(model, "EUR", "Premier");
        var archived = Add(model, "EUR", "Archivé");
        var last = Add(model, "EUR", "Dernier");
        model.SetAccountArchived(archived.Account.Id, true);
        model.MoveAccount(last.Account.Id, -1);
        workspace.Save(FileName);
        CollectionAssert.AreEqual(new[] { last.Account.Id, archived.Account.Id, first.Account.Id },
            MonetaFile.Inspect(FileName).Accounts.Select(item => item.Id).ToArray());
        model.SetAccountArchived(archived.Account.Id, false);
        Assert.AreEqual(archived, model.VisibleAccounts[1]);
    }

    [TestMethod]
    public void ExternalMetadataChangeRejectsSaveAndRetainsTheDraft()
    {
        using var workspace = NewWorkspace();
        var model = workspace.Model!;
        var account = Add(model, "EUR", "Concurrence");
        workspace.Save(FileName);
        model.EditAccount(account.Account.Id, new("Brouillon", null, null, "Banque brouillon"));
        Execute(FileName, "UPDATE Accounts SET bank_name='Changement externe';");
        Assert.AreEqual(DocumentError.InvalidData, Assert.ThrowsException<DocumentException>(() => workspace.Save()).Error);
        Assert.IsTrue(workspace.HasUnsavedChanges);
        Assert.AreEqual("Banque brouillon", account.Account.BankName);
        Assert.AreEqual("Changement externe", MonetaFile.Inspect(FileName).Accounts.Single().BankName);
        Assert.AreEqual("Concurrence", MonetaFile.Inspect(FileName).Accounts.Single().Name);
    }

    [TestMethod]
    public void V5ImportedAccountMigratesOnACopyAndKeepsOriginThroughMetadataArchiveAndRestore()
    {
        var source = Path.Combine(directory, "origine.bp");
        File.WriteAllBytes(source, BankPerfectV5Tests.Encode("""
            utilisateur=Origine fictive
            guid=source-fictive
            compte0=;;;Import fictif;;1;€;6,55957;F;;;;;;real;UID-LEGACY;
            TNSLPPBNTSOGPTQBC280673
            """));
        var preview = BankPerfectImporter.Prepare(source);
        Assert.IsTrue(preview.Report.CanImport, string.Join(", ", preview.Report.Issues.Select(issue => issue.Code)));
        BankPerfectImporter.Import(preview, FileName);
        Account account;
        byte[] provenance;
        using (var initial = MonetaFile.Open(FileName))
        {
            account = initial.Snapshot.Accounts.Single();
            Assert.AreEqual(0L, initial.Snapshot.OperationCount);
            provenance = initial.ReadBankPerfectProvenance()!;
        }
        Execute(FileName, """
            ALTER TABLE Accounts DROP COLUMN holder;
            ALTER TABLE Accounts DROP COLUMN bank_reference;
            ALTER TABLE Accounts DROP COLUMN bank_name;
            UPDATE Document SET schema_version=5,min_reader=5,min_writer=5;
            PRAGMA user_version=5;
            UPDATE MigrationHistory SET version=5,migration_id='create-v5',definition='native-accounts-v5+bankperfect-v1';
            """);
        var original = SHA256.HashData(File.ReadAllBytes(FileName));
        Assert.AreEqual(DocumentError.MigrationRequired, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(FileName)).Error);
        var migrated = Path.Combine(directory, "migrated.moneta");
        using var workspace = new DocumentWorkspace();
        workspace.OpenMigratedCopy(FileName, migrated);
        var model = workspace.Model!;
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteEmptyAccount(account.Id));
        model.EditAccount(account.Id, new("Renommé dans Moneta", null, null, "Banque fictive"));
        model.SetAccountArchived(account.Id, true);
        workspace.Save();
        var temporary = Add(model, "EUR", "Ajout natif sans historique");
        workspace.Save();
        model.DeleteEmptyAccount(temporary.Account.Id);
        workspace.Save();
        model.SetAccountArchived(account.Id, false);
        workspace.Save();
        var backup = Path.Combine(directory, "backup.moneta");
        workspace.Backup(backup);
        using var restored = MonetaBackup.Restore(backup, Path.Combine(directory, "restored.moneta"));
        var current = restored.Snapshot.Accounts.Single();
        Assert.AreEqual(account.Id, current.Id);
        Assert.AreEqual("Renommé dans Moneta", current.Name);
        Assert.AreEqual("Banque fictive", current.BankName);
        Assert.IsFalse(current.IsArchived);
        CollectionAssert.AreEqual(provenance, restored.ReadBankPerfectProvenance());
        using var audit = JsonDocument.Parse(provenance);
        Assert.IsTrue(audit.RootElement.GetProperty("objects").EnumerateArray().Any(item =>
            item.GetProperty("type").GetString() == "account" && item.GetProperty("sourceId").GetString() == "UID-LEGACY"));
        CollectionAssert.AreEqual(original, SHA256.HashData(File.ReadAllBytes(FileName)));
    }

    private static DocumentWorkspace NewWorkspace()
    {
        var workspace = new DocumentWorkspace();
        workspace.New("Dossier fictif");
        return workspace;
    }

    private static AccountSummary Add(MainViewModel model, string currency, string name, decimal opening = 0) =>
        model.AddAccount(name, model.CurrencyRegistry.Get(currency), null, opening, null);

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
