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
                ? account with { Notes = "Modification en WAL\nnon consolidée", BankName = "Banque fictive", ModifiedUtc = account.ModifiedUtc.AddHours(1) }
                : account).ToArray()
        };
        var accountId = original.Accounts[0].Id;
        var adjustment = new Transaction(Guid.NewGuid(), accountId, new DateOnly(2025, 4, 23),
            "Correction explicite", new Payee(""), new Category(""), 11111.10m, original.Currencies[0],
            TransactionDirection.Debit, ReconciliationStatus.Unmarked, "")
            { CreatedUtc = saved.ModifiedUtc, ModifiedUtc = saved.ModifiedUtc };
        source.Save(saved, [new(adjustment, 0)]);
        saved = source.Snapshot;
        Assert.AreEqual(-9876.54m, saved.AccountBalances![accountId]);
        Assert.IsTrue(File.Exists(source.FilePath + "-wal"), "The source must remain open in WAL mode.");
        Assert.IsTrue(new FileInfo(source.FilePath + "-wal").Length > 32, "Saved accounts must have live WAL frames.");
        var result = MonetaBackup.Create(source, PathFor("backup.moneta"));
        Assert.AreEqual(saved.Id, result.DocumentId);
        Assert.AreEqual(Sha(result.FilePath), result.Sha256);
        Assert.IsTrue(File.Exists(result.FilePath + ".manifest.json"));
        var backupBytes = File.ReadAllBytes(result.FilePath);
        using (var restored = MonetaBackup.Restore(result.FilePath, PathFor("restored.moneta")))
            AssertCompleteSnapshot(saved, restored.Snapshot);
        using (var reopened = MonetaFile.Open(PathFor("restored.moneta")))
            AssertCompleteSnapshot(saved, reopened.Snapshot);
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

    // Execute the actual old DDL; a current ledger schema with an old version tag
    // is not a legacy fixture and must be rejected by exact-schema validation.
    internal static void BuildLegacy(string path, FinancialDocument snapshot, int version = 1,
        IReadOnlyList<Transaction>? rows = null)
    {
        var schema = typeof(MonetaFile).GetMethod("SchemaSql",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = (string)schema.Invoke(null, [version])!;
        command.ExecuteNonQuery();
        command.CommandText = $"PRAGMA application_id={MonetaFile.ApplicationId}; PRAGMA user_version={version};";
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO Document(singleton,format,document_id,name,schema_version,min_reader,min_writer,creator_version,created_utc,modified_utc,selected_account_id)
            VALUES(1,'Moneta',$id,$name,$version,$version,$version,'Moneta-6',$created,$modified,$selected);
            INSERT INTO MigrationHistory(version,from_version,migration_id,definition,applied_utc)
            VALUES($version,0,$migration,$definition,$created);
            """;
        command.Parameters.AddWithValue("$id", snapshot.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", snapshot.Name);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$created", Stamp(snapshot.CreatedUtc));
        command.Parameters.AddWithValue("$modified", Stamp(snapshot.ModifiedUtc));
        command.Parameters.AddWithValue("$selected", (object?)snapshot.SelectedAccountId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$migration", $"create-v{version}");
        command.Parameters.AddWithValue("$definition", $"native-accounts-v{version}");
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        for (var index = 0; index < snapshot.Currencies.Count; index++)
        {
            var currency = snapshot.Currencies[index];
            command.CommandText = """
                INSERT INTO Currencies(code,name,decimal_places,symbol,culture_name,use_symbol,symbol_before_amount,ordinal)
                VALUES($code,$name,$places,$symbol,$culture,$use,$before,$ordinal);
                """;
            command.Parameters.AddWithValue("$code", currency.Code);
            command.Parameters.AddWithValue("$name", currency.Name);
            command.Parameters.AddWithValue("$places", currency.DecimalPlaces);
            command.Parameters.AddWithValue("$symbol", currency.Symbol);
            command.Parameters.AddWithValue("$culture", currency.DisplayRules.CultureName);
            command.Parameters.AddWithValue("$use", currency.DisplayRules.UseSymbol ? 1 : 0);
            command.Parameters.AddWithValue("$before", currency.DisplayRules.SymbolBeforeAmount ? 1 : 0);
            command.Parameters.AddWithValue("$ordinal", index);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
        }
        for (var index = 0; index < snapshot.Accounts.Count; index++)
        {
            var account = snapshot.Accounts[index];
            command.CommandText = version == 1
                ? "INSERT INTO Accounts(account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal) VALUES($id,$name,$type,$currency,$minor,$notes,$created,$modified,$ordinal);"
                : "INSERT INTO Accounts(account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal,is_archived) VALUES($id,$name,$type,$currency,$minor,$notes,$created,$modified,$ordinal,$archived);";
            command.Parameters.AddWithValue("$id", account.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", account.Name);
            command.Parameters.AddWithValue("$type", (object?)account.Type ?? DBNull.Value);
            command.Parameters.AddWithValue("$currency", account.Currency.Code);
            command.Parameters.AddWithValue("$minor", ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency));
            command.Parameters.AddWithValue("$notes", (object?)account.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", Stamp(account.CreatedUtc));
            command.Parameters.AddWithValue("$modified", Stamp(account.ModifiedUtc));
            command.Parameters.AddWithValue("$ordinal", index);
            if (version >= 2) command.Parameters.AddWithValue("$archived", account.IsArchived ? 1 : 0);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
        }
        if (version >= 3)
        {
            void Reference(string table, string column, Guid id, string name, int ordinal, Guid? parent = null)
            {
                command.CommandText = $"INSERT INTO {table}({column},name,name_key,ordinal" +
                    (table == "Categories" ? ",parent_id" : "") + ") VALUES($id,$name,$key,$ordinal" +
                    (table == "Categories" ? ",$parent" : "") + ");";
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$key", FinancialRules.NameKey(name));
                command.Parameters.AddWithValue("$ordinal", ordinal);
                if (table == "Categories") command.Parameters.AddWithValue("$parent", (object?)parent?.ToString("D") ?? DBNull.Value);
                command.ExecuteNonQuery();
                command.Parameters.Clear();
            }
            for (var index = 0; index < snapshot.Categories.Count; index++)
            {
                var value = snapshot.Categories[index];
                Reference("Categories", "category_id", value.Id, value.Name, index, value.ParentId);
            }
            for (var index = 0; index < snapshot.Payees.Count; index++)
            {
                var value = snapshot.Payees[index];
                Reference("Payees", "payee_id", value.Id, value.Name, index);
            }
            for (var index = 0; index < snapshot.PaymentMethods.Count; index++)
            {
                var value = snapshot.PaymentMethods[index];
                Reference("PaymentMethods", "payment_method_id", value.Id, value.Name, index);
            }
            foreach (var value in rows ?? Array.Empty<Transaction>())
            {
                command.CommandText = """
                    INSERT INTO Transactions(transaction_id,account_id,date,label,amount_minor,native_code,direction,status,notes,
                        payee_id,category_id,subcategory_id,payment_method_id,is_deleted,created_utc,modified_utc,revision)
                    VALUES($id,$account,$date,$label,$minor,$currency,$direction,$status,$notes,$payee,$category,$subcategory,$method,$deleted,$created,$modified,$revision);
                    """;
                if (version >= 4)
                {
                    command.CommandText = command.CommandText
                        .Replace("modified_utc,revision)", "modified_utc,revision,marked_on,reconciliation_id)", StringComparison.Ordinal)
                        .Replace("$modified,$revision)", "$modified,$revision,$marked,$reconciliation)", StringComparison.Ordinal);
                    command.Parameters.AddWithValue("$marked", (object?)value.MarkedOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? DBNull.Value);
                    command.Parameters.AddWithValue("$reconciliation", (object?)value.ReconciliationId?.ToString("D") ?? DBNull.Value);
                }
                command.Parameters.AddWithValue("$id", value.Id.ToString("D"));
                command.Parameters.AddWithValue("$account", value.AccountId.ToString("D"));
                command.Parameters.AddWithValue("$date", value.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$label", value.Label);
                command.Parameters.AddWithValue("$minor", ExactMoney.ToMinorUnits(value.Amount, value.Currency));
                command.Parameters.AddWithValue("$currency", value.Currency.Code);
                command.Parameters.AddWithValue("$direction", (int)value.Direction);
                command.Parameters.AddWithValue("$status", (int)value.Status);
                command.Parameters.AddWithValue("$notes", value.Details);
                command.Parameters.AddWithValue("$payee", (object?)value.PayeeId?.ToString("D") ?? DBNull.Value);
                command.Parameters.AddWithValue("$category", (object?)value.CategoryId?.ToString("D") ?? DBNull.Value);
                command.Parameters.AddWithValue("$subcategory", (object?)value.SubcategoryId?.ToString("D") ?? DBNull.Value);
                command.Parameters.AddWithValue("$method", (object?)value.PaymentMethodId?.ToString("D") ?? DBNull.Value);
                command.Parameters.AddWithValue("$deleted", value.IsDeleted ? 1 : 0);
                command.Parameters.AddWithValue("$created", Stamp(value.CreatedUtc));
                command.Parameters.AddWithValue("$modified", Stamp(value.ModifiedUtc));
                command.Parameters.AddWithValue("$revision", value.Revision);
                command.ExecuteNonQuery();
                command.Parameters.Clear();
            }
        }
        transaction.Commit();
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

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
            Assert.AreEqual(account.BankName, restored.BankName);
            Assert.AreEqual(account.BankReference, restored.BankReference);
            Assert.AreEqual(account.Holder, restored.Holder);
            Assert.AreEqual(account.IsArchived, restored.IsArchived);
            Assert.AreEqual(account.CreatedUtc, restored.CreatedUtc);
            Assert.AreEqual(account.ModifiedUtc, restored.ModifiedUtc);
        }
    }

    internal static void AssertCompleteSnapshot(FinancialDocument expected, FinancialDocument actual)
    {
        AssertSnapshot(expected, actual);
        Assert.AreEqual(expected.LedgerDigest, actual.LedgerDigest);
        Assert.AreEqual(expected.OperationCount, actual.OperationCount);
        Assert.AreEqual(expected.ReconciliationCount, actual.ReconciliationCount);
        Assert.AreEqual(expected.ReconciliationEventCount, actual.ReconciliationEventCount);
        Assert.AreEqual(expected.TransferCount, actual.TransferCount);
        CollectionAssert.AreEquivalent(expected.Categories.ToArray(), actual.Categories.ToArray());
        CollectionAssert.AreEquivalent(expected.Payees.ToArray(), actual.Payees.ToArray());
        CollectionAssert.AreEquivalent(expected.PaymentMethods.ToArray(), actual.PaymentMethods.ToArray());
        AssertBalances(expected.AccountBalances, actual.AccountBalances);
        AssertBalances(expected.AccountPointedBalances, actual.AccountPointedBalances);
    }

    private static void AssertBalances(IReadOnlyDictionary<Guid, decimal>? expected, IReadOnlyDictionary<Guid, decimal>? actual)
    {
        if (expected is null) { Assert.IsNull(actual); return; }
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Count, actual.Count);
        foreach (var (id, balance) in expected) Assert.AreEqual(balance, actual[id]);
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
