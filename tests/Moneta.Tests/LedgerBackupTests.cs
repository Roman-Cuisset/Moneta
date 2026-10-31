using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class LedgerBackupTests
{
    private string directory = null!;
    private string PathFor(string name) => Path.Combine(directory, name);

    private static byte[] ReadLiveBytes(string path)
    {
        // Observation in this single-writer test, not a backup mechanism.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "Moneta-ledger-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void CertifiedHotBackupRestoresEveryFinancialFieldIncludingTombstonesAndReferences()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var tombstone = rows[2] with { IsDeleted = true, Revision = 2, ModifiedUtc = rows[2].ModifiedUtc.AddDays(1) };
        source.Save(source.Snapshot, [new TransactionChange(tombstone, 1)]);
        rows[2] = tombstone;
        var saved = source.Snapshot;
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta"));
        var manifest = File.ReadAllText(backup.FilePath + ".manifest.json");
        foreach (var privateValue in new[] { rows[0].Label, rows[0].Details, metadata.Payees[0].Name,
                     metadata.Categories[0].Name, metadata.PaymentMethods[0].Name })
            Assert.IsFalse(manifest.Contains(privateValue, StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(source.FilePath + "-wal"));
        using (var restored = MonetaBackup.Restore(backup.FilePath, PathFor("restored.moneta")))
        {
            AssertLedger(saved, rows, restored);
            Assert.IsNull(restored.GetTransaction(tombstone.Id));
            var page = restored.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>());
            Assert.AreEqual(3L, page.TotalCount);
            Assert.AreEqual(100m, page.AccountBalance);
        }
        using var reopened = MonetaFile.Open(PathFor("restored.moneta"));
        AssertLedger(saved, rows, reopened);
        AssertLedger(saved, rows, source);
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void SaveAsCopiesUnchangedRowsAndAppliesAllPendingKindsOnlyToDestination()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        source.Save(source.Snapshot);
        var baseline = source.Snapshot;
        var databaseBytes = ReadLiveBytes(source.FilePath);
        var walBytes = ReadLiveBytes(source.FilePath + "-wal");
        var edit = rows[0] with { Label = "Edited only in destination", Amount = 13.25m, Revision = 2,
            ModifiedUtc = rows[0].ModifiedUtc.AddDays(1) };
        var deleted = rows[1] with { IsDeleted = true, Revision = 2, ModifiedUtc = rows[1].ModifiedUtc.AddDays(1) };
        var newPayee = new LedgerPayee(Guid.NewGuid(), "Destination-only payee");
        var added = rows[0] with { Id = Guid.NewGuid(), Label = "Pending new row", Amount = 8m,
            PayeeId = newPayee.Id, Payee = new Payee(newPayee.Name) };
        var pending = new[] { new TransactionChange(edit, 1), new TransactionChange(deleted, 1), new TransactionChange(added, 0) };
        var renamed = baseline with { Name = "Destination metadata", ModifiedUtc = baseline.ModifiedUtc.AddDays(2),
            Payees = baseline.Payees.Append(newPayee).ToArray() };
        FinancialDocument saved;
        var expectedRows = new[] { edit, deleted, rows[2], rows[3], rows[4], added };
        using (var target = MonetaBackup.SaveAs(source, PathFor("copy.moneta"), renamed, pending))
        {
            saved = target.Snapshot;
            Assert.AreEqual(renamed.Name, saved.Name);
            Assert.AreEqual(6L, saved.OperationCount);
            Assert.AreNotEqual(baseline.LedgerDigest, saved.LedgerDigest);
            AssertLedger(saved, expectedRows, target);
        }
        using (var reopened = MonetaFile.Open(PathFor("copy.moneta"))) AssertLedger(saved, expectedRows, reopened);
        AssertLedger(baseline, rows, source);
        CollectionAssert.AreEqual(databaseBytes, ReadLiveBytes(source.FilePath));
        CollectionAssert.AreEqual(walBytes, ReadLiveBytes(source.FilePath + "-wal"));
        Assert.AreEqual(3, pending.Length, "SaveAs must not mutate or consume the caller's drafts.");
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void FailedSaveAsLeavesSourceAndPendingIntactAndCleansOnlyOwnedWorkFiles()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        source.Save(source.Snapshot);
        var baseline = source.Snapshot;
        var marker = new byte[] { 13, 21, 34 };
        var unrelatedJournal = PathFor("unrelated.moneta-journal");
        File.WriteAllBytes(unrelatedJournal, marker);
        var invalid = new[] { new TransactionChange(rows[0] with { Label = "Stale edit", Revision = 10 }, 9) };
        Assert.ThrowsException<DocumentException>(() => MonetaBackup.SaveAs(source, PathFor("failed.moneta"), baseline, invalid));
        Assert.IsFalse(File.Exists(PathFor("failed.moneta")));
        AssertLedger(baseline, rows, source);
        Assert.IsTrue(File.Exists(source.FilePath + "-wal"));
        CollectionAssert.AreEqual(marker, File.ReadAllBytes(unrelatedJournal));
        Assert.AreEqual(9L, invalid[0].ExpectedRevision);
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void SaveAsNeverOverwritesDestinationOrRecoveryCompanions()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var marker = new byte[] { 3, 1, 4 };
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".lock" })
        {
            var destination = PathFor("occupied" + Guid.NewGuid().ToString("N") + ".moneta");
            File.WriteAllBytes(destination + suffix, marker);
            var error = Assert.ThrowsException<DocumentException>(() =>
                MonetaBackup.SaveAs(source, destination, source.Snapshot, Array.Empty<TransactionChange>()));
            Assert.AreEqual(DocumentError.DestinationExists, error.Error);
            CollectionAssert.AreEqual(marker, File.ReadAllBytes(destination + suffix));
        }
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void StaleLedgerSessionCannotCertifyOrSaveAsExternallyChangedRows()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        using (var connection = Raw(source.FilePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Transactions SET notes='External row change';";
            Assert.AreEqual(rows.Length, command.ExecuteNonQuery());
        }
        var backupError = Assert.ThrowsException<DocumentException>(() => MonetaBackup.Create(source, PathFor("backup.moneta")));
        Assert.AreEqual(DocumentError.VerificationFailed, backupError.Error);
        var copyError = Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.SaveAs(source, PathFor("copy.moneta"), source.Snapshot, Array.Empty<TransactionChange>()));
        Assert.AreEqual(DocumentError.VerificationFailed, copyError.Error);
        Assert.IsFalse(File.Exists(PathFor("backup.moneta")));
        Assert.IsFalse(File.Exists(PathFor("backup.moneta.manifest.json")));
        Assert.IsFalse(File.Exists(PathFor("copy.moneta")));
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow("equal-balance")]
    [DataRow("deleted-notes")]
    [DataRow("references")]
    public void MatchingFileHashCannotHideDifferentLedgerEvenWithEqualFinalBalance(string change)
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var deleted = rows[2] with { IsDeleted = true, Revision = 2, ModifiedUtc = rows[2].ModifiedUtc.AddDays(1) };
        source.Save(source.Snapshot, [new TransactionChange(deleted, 1)]);
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        using (var connection = Raw(backup))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = change switch
            {
                "equal-balance" => "UPDATE Transactions SET amount_minor=amount_minor+100 WHERE transaction_id IN ($first,$second);",
                "deleted-notes" => "UPDATE Transactions SET notes='Changed deleted notes' WHERE transaction_id=$deleted;",
                _ => "UPDATE Transactions SET category_id=NULL,subcategory_id=NULL WHERE transaction_id=$first;"
            };
            command.Parameters.AddWithValue("$first", rows[0].Id.ToString("D"));
            command.Parameters.AddWithValue("$second", rows[1].Id.ToString("D"));
            command.Parameters.AddWithValue("$deleted", deleted.Id.ToString("D"));
            command.ExecuteNonQuery();
        }
        var tampered = MonetaFile.Inspect(backup);
        Assert.AreEqual(source.Snapshot.AccountBalances![metadata.Accounts[0].Id], tampered.AccountBalances![metadata.Accounts[0].Id]);
        Assert.AreNotEqual(source.Snapshot.LedgerDigest, tampered.LedgerDigest);
        RefreshFileCertificate(backup);
        var bytes = File.ReadAllBytes(backup);
        var error = Assert.ThrowsException<DocumentException>(() => MonetaBackup.Restore(backup, PathFor("rejected.moneta")));
        Assert.AreEqual(DocumentError.VerificationFailed, error.Error);
        Assert.IsFalse(File.Exists(PathFor("rejected.moneta")));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(backup));
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ActualLegacySchemasMigrateFullMetadataWithoutChangingReadOnlyOriginal(int version)
    {
        var (metadata, _) = Sample();
        metadata = metadata with { Categories = Array.Empty<LedgerCategory>(), Payees = Array.Empty<LedgerPayee>(),
            PaymentMethods = Array.Empty<PaymentMethod>(), Accounts = metadata.Accounts.Select(account =>
                account with { Notes = "Old financial notes 日本", IsArchived = version == 2 }).ToArray() };
        var source = PathFor("legacy.moneta");
        BackupRestoreTests.BuildLegacy(source, metadata, version);
        var original = File.ReadAllBytes(source);
        var attributes = File.GetAttributes(source);
        File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
        try
        {
            using var migrated = MonetaBackup.Migrate(source, PathFor("migrated.moneta"));
            AssertLegacyValues(metadata, migrated.Snapshot);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(source));
            Assert.IsTrue(File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly));
        }
        finally { File.SetAttributes(source, attributes); }
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void CertifiedLegacyBackupUsesOriginalDigestThenUpgradesOnlyRestoredWorkCopy(int version)
    {
        var (metadata, _) = Sample();
        metadata = metadata with { SchemaVersion = version, Categories = Array.Empty<LedgerCategory>(),
            Payees = Array.Empty<LedgerPayee>(), PaymentMethods = Array.Empty<PaymentMethod>(),
            Accounts = metadata.Accounts.Select(account => account with { IsArchived = version == 2 }).ToArray() };
        var backup = PathFor("certified-old.moneta");
        BackupRestoreTests.BuildLegacy(backup, metadata, version);
        File.WriteAllText(backup + ".manifest.json", JsonSerializer.Serialize(new
        {
            ManifestVersion = 1, Sha256 = Sha(backup), Length = new FileInfo(backup).Length,
            SchemaVersion = version, DocumentId = metadata.Id, SnapshotDigest = LegacyDigest(metadata),
            CurrencyCount = metadata.Currencies.Count, AccountCount = metadata.Accounts.Count
        }));
        var original = File.ReadAllBytes(backup);
        using (var restored = MonetaBackup.Restore(backup, PathFor("restored.moneta"))) AssertLegacyValues(metadata, restored.Snapshot);
        using (var reopened = MonetaFile.Open(PathFor("restored.moneta"))) AssertLegacyValues(metadata, reopened.Snapshot);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(backup));
        var error = Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(backup));
        Assert.AreEqual(DocumentError.MigrationRequired, error.Error);
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void CertifiedFinancialBackupRecoversAfterOriginalIsLostOrCorrupt()
    {
        var (metadata, rows) = Sample();
        FinancialDocument saved;
        string backup;
        var original = PathFor("source.moneta");
        using (var source = MonetaFile.Create(original, metadata, Inserts(rows)))
        {
            saved = source.Snapshot;
            backup = MonetaBackup.Create(source, PathFor("recovery.moneta")).FilePath;
        }
        File.WriteAllText(original, "damaged original; never consulted by restore");
        var damaged = File.ReadAllBytes(original);
        using var recovered = MonetaBackup.Restore(backup, PathFor("recovered.moneta"));
        AssertLedger(saved, rows, recovered);
        CollectionAssert.AreEqual(damaged, File.ReadAllBytes(original));
        AssertNoTemporaryFiles();
    }

    private static (FinancialDocument Metadata, Transaction[] Rows) Sample()
    {
        var stamp = new DateTimeOffset(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
        var eur = new Currency("EUR", "Euro", 2);
        var jpy = new Currency("JPY", "Yen", 0);
        var accounts = new[]
        {
            new Account(Guid.NewGuid(), "Euro account", eur, 100m) { CreatedUtc = stamp, ModifiedUtc = stamp },
            new Account(Guid.NewGuid(), "Yen account", jpy, -30m) { CreatedUtc = stamp, ModifiedUtc = stamp }
        };
        var category = new LedgerCategory(Guid.NewGuid(), "Private category");
        var subcategory = new LedgerCategory(Guid.NewGuid(), "Private subcategory", category.Id);
        var payee = new LedgerPayee(Guid.NewGuid(), "Private payee 日本");
        var method = new PaymentMethod(Guid.NewGuid(), "Private payment mode");
        var metadata = new FinancialDocument(Guid.NewGuid(), "Private financial dossier", stamp, stamp.AddDays(1), [eur, jpy], accounts)
        {
            SelectedAccountId = accounts[0].Id, Categories = [category, subcategory], Payees = [payee], PaymentMethods = [method]
        };
        Transaction Row(Account account, decimal amount, TransactionDirection direction, ReconciliationStatus status) =>
            new(Guid.NewGuid(), account.Id, new DateOnly(2025, 7, 8), "Private operation label", new Payee(payee.Name),
                new Category(category.Name) { Subcategory = subcategory.Name }, amount, account.Currency, direction, status, "Private multiline notes\n日本 €")
            {
                PayeeId = payee.Id, CategoryId = category.Id, SubcategoryId = subcategory.Id,
                PaymentMethodId = method.Id, PaymentMode = method.Name, CreatedUtc = stamp, ModifiedUtc = stamp
            };
        var first = Row(accounts[0], 12.25m, TransactionDirection.Debit, ReconciliationStatus.Marked);
        return (metadata,
        [
            first,
            Row(accounts[0], 24.50m, TransactionDirection.Credit, ReconciliationStatus.Reconciled),
            Row(accounts[0], 3m, TransactionDirection.Debit, ReconciliationStatus.Unmarked),
            first with { Id = Guid.NewGuid() }, // Identical legitimate operations are not deduplicated.
            Row(accounts[1], 150m, TransactionDirection.Credit, ReconciliationStatus.Unmarked)
        ]);
    }

    private static TransactionChange[] Inserts(IEnumerable<Transaction> rows) => rows.Select(row => new TransactionChange(row, 0)).ToArray();

    private static void AssertLedger(FinancialDocument expected, IEnumerable<Transaction> rows, MonetaFile actual)
    {
        Assert.AreEqual(expected.Id, actual.Snapshot.Id);
        Assert.AreEqual(expected.Name, actual.Snapshot.Name);
        Assert.AreEqual(expected.LedgerDigest, actual.Snapshot.LedgerDigest);
        Assert.AreEqual(expected.OperationCount, actual.Snapshot.OperationCount);
        CollectionAssert.AreEquivalent(expected.Categories.ToArray(), actual.Snapshot.Categories.ToArray());
        CollectionAssert.AreEquivalent(expected.Payees.ToArray(), actual.Snapshot.Payees.ToArray());
        CollectionAssert.AreEquivalent(expected.PaymentMethods.ToArray(), actual.Snapshot.PaymentMethods.ToArray());
        foreach (var balance in expected.AccountBalances!) Assert.AreEqual(balance.Value, actual.Snapshot.AccountBalances![balance.Key]);
        foreach (var row in rows) Assert.AreEqual(row, actual.GetTransaction(row.Id, includeDeleted: true));
    }

    private static void AssertLegacyValues(FinancialDocument expected, FinancialDocument actual)
    {
        Assert.AreEqual(MonetaFile.CurrentSchemaVersion, actual.SchemaVersion);
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.CreatedUtc, actual.CreatedUtc);
        Assert.AreEqual(expected.ModifiedUtc, actual.ModifiedUtc);
        Assert.AreEqual(expected.SelectedAccountId, actual.SelectedAccountId);
        CollectionAssert.AreEqual(expected.Currencies.ToArray(), actual.Currencies.ToArray());
        CollectionAssert.AreEqual(expected.Accounts.ToArray(), actual.Accounts.ToArray());
        Assert.AreEqual(0L, actual.OperationCount);
        Assert.IsNotNull(actual.LedgerDigest);
        Assert.AreEqual(0, actual.Categories.Count);
        Assert.AreEqual(0, actual.Payees.Count);
        Assert.AreEqual(0, actual.PaymentMethods.Count);
        foreach (var account in expected.Accounts) Assert.AreEqual(account.OpeningBalance, actual.AccountBalances![account.Id]);
    }

    // Frozen schema-1/2 certificate algorithm, independent of the implementation under test.
    private static string LegacyDigest(FinancialDocument snapshot)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", snapshot.SchemaVersion);
            writer.WriteString("id", snapshot.Id);
            writer.WriteString("name", snapshot.Name);
            writer.WriteString("created", snapshot.CreatedUtc.ToUniversalTime());
            writer.WriteString("modified", snapshot.ModifiedUtc.ToUniversalTime());
            if (snapshot.SelectedAccountId is { } selected) writer.WriteString("selected", selected);
            else writer.WriteNull("selected");
            writer.WriteStartArray("currencies");
            foreach (var currency in snapshot.Currencies)
            {
                writer.WriteStartObject();
                writer.WriteString("code", currency.Code);
                writer.WriteString("name", currency.Name);
                writer.WriteNumber("decimals", currency.DecimalPlaces);
                writer.WriteString("symbol", currency.Symbol);
                writer.WriteString("culture", currency.DisplayRules.CultureName);
                writer.WriteBoolean("useSymbol", currency.DisplayRules.UseSymbol);
                writer.WriteBoolean("symbolBefore", currency.DisplayRules.SymbolBeforeAmount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("accounts");
            foreach (var account in snapshot.Accounts)
            {
                writer.WriteStartObject();
                writer.WriteString("id", account.Id);
                writer.WriteString("name", account.Name);
                writer.WriteString("currency", account.Currency.Code);
                writer.WriteNumber("openingMinor", ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency));
                writer.WriteString("type", account.Type);
                writer.WriteString("notes", account.Notes);
                writer.WriteBoolean("archived", account.IsArchived);
                writer.WriteString("created", account.CreatedUtc.ToUniversalTime());
                writer.WriteString("modified", account.ModifiedUtc.ToUniversalTime());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RefreshFileCertificate(string backup)
    {
        var path = backup + ".manifest.json";
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest["Sha256"] = Sha(backup);
        manifest["Length"] = new FileInfo(backup).Length;
        File.WriteAllText(path, manifest.ToJsonString());
    }

    private void AssertNoTemporaryFiles() => Assert.AreEqual(0, Directory.GetFiles(directory, ".moneta-*.tmp*").Length);
}
