using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class TransferBackupTests
{
    private string directory = null!;
    private string PathFor(string name) => Path.Combine(directory, name + ".moneta");
    private static readonly DateOnly Day = new(2025, 1, 2);

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "Moneta-transfer-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [TestMethod]
    public void CertifiedWalRoundTripRetainsTwoAndThreeLegTransfersReferencesFeesTombstonesAndIndependentStatuses()
    {
        using var source = CreateSource(out var ids);
        var fx = source.ReadTransfer(ids[1]);
        var fee = fx.Fee!;
        source.Save(source.Snapshot, [new(fee with { Status = ReconciliationStatus.Marked,
            MarkedOn = Day.AddDays(1), Revision = fee.Revision + 1, ModifiedUtc = DateTimeOffset.UtcNow }, fee.Revision)]);
        var session = Validate(source, fx.Credit);
        var expected = source.Snapshot;
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        var backup = MonetaBackup.Create(source, PathFor("backup")).FilePath;
        var manifest = JsonNode.Parse(File.ReadAllText(backup + ".manifest.json"))!;
        Assert.AreEqual(1, manifest["ManifestVersion"]!.GetValue<int>());
        Assert.AreEqual(5, manifest["SchemaVersion"]!.GetValue<int>());
        Assert.IsFalse(File.ReadAllText(backup + ".manifest.json").Contains(fx.Value.Reference, StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(backup);
        using (var restored = MonetaBackup.Restore(backup, PathFor("restored")))
        {
            AssertComplete(source, restored, ids);
            var copied = restored.ReadTransfer(fx.Value.Id);
            Assert.AreEqual(ReconciliationStatus.Unmarked, copied.Debit.Status);
            Assert.AreEqual(ReconciliationStatus.Reconciled, copied.Credit.Status);
            Assert.AreEqual(ReconciliationStatus.Marked, copied.Fee!.Status);
            Assert.AreEqual(0.123m, copied.Fee.Amount);
            Assert.AreEqual(source.Snapshot.Accounts[3].Id, copied.Fee.AccountId);
            Assert.AreEqual(source.ReadReconciliation(session.Id).Session, restored.ReadReconciliation(session.Id).Session);
            CollectionAssert.AreEqual(source.ReadReconciliation(session.Id).Entries.ToArray(), restored.ReadReconciliation(session.Id).Entries.ToArray());
            CollectionAssert.AreEqual(source.ReadReconciliation(session.Id).Events.ToArray(), restored.ReadReconciliation(session.Id).Events.ToArray());
            Assert.IsTrue(restored.ReadTransfer(ids[2]).Value.IsDeleted);
        }
        using (var reopened = MonetaFile.Open(PathFor("restored"))) AssertComplete(source, reopened, ids);
        BackupRestoreTests.AssertCompleteSnapshot(expected, source.Snapshot);
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(backup));
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void SaveAsAppliesCompleteTransferEditsDeletionCreationAndZeroFeeOnlyToDestination()
    {
        using var source = CreateSource(out var ids);
        var untouched = source.ReadTransfer(ids[3]);
        var session = Validate(source, untouched.Credit);
        var originalFee = source.ReadTransfer(ids[1]).Fee!;
        source.Save(source.Snapshot, [new(originalFee with { Status = ReconciliationStatus.Marked,
            MarkedOn = Day.AddDays(1), Revision = originalFee.Revision + 1, ModifiedUtc = DateTimeOffset.UtcNow }, originalFee.Revision)]);
        var baseline = source.Snapshot;
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        var model = new MainViewModel(baseline, source);
        var fx = model.GetTransfer(ids[1]);
        var edited = model.EditTransfer(fx.Value.Id, fx.Value.Revision,
            new(fx.Debit.AccountId, fx.Credit.AccountId, Day.AddDays(1), 15.67m, 2300m,
                "Destination reference 日本", "Destination notes", Day, "Manually agreed rate", fx.Fee!.AccountId, 0m));
        model.DeleteTransfer(ids[0], model.GetTransfer(ids[0]).Value.Revision);
        var accounts = baseline.Accounts;
        var added = model.CreateTransfer(new(accounts[0].Id, accounts[1].Id, Day, 7.89m, 7.89m,
            "New two-leg reference", "New pending transfer"));
        var changes = model.PendingChanges.ToArray();
        var transfers = model.PendingTransfers.ToArray();
        FinancialDocument saved;
        var allIds = ids.Append(added.Value.Id).ToArray();
        using (var target = MonetaBackup.SaveAs(source, PathFor("copy"), model.CaptureDocument(), changes,
            model.PendingReconciliations, transfers))
        {
            saved = target.Snapshot;
            Assert.AreEqual(baseline.TransferCount + 1, saved.TransferCount);
            Assert.AreEqual(edited, target.ReadTransfer(edited.Value.Id));
            Assert.AreEqual(added, target.ReadTransfer(added.Value.Id));
            Assert.IsTrue(target.ReadTransfer(ids[0]).Value.IsDeleted);
            Assert.AreEqual(fx.Fee.Id, target.ReadTransfer(ids[1]).Fee!.Id);
            Assert.AreEqual(0m, target.ReadTransfer(ids[1]).Fee!.Amount);
            Assert.AreEqual(ReconciliationStatus.Marked, target.ReadTransfer(ids[1]).Fee!.Status);
            Assert.AreEqual(originalFee.Id, target.ReadTransfer(ids[1]).Fee!.Id);
            Assert.AreEqual(ReconciliationStatus.Unmarked, target.ReadTransfer(ids[3]).Debit.Status);
            Assert.AreEqual(ReconciliationStatus.Reconciled, target.ReadTransfer(ids[3]).Credit.Status);
            CollectionAssert.AreEqual(source.ReadReconciliation(session.Id).Entries.ToArray(), target.ReadReconciliation(session.Id).Entries.ToArray());
            var backup = MonetaBackup.Create(target, PathFor("copy-backup")).FilePath;
            using var restored = MonetaBackup.Restore(backup, PathFor("copy-restored"));
            AssertComplete(target, restored, allIds);
        }
        using (var reopened = MonetaFile.Open(PathFor("copy")))
        {
            BackupRestoreTests.AssertCompleteSnapshot(saved, reopened.Snapshot);
            Assert.AreEqual(edited, reopened.ReadTransfer(edited.Value.Id));
            Assert.AreEqual(added, reopened.ReadTransfer(added.Value.Id));
        }
        Assert.IsFalse(source.ReadTransfer(ids[0]).Value.IsDeleted);
        Assert.AreEqual(fx, source.ReadTransfer(ids[1]));
        BackupRestoreTests.AssertCompleteSnapshot(baseline, source.Snapshot);
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        CollectionAssert.AreEqual(changes, model.PendingChanges.ToArray());
        CollectionAssert.AreEqual(transfers, model.PendingTransfers.ToArray());
        Assert.IsTrue(model.IsDirty);
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void FailedTransferSaveAsPreservesAllDraftsSourceBytesAndUnrelatedRecoveryFiles()
    {
        using var source = CreateSource(out var ids);
        var baseline = source.Snapshot;
        var model = new MainViewModel(baseline, source);
        var original = model.GetTransfer(ids[0]);
        model.EditTransfer(original.Value.Id, original.Value.Revision,
            new(original.Debit.AccountId, original.Credit.AccountId, Day, 99m, 99m, original.Value.Reference, original.Value.Notes));
        var changes = model.PendingChanges.ToArray();
        var drafts = model.PendingTransfers.ToArray();
        var invalid = drafts.Select(change => change with { ExpectedRevision = 99 }).ToArray();
        var marker = new byte[] { 7, 11, 13 };
        var unrelated = PathFor("unrelated") + "-journal";
        File.WriteAllBytes(unrelated, marker);
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        Assert.ThrowsException<DocumentException>(() => MonetaBackup.SaveAs(source, PathFor("failed"),
            model.CaptureDocument(), changes, null, invalid));
        Assert.IsFalse(File.Exists(PathFor("failed")));
        Assert.AreEqual(original, source.ReadTransfer(original.Value.Id));
        BackupRestoreTests.AssertCompleteSnapshot(baseline, source.Snapshot);
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        CollectionAssert.AreEqual(marker, File.ReadAllBytes(unrelated));
        CollectionAssert.AreEqual(changes, model.PendingChanges.ToArray());
        CollectionAssert.AreEqual(drafts, model.PendingTransfers.ToArray());
        Assert.AreEqual(99L, invalid[0].ExpectedRevision);
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void TransferSaveAsNeverReplacesDestinationOrRecoveryCompanions()
    {
        using var source = CreateSource(out _);
        var model = new MainViewModel(source.Snapshot, source);
        model.CreateTransfer(new(source.Snapshot.Accounts[0].Id, source.Snapshot.Accounts[1].Id, Day, 1m, 1m));
        var marker = new byte[] { 2, 3, 5 };
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".lock" })
        {
            var target = PathFor(Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(target + suffix, marker);
            Assert.AreEqual(DocumentError.DestinationExists, Assert.ThrowsException<DocumentException>(() =>
                MonetaBackup.SaveAs(source, target, model.CaptureDocument(), model.PendingChanges,
                    model.PendingReconciliations, model.PendingTransfers)).Error);
            CollectionAssert.AreEqual(marker, File.ReadAllBytes(target + suffix));
            if (suffix.Length != 0) Assert.IsFalse(File.Exists(target));
        }
        Assert.AreEqual(1, model.PendingTransfers.Count);
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void MatchingFileHashCannotHideSwappedMirrorLinksWithIdenticalAmountsAndBalances()
    {
        using var source = CreateSource(out var ids);
        var first = source.ReadTransfer(ids[0]);
        var mirror = source.ReadTransfer(ids[3]);
        var backup = MonetaBackup.Create(source, PathFor("backup")).FilePath;
        SwapCreditLinks(backup, first, mirror);
        var tampered = MonetaFile.Inspect(backup);
        foreach (var (id, balance) in source.Snapshot.AccountBalances!) Assert.AreEqual(balance, tampered.AccountBalances![id]);
        Assert.AreEqual(source.Snapshot.TransferCount, tampered.TransferCount);
        Assert.AreNotEqual(source.Snapshot.LedgerDigest, tampered.LedgerDigest);
        RefreshFileHash(backup);
        var bytes = File.ReadAllBytes(backup);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.Restore(backup, PathFor("rejected"))).Error);
        Assert.IsFalse(File.Exists(PathFor("rejected")));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(backup));
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void StaleTransferLinksCannotBeCertifiedOrSavedAsEvenWhenEveryBalanceStillMatches()
    {
        using var source = CreateSource(out var ids);
        var baseline = source.Snapshot;
        SwapCreditLinks(source.FilePath, source.ReadTransfer(ids[0]), source.ReadTransfer(ids[3]));
        foreach (var (id, balance) in baseline.AccountBalances!)
            Assert.AreEqual(balance, MonetaFile.Inspect(source.FilePath).AccountBalances![id]);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.Create(source, PathFor("rejected-backup"))).Error);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.SaveAs(source, PathFor("rejected-copy"), baseline, [])).Error);
        Assert.IsFalse(File.Exists(PathFor("rejected-backup")));
        Assert.IsFalse(File.Exists(PathFor("rejected-backup") + ".manifest.json"));
        Assert.IsFalse(File.Exists(PathFor("rejected-copy")));
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void UncommittedPartialTransferWritesNeverEnterCertificationAndRollbackLeavesEveryLinkIntact()
    {
        using var source = CreateSource(out var ids);
        var baseline = source.Snapshot;
        string backup;
        using (var raw = Raw(source.FilePath))
        using (var transaction = raw.BeginTransaction())
        using (var command = raw.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TransferLegs WHERE transfer_id=$id AND role=1; UPDATE Transfers SET reference='Uncommitted partial change' WHERE transfer_id=$id;";
            command.Parameters.AddWithValue("$id", ids[0].ToString("D"));
            Assert.AreEqual(2, command.ExecuteNonQuery());
            backup = MonetaBackup.Create(source, PathFor("committed-only")).FilePath;
            using var restored = MonetaBackup.Restore(backup, PathFor("during-write"));
            AssertComplete(source, restored, ids);
            // Disposing this real write transaction without Commit rolls it back.
        }
        using var recovered = MonetaBackup.Restore(backup, PathFor("after-rollback"));
        AssertComplete(source, recovered, ids);
        BackupRestoreTests.AssertCompleteSnapshot(baseline, MonetaFile.Inspect(source.FilePath));
        AssertNoWorkFiles();
    }

    private MonetaFile CreateSource(out Guid[] ids)
    {
        var stamp = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var eur = new Currency("EUR", "Euro", 2);
        var jpy = new Currency("JPY", "Yen", 0);
        var kwd = new Currency("KWD", "Dinar", 3);
        var accounts = new[] { eur, eur, jpy, kwd }.Select((currency, index) =>
            new Account(Guid.NewGuid(), "Native account " + index, currency, 100m)
            { CreatedUtc = stamp, ModifiedUtc = stamp }).ToArray();
        var category = new LedgerCategory(Guid.NewGuid(), "Backup category");
        var payee = new LedgerPayee(Guid.NewGuid(), "Backup payee");
        var method = new PaymentMethod(Guid.NewGuid(), "Backup payment method");
        var metadata = new FinancialDocument(Guid.NewGuid(), "Transfer backup 日本", stamp, stamp, [eur, jpy, kwd], accounts)
        { SelectedAccountId = accounts[0].Id, Categories = [category], Payees = [payee], PaymentMethods = [method] };
        var model = new MainViewModel(metadata);
        var same = model.CreateTransfer(new(accounts[0].Id, accounts[1].Id, Day, 10m, 10m, "Mirror ref 日本", "Mirror notes"));
        var fx = model.CreateTransfer(new(accounts[0].Id, accounts[2].Id, Day, 12.34m, 1800m,
            "FX reference 日本", "FX notes", Day, "User declared nonverified", accounts[3].Id, 0.123m));
        var deleted = model.CreateTransfer(new(accounts[1].Id, accounts[0].Id, Day, 2m, 2m, "Deleted reference", "Deleted notes"));
        var mirrorDebit = same.Debit with { Id = Guid.NewGuid() };
        var mirrorCredit = same.Credit with { Id = Guid.NewGuid() };
        var mirror = same.Value with { Id = Guid.NewGuid(), DebitTransactionId = mirrorDebit.Id, CreditTransactionId = mirrorCredit.Id };
        var standalone = same.Debit with { Id = Guid.NewGuid(), Label = "Standalone catalog references",
            Category = new Category(category.Name), CategoryId = category.Id,
            Payee = new Payee(payee.Name), PayeeId = payee.Id, PaymentMode = method.Name, PaymentMethodId = method.Id };
        var changes = model.PendingChanges.Concat(new TransactionChange[] { new(mirrorDebit, 0), new(mirrorCredit, 0), new(standalone, 0) }).ToArray();
        var transfers = model.PendingTransfers.Append(new TransferChange(mirror, 0)).ToArray();
        var source = MonetaFile.Create(PathFor("source"), model.CaptureDocument(), changes, transfers);
        try
        {
            var persisted = new MainViewModel(source.Snapshot, source);
            persisted.DeleteTransfer(deleted.Value.Id, deleted.Value.Revision);
            source.Save(persisted.CaptureDocument(), persisted.PendingChanges, persisted.PendingReconciliations, persisted.PendingTransfers);
            ids = [same.Value.Id, fx.Value.Id, deleted.Value.Id, mirror.Id];
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    private static ReconciliationSession Validate(MonetaFile source, Transaction original)
    {
        var stamp = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var account = source.Snapshot.Accounts.Single(value => value.Id == original.AccountId);
        var candidates = source.ReadReconciliationCandidates(account, today, new HashSet<Guid> { original.Id }, 0, 100);
        var closing = candidates.OpeningBalance + FinancialRules.SignedAmount(original);
        var session = new ReconciliationSession(Guid.NewGuid(), account.Id, today, "Transfer statement", candidates.OpeningBalance,
            closing, closing, candidates.PreviousSessionId, stamp);
        var changed = original with { Status = ReconciliationStatus.Reconciled, MarkedOn = original.MarkedOn ?? today,
            ReconciliationId = session.Id, Revision = original.Revision + 1, ModifiedUtc = stamp };
        source.Save(source.Snapshot, [new(changed, original.Revision)],
            new([new(session, 0, [new(session.Id, original)])], [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Validated, stamp, "")]));
        return session;
    }

    private static void AssertComplete(MonetaFile expected, MonetaFile actual, IEnumerable<Guid> ids)
    {
        BackupRestoreTests.AssertCompleteSnapshot(expected.Snapshot, actual.Snapshot);
        foreach (var id in ids)
        {
            var transfer = expected.ReadTransfer(id);
            Assert.AreEqual(transfer, actual.ReadTransfer(id));
            foreach (var leg in new[] { transfer.Debit, transfer.Credit, transfer.Fee }.OfType<Transaction>())
            {
                Assert.AreEqual(leg, actual.GetTransaction(leg.Id, includeDeleted: true));
                Assert.AreEqual(id, actual.FindTransferId(leg.Id));
            }
        }
        foreach (var account in expected.Snapshot.Accounts)
        {
            var page = expected.ReadPage(account, 0, 100, []);
            var copied = actual.ReadPage(account, 0, 100, []);
            Assert.AreEqual(page.TotalCount, copied.TotalCount);
            CollectionAssert.AreEqual(page.Transactions.ToArray(), copied.Transactions.ToArray());
            CollectionAssert.AreEquivalent(page.TransferIds.ToArray(), copied.TransferIds.ToArray());
        }
    }

    private static void SwapCreditLinks(string path, TransferDetails first, TransferDetails second)
    {
        using var raw = Raw(path);
        using var transaction = raw.BeginTransaction();
        using var command = raw.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM TransferLegs WHERE role=1 AND transfer_id IN ($first,$second); INSERT INTO TransferLegs(transfer_id,role,transaction_id) VALUES($first,1,$secondCredit),($second,1,$firstCredit);";
        command.Parameters.AddWithValue("$first", first.Value.Id.ToString("D"));
        command.Parameters.AddWithValue("$second", second.Value.Id.ToString("D"));
        command.Parameters.AddWithValue("$firstCredit", first.Credit.Id.ToString("D"));
        command.Parameters.AddWithValue("$secondCredit", second.Credit.Id.ToString("D"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static byte[] LiveBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void RefreshFileHash(string path)
    {
        var json = JsonNode.Parse(File.ReadAllText(path + ".manifest.json"))!.AsObject();
        json["Sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        json["Length"] = new FileInfo(path).Length;
        File.WriteAllText(path + ".manifest.json", json.ToJsonString());
    }

    private void AssertNoWorkFiles() => Assert.AreEqual(0, Directory.GetFiles(directory, ".moneta-*.tmp*").Length);
}
