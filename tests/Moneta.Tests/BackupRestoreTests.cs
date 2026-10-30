using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class BackupRestoreTests
{
    private string _directory = null!;
    private string PathFor(string name) => Path.Combine(_directory, name);

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Moneta-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void HotWalBackupRestoresEverySavedValueWithoutClosingSource()
    {
        var original = Sample();
        using var source = MonetaFile.Create(PathFor("active.moneta"), original);
        var saved = original with
        {
            Name = "Dossier sauvegardé € 日本",
            ModifiedUtc = original.ModifiedUtc.AddDays(1),
            Accounts = original.Accounts.Select((account, index) => index == 0
                ? account with { Notes = "Modification en WAL\nnon consolidée", OpeningBalance = -9876.54m, ModifiedUtc = account.ModifiedUtc.AddHours(1) }
                : account).ToArray()
        };
        source.Save(saved);
        Assert.IsTrue(File.Exists(source.FilePath + "-wal"), "The source must remain open in WAL mode.");
        Assert.IsTrue(new FileInfo(source.FilePath + "-wal").Length > 32, "Saved accounts must have live WAL frames.");
        var result = MonetaBackup.Create(source, PathFor("backup.moneta"));
        Assert.AreEqual(saved.Id, result.DocumentId);
        Assert.AreEqual(Sha(result.FilePath), result.Sha256);
        Assert.IsTrue(File.Exists(result.FilePath + ".manifest.json"));
        var backupBytes = File.ReadAllBytes(result.FilePath);
        using (var restored = MonetaBackup.Restore(result.FilePath, PathFor("restored.moneta")))
            AssertSnapshot(saved, restored.Snapshot);
        using (var reopened = MonetaFile.Open(PathFor("restored.moneta")))
            AssertSnapshot(saved, reopened.Snapshot);
        CollectionAssert.AreEqual(backupBytes, File.ReadAllBytes(result.FilePath));
        AssertSnapshot(saved, source.Snapshot);
        AssertSnapshot(saved, MonetaFile.Inspect(source.FilePath));
    }

    [TestMethod]
    public void ActiveUncommittedWalWriterCannotLeakIntoCertifiedBackup()
    {
        var snapshot = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), snapshot);
        using (var writer = Raw(source.FilePath))
        using (var transaction = writer.BeginTransaction())
        {
            using var command = writer.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Accounts SET notes='still uncommitted', opening_minor=123456;";
            Assert.AreEqual(3, command.ExecuteNonQuery());
            var backup = MonetaBackup.Create(source, PathFor("backup.moneta"));
            using var restored = MonetaBackup.Restore(backup.FilePath, PathFor("restored.moneta"));
            AssertSnapshot(snapshot, restored.Snapshot);
        }
        AssertSnapshot(snapshot, MonetaFile.Inspect(source.FilePath));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void PinnedWalReaderDoesNotPreventBackupOfLatestCommit()
    {
        var original = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), original);
        var saved = original with
        {
            Name = "Committed after reader snapshot",
            ModifiedUtc = original.ModifiedUtc.AddMinutes(1),
            Accounts = original.Accounts.Select(account => account with { Notes = "New committed notes" }).ToArray()
        };
        using (var reader = Raw(source.FilePath))
        using (var transaction = reader.BeginTransaction(deferred: true))
        {
            using var command = reader.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM Document;";
            Assert.AreEqual(original.Name, command.ExecuteScalar());
            source.Save(saved);
            Assert.AreEqual(original.Name, command.ExecuteScalar(), "The WAL reader must remain pinned to the old commit.");
            var backup = MonetaBackup.Create(source, PathFor("backup.moneta"));
            using var restored = MonetaBackup.Restore(backup.FilePath, PathFor("restored.moneta"));
            AssertSnapshot(saved, restored.Snapshot);
            Assert.IsFalse(File.Exists(backup.FilePath + "-wal"));
            Assert.IsFalse(File.Exists(backup.FilePath + "-shm"));
        }
        AssertSnapshot(saved, MonetaFile.Inspect(source.FilePath));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void ManifestContainsCertificationButNoRawFinancialContent()
    {
        var snapshot = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), snapshot);
        var result = MonetaBackup.Create(source, PathFor("backup.moneta"));
        var text = File.ReadAllText(result.FilePath + ".manifest.json");
        var manifest = JsonNode.Parse(text)!.AsObject();
        Assert.AreEqual(snapshot.Id.ToString(), manifest["DocumentId"]!.GetValue<string>());
        Assert.AreEqual(3, manifest["CurrencyCount"]!.GetValue<int>());
        Assert.AreEqual(3, manifest["AccountCount"]!.GetValue<int>());
        Assert.AreEqual(new FileInfo(result.FilePath).Length, manifest["Length"]!.GetValue<long>());
        Assert.AreEqual(64, manifest["SnapshotDigest"]!.GetValue<string>().Length);
        Assert.IsFalse(text.Contains(snapshot.Name, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(snapshot.Accounts[0].Notes!, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("OpeningBalance", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingAndBrokenManifestNeverCreateRestoredDestination()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        var manifest = File.ReadAllText(backup + ".manifest.json");
        File.Delete(backup + ".manifest.json");
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("missing.moneta")));
        File.WriteAllText(backup + ".manifest.json", "{broken");
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("broken.moneta")));
        File.WriteAllText(backup + ".manifest.json", manifest);
        using var restored = MonetaBackup.Restore(backup, PathFor("valid.moneta"));
        AssertSnapshot(source.Snapshot, restored.Snapshot);
        Assert.IsFalse(File.Exists(PathFor("missing.moneta")));
        Assert.IsFalse(File.Exists(PathFor("broken.moneta")));
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow("Sha256")]
    [DataRow("SnapshotDigest")]
    [DataRow("Length")]
    [DataRow("DocumentId")]
    [DataRow("AccountCount")]
    [DataRow("CurrencyCount")]
    [DataRow("SchemaVersion")]
    public void TamperedCertificationIsRejected(string field)
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        var manifestPath = backup + ".manifest.json";
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        switch (field)
        {
            case "Sha256":
            case "SnapshotDigest": json[field] = new string('0', 64); break;
            case "Length": json[field] = json[field]!.GetValue<long>() + 1; break;
            case "DocumentId": json[field] = Guid.NewGuid().ToString(); break;
            default: json[field] = 999; break;
        }
        File.WriteAllText(manifestPath, json.ToJsonString());
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void TamperedDatabaseHashIsRejectedBeforeRestore()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        using (var stream = new FileStream(backup, FileMode.Append, FileAccess.Write)) stream.WriteByte(42);
        var before = File.ReadAllBytes(backup);
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(backup));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
    }

    [TestMethod]
    public void CorruptCertifiedSourceStillFailsDatabaseValidation()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        // A manifest hash is not authenticity: matching bytes must still be a valid Moneta database.
        var bytes = File.ReadAllBytes(backup);
        Array.Fill(bytes, (byte)0, 0, Math.Min(100, bytes.Length));
        File.WriteAllBytes(backup, bytes);
        var json = JsonNode.Parse(File.ReadAllText(backup + ".manifest.json"))!.AsObject();
        json["Sha256"] = Sha(backup);
        File.WriteAllText(backup + ".manifest.json", json.ToJsonString());
        var error = Assert.ThrowsException<DocumentException>(() => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        Assert.IsTrue(error.Error is DocumentError.Corrupt or DocumentError.InvalidFormat);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(backup));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void MatchingFileHashCannotHideChangedNotesFromFullSnapshotCertification()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        using (var connection = Raw(backup))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Accounts SET notes='changed certified content';";
            command.ExecuteNonQuery();
        }
        var json = JsonNode.Parse(File.ReadAllText(backup + ".manifest.json"))!.AsObject();
        json["Sha256"] = Sha(backup);
        json["Length"] = new FileInfo(backup).Length;
        File.WriteAllText(backup + ".manifest.json", json.ToJsonString());
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
        AssertSnapshot(source.Snapshot, MonetaFile.Inspect(source.FilePath));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void IncompleteManifestIsRejectedEvenWhenOmittedCountsWouldDefaultToZero()
    {
        using var source = MonetaFile.Create(PathFor("empty.moneta"), FinancialDocument.New("Sans comptes"));
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        var json = JsonNode.Parse(File.ReadAllText(backup + ".manifest.json"))!.AsObject();
        json.Remove("AccountCount");
        File.WriteAllText(backup + ".manifest.json", json.ToJsonString());
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
    }

    [TestMethod]
    public void ExistingDestinationsAndCompanionAreNeverReplaced()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        var occupied = PathFor("occupied.moneta");
        var marker = new byte[] { 5, 8, 13, 21 };
        File.WriteAllBytes(occupied, marker);
        ExpectError(DocumentError.DestinationExists, () => MonetaBackup.Create(source, occupied));
        ExpectError(DocumentError.DestinationExists, () => MonetaBackup.Restore(backup, occupied));
        ExpectError(DocumentError.DestinationExists, () => MonetaBackup.Migrate(source.FilePath, occupied));
        CollectionAssert.AreEqual(marker, File.ReadAllBytes(occupied));
        var companion = PathFor("reserved.moneta.manifest.json");
        File.WriteAllBytes(companion, marker);
        ExpectError(DocumentError.DestinationExists, () => MonetaBackup.Create(source, PathFor("reserved.moneta")));
        CollectionAssert.AreEqual(marker, File.ReadAllBytes(companion));
        Assert.IsFalse(File.Exists(PathFor("reserved.moneta")));
        foreach (var suffix in new[] { "-wal", "-shm", "-journal", ".lock" })
        {
            var destination = PathFor("with-companion.moneta");
            File.WriteAllBytes(destination + suffix, marker);
            ExpectError(DocumentError.DestinationExists, () => MonetaBackup.Restore(backup, destination));
            CollectionAssert.AreEqual(marker, File.ReadAllBytes(destination + suffix));
            Assert.IsFalse(File.Exists(destination));
            File.Delete(destination + suffix);
        }
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void PartialPublicationWithoutFinalManifestIsNotCertified()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        var backup = MonetaBackup.Create(source, PathFor("partial.moneta")).FilePath;
        // Model interruption between database publication and manifest publication.
        File.Move(backup + ".manifest.json", backup + ".manifest.unpublished");
        var before = File.ReadAllBytes(backup);
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Restore(backup, PathFor("restored.moneta")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(backup));
        AssertSnapshot(source.Snapshot, MonetaFile.Inspect(source.FilePath));
        Assert.IsFalse(File.Exists(PathFor("restored.moneta")));
    }

    [TestMethod]
    public void RealInterruptedTransactionRollsBackBeforeBackupAndRestore()
    {
        var snapshot = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), snapshot);
        using (var connection = Raw(source.FilePath))
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Accounts SET notes='uncommitted interruption', opening_minor=123456;";
            Assert.AreEqual(3, command.ExecuteNonQuery());
            // Disposing an uncommitted SQLite transaction is the real rollback path.
        }
        AssertSnapshot(snapshot, MonetaFile.Inspect(source.FilePath));
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta"));
        using var restored = MonetaBackup.Restore(backup.FilePath, PathFor("restored.moneta"));
        AssertSnapshot(snapshot, restored.Snapshot);
    }

    [TestMethod]
    public void StaleActiveSnapshotCannotCertifyExternallyChangedData()
    {
        using var source = MonetaFile.Create(PathFor("source.moneta"), Sample());
        using (var connection = Raw(source.FilePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Accounts SET notes='externally committed';";
            command.ExecuteNonQuery();
        }
        ExpectError(DocumentError.VerificationFailed, () => MonetaBackup.Create(source, PathFor("backup.moneta")));
        Assert.IsFalse(File.Exists(PathFor("backup.moneta")));
        Assert.IsFalse(File.Exists(PathFor("backup.moneta.manifest.json")));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void LegacyDraftMigratesByCopyPreservingOriginalBytesAndFullValues()
    {
        var expected = Sample(archived: false);
        var legacyPath = PathFor("draft-v1.moneta");
        BuildLegacy(legacyPath, expected);
        var originalBytes = File.ReadAllBytes(legacyPath);
        var originalHash = Sha(legacyPath);
        ExpectError(DocumentError.MigrationRequired, () => MonetaFile.Open(legacyPath));
        using (var migrated = MonetaBackup.Migrate(legacyPath, PathFor("migrated.moneta")))
            AssertSnapshot(expected, migrated.Snapshot);
        using (var reopened = MonetaFile.Open(PathFor("migrated.moneta")))
            AssertSnapshot(expected, reopened.Snapshot);
        Assert.AreEqual(originalHash, Sha(legacyPath));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(legacyPath));
        ExpectError(DocumentError.MigrationRequired, () => MonetaFile.Open(legacyPath));
        using var check = Raw(legacyPath, readOnly: true);
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(1L, query.ExecuteScalar());
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void CurrentAndFutureSchemaAreNotMigratedOrModified()
    {
        var current = PathFor("current.moneta");
        using (MonetaFile.Create(current, Sample())) { }
        var before = File.ReadAllBytes(current);
        ExpectError(DocumentError.InvalidData, () => MonetaBackup.Migrate(current, PathFor("unneeded.moneta")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(current));
        using (var connection = Raw(current))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Document SET schema_version=99,min_reader=99,min_writer=99; PRAGMA user_version=99;";
            command.ExecuteNonQuery();
        }
        before = File.ReadAllBytes(current);
        ExpectError(DocumentError.UnsupportedVersion, () => MonetaBackup.Migrate(current, PathFor("future.moneta")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(current));
        Assert.IsFalse(File.Exists(PathFor("unneeded.moneta")));
        Assert.IsFalse(File.Exists(PathFor("future.moneta")));
        AssertNoTemporaryFiles();
    }

    [TestMethod]
    public void MissingOrCorruptMigrationSourceLeavesNoDestination()
    {
        ExpectError(DocumentError.MissingFile, () => MonetaBackup.Migrate(PathFor("missing.moneta"), PathFor("destination.moneta")));
        var source = PathFor("corrupt.moneta");
        File.WriteAllText(source, "not SQLite or a Moneta folder");
        var before = File.ReadAllBytes(source);
        var error = Assert.ThrowsException<DocumentException>(() => MonetaBackup.Migrate(source, PathFor("destination.moneta")));
        Assert.IsTrue(error.Error is DocumentError.InvalidFormat or DocumentError.Corrupt);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(source));
        Assert.IsFalse(File.Exists(PathFor("destination.moneta")));
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SharingLockedSourceReportsLockedWithoutPublishingOrChangingBytes(bool migrate)
    {
        var sourcePath = PathFor("input.moneta");
        if (migrate)
            BuildLegacy(sourcePath, Sample(archived: false));
        else
        {
            using var source = MonetaFile.Create(PathFor("active.moneta"), Sample());
            MonetaBackup.Create(source, sourcePath);
        }
        var before = File.ReadAllBytes(sourcePath);
        var destination = PathFor("result.moneta");
        using (var locked = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
            ExpectError(DocumentError.Locked, () =>
            {
                using var result = migrate
                    ? MonetaBackup.Migrate(sourcePath, destination)
                    : MonetaBackup.Restore(sourcePath, destination);
            });
        CollectionAssert.AreEqual(before, File.ReadAllBytes(sourcePath));
        Assert.IsFalse(File.Exists(destination));
        AssertNoTemporaryFiles();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReadOnlySourceRestoresOrMigratesWithoutChangingItsBytes(bool migrate)
    {
        var expected = Sample(archived: !migrate);
        var sourcePath = PathFor("input.moneta");
        if (migrate)
            BuildLegacy(sourcePath, expected);
        else
        {
            using var source = MonetaFile.Create(PathFor("active.moneta"), expected);
            MonetaBackup.Create(source, sourcePath);
        }
        var before = File.ReadAllBytes(sourcePath);
        var attributes = File.GetAttributes(sourcePath);
        File.SetAttributes(sourcePath, attributes | FileAttributes.ReadOnly);
        try
        {
            using (var result = migrate
                ? MonetaBackup.Migrate(sourcePath, PathFor("result.moneta"))
                : MonetaBackup.Restore(sourcePath, PathFor("result.moneta")))
                AssertSnapshot(expected, result.Snapshot);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(sourcePath));
            Assert.IsTrue(File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly));
            AssertNoTemporaryFiles();
        }
        finally { File.SetAttributes(sourcePath, attributes); }
    }

    // Schema 1 is an internal draft, constructed from the actual current schema using
    // the inverse of its sole known migration, not a claimed historical release fixture.
    private static void BuildLegacy(string path, FinancialDocument snapshot)
    {
        using (MonetaFile.Create(path, snapshot)) { }
        using var connection = Raw(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE Accounts DROP COLUMN is_archived;
            UPDATE Document SET schema_version=1,min_reader=1,min_writer=1;
            PRAGMA user_version=1;
            DELETE FROM MigrationHistory;
            INSERT INTO MigrationHistory(version,from_version,migration_id,definition,applied_utc)
            VALUES(1,0,'create-v1','native-accounts-v1',$created);
            """;
        command.Parameters.AddWithValue("$created", snapshot.CreatedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Raw(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 3
        }.ToString());
        connection.Open();
        return connection;
    }

    private static FinancialDocument Sample(bool archived = true)
    {
        var stamp = new DateTimeOffset(2025, 4, 17, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567);
        var currencies = new[]
        {
            new Currency("EUR", "Euro", 2) { Symbol = "€", DisplayRules = new("fr-FR", true, false) },
            new Currency("JPY", "Yen japonais", 0) { Symbol = "¥", DisplayRules = new("ja-JP", true, true) },
            new Currency("KWD", "Dinar koweïtien", 3) { Symbol = "د.ك", DisplayRules = new("en-GB", false, true) }
        };
        var accounts = new[]
        {
            new Account(Guid.NewGuid(), "Compte courant €", currencies[0], 1234.56m)
                { Type = "Courant", Notes = "Notes exactes\naccents é 日本", CreatedUtc = stamp, ModifiedUtc = stamp.AddHours(2) },
            new Account(Guid.NewGuid(), "Épargne yen", currencies[1], -123456789m)
                { Type = null, Notes = "", IsArchived = archived, CreatedUtc = stamp.AddDays(1), ModifiedUtc = stamp.AddDays(2) },
            new Account(Guid.NewGuid(), "Compte trois décimales", currencies[2], 9223372036854775.807m)
                { Type = "Épargne", Notes = null, CreatedUtc = stamp.AddDays(3), ModifiedUtc = stamp.AddDays(4) }
        };
        return new FinancialDocument(Guid.NewGuid(), "Dossier privé très distinctif", stamp, stamp.AddDays(5), currencies, accounts)
            { SelectedAccountId = accounts[0].Id };
    }

    private static void AssertSnapshot(FinancialDocument expected, FinancialDocument actual)
    {
        Assert.AreEqual(expected.SchemaVersion, actual.SchemaVersion);
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.CreatedUtc, actual.CreatedUtc);
        Assert.AreEqual(expected.ModifiedUtc, actual.ModifiedUtc);
        Assert.AreEqual(expected.SelectedAccountId, actual.SelectedAccountId);
        Assert.AreEqual(expected.Currencies.Count, actual.Currencies.Count);
        CollectionAssert.AreEqual(expected.Currencies.Select(currency => currency.Code).ToArray(), actual.Currencies.Select(currency => currency.Code).ToArray());
        foreach (var currency in expected.Currencies)
            Assert.AreEqual(currency, actual.Currencies.Single(value => value.Code == currency.Code));
        Assert.AreEqual(expected.Accounts.Count, actual.Accounts.Count);
        CollectionAssert.AreEqual(expected.Accounts.Select(account => account.Id).ToArray(), actual.Accounts.Select(account => account.Id).ToArray());
        foreach (var account in expected.Accounts)
        {
            var restored = actual.Accounts.Single(value => value.Id == account.Id);
            Assert.AreEqual(account.Name, restored.Name);
            Assert.AreEqual(account.Currency, restored.Currency);
            Assert.AreEqual(account.OpeningBalance, restored.OpeningBalance);
            Assert.AreEqual(account.Type, restored.Type);
            Assert.AreEqual(account.Notes, restored.Notes);
            Assert.AreEqual(account.IsArchived, restored.IsArchived);
            Assert.AreEqual(account.CreatedUtc, restored.CreatedUtc);
            Assert.AreEqual(account.ModifiedUtc, restored.ModifiedUtc);
        }
    }

    private static void ExpectError(DocumentError expected, Action action) =>
        Assert.AreEqual(expected, Assert.ThrowsException<DocumentException>(action).Error);

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void AssertNoTemporaryFiles() => Assert.AreEqual(0, Directory.GetFiles(_directory, ".moneta-*.tmp*").Length);
}
