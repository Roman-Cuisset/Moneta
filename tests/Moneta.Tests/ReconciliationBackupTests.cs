using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class ReconciliationBackupTests
{
    private string directory = null!;
    private static readonly DateTimeOffset Created = new(2025, 1, 2, 10, 0, 0, TimeSpan.Zero);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private string PathFor(string name) => Path.Combine(directory, name);

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "Moneta-reconciliation-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [TestMethod]
    public void CertifiedWalRoundTripPreservesValidatedReopenedAndRevalidatedHistoryInThreeNativeCurrencies()
    {
        var (metadata, rows) = Sample(137);
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var first = Validate(source, rows[0]);
        Reopen(source, first, rows[0]);
        var again = Validate(source, source.GetTransaction(rows[0].Id)!);
        var yen = Validate(source, rows[1]);
        var dinar = Validate(source, rows[2]);
        Assert.AreEqual(4L, source.Snapshot.ReconciliationCount);
        Assert.AreEqual(5L, source.Snapshot.ReconciliationEventCount);
        Assert.IsTrue(File.Exists(source.FilePath + "-wal"));
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        var certified = File.ReadAllBytes(backup);
        var attributes = File.GetAttributes(backup);
        File.SetAttributes(backup, attributes | FileAttributes.ReadOnly);
        try
        {
            using (var restored = MonetaBackup.Restore(backup, PathFor("restored.moneta")))
                AssertComplete(source, restored, rows, [first.Id, again.Id, yen.Id, dinar.Id]);
            using var reopened = MonetaFile.Open(PathFor("restored.moneta"));
            AssertComplete(source, reopened, rows, [first.Id, again.Id, yen.Id, dinar.Id]);
            Assert.AreEqual(ReconciliationSessionState.Reopened, reopened.ReadReconciliation(first.Id).Session.State);
            Assert.AreEqual(rows[0], reopened.ReadReconciliation(first.Id).Entries.Single().Original);
            CollectionAssert.AreEqual(certified, File.ReadAllBytes(backup));
            Assert.IsTrue(File.GetAttributes(backup).HasFlag(FileAttributes.ReadOnly));
        }
        finally { File.SetAttributes(backup, attributes); }
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        AssertNoWorkFiles();
    }

    [DataTestMethod]
    [DataRow("session")]
    [DataRow("event")]
    [DataRow("entry")]
    public void EqualBalanceHistoryTamperCannotBeCertifiedByRefreshingOnlyFileHash(string field)
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var session = Validate(source, rows[0]);
        Reopen(source, session, rows[0]);
        var backup = MonetaBackup.Create(source, PathFor("backup.moneta")).FilePath;
        using (var raw = Raw(backup))
        using (var command = raw.CreateCommand())
        {
            command.CommandText = field switch
            {
                "session" => "UPDATE Reconciliations SET statement_number='Changed statement';",
                "event" => "UPDATE ReconciliationHistory SET event_id=$id WHERE kind=0;",
                _ => "UPDATE ReconciliationEntries SET snapshot_json=replace(snapshot_json,'Native 0','Historic 0');"
            };
            if (field == "event") command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }
        var tampered = MonetaFile.Inspect(backup);
        Assert.AreEqual(source.Snapshot.AccountBalances![rows[0].AccountId], tampered.AccountBalances![rows[0].AccountId]);
        Assert.AreNotEqual(source.Snapshot.LedgerDigest, tampered.LedgerDigest);
        RefreshFileHash(backup);
        var bytes = File.ReadAllBytes(backup);
        var error = Assert.ThrowsException<DocumentException>(() => MonetaBackup.Restore(backup, PathFor("rejected.moneta")));
        Assert.AreEqual(DocumentError.VerificationFailed, error.Error);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(backup));
        Assert.IsFalse(File.Exists(PathFor("rejected.moneta")));
        AssertNoWorkFiles();
    }

    [DataTestMethod]
    [DataRow("validate")]
    [DataRow("reopen")]
    [DataRow("release")]
    public void SaveAsAppliesPendingReconciliationOnlyToCopiedTargetAndRetainsUnshownRowsAndHistory(string action)
    {
        var (metadata, rows) = Sample(137);
        if (action == "release") rows[0] = rows[0] with { Status = ReconciliationStatus.Reconciled, MarkedOn = null };
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        ReconciliationBatch batch;
        TransactionChange[] changes;
        Guid? sessionId = null;
        if (action == "reopen")
        {
            var session = Validate(source, rows[0]);
            sessionId = session.Id;
            (changes, batch) = Reopening(source, session, rows[0]);
        }
        else if (action == "release")
        {
            var stamp = DateTimeOffset.UtcNow;
            changes = [new(rows[0] with { Status = ReconciliationStatus.Unmarked, MarkedOn = null,
                Revision = 2, ModifiedUtc = stamp }, 1)];
            batch = new([], [new(Guid.NewGuid(), null, rows[0].Id, ReconciliationEventKind.LegacyReleased,
                stamp, "Legacy statement unavailable") { OperationBefore = rows[0] }]);
        }
        else
        {
            (changes, batch) = Validation(source, rows[0]);
            sessionId = batch.Sessions.Single().Value.Id;
        }
        // A different account's prior session is not on the current ledger page.
        var untouched = Validate(source, rows[1]);
        var baseline = source.Snapshot;
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        using (var target = MonetaBackup.SaveAs(source, PathFor("copy.moneta"), baseline with { Name = "Copied draft" }, changes, batch))
        {
            Assert.AreEqual(changes[0].Value, target.GetTransaction(rows[0].Id));
            Assert.AreEqual(baseline.OperationCount, target.Snapshot.OperationCount);
            AssertDetails(source.ReadReconciliation(untouched.Id), target.ReadReconciliation(untouched.Id));
            foreach (var row in rows.Skip(3)) Assert.AreEqual(row, target.GetTransaction(row.Id));
            Assert.AreEqual(baseline.ReconciliationEventCount + 1, target.Snapshot.ReconciliationEventCount);
            if (sessionId is { } id)
                Assert.AreEqual(action == "reopen" ? ReconciliationSessionState.Reopened : ReconciliationSessionState.Validated,
                    target.ReadReconciliation(id).Session.State);
            if (action == "release")
            {
                var backup = MonetaBackup.Create(target, PathFor("released.moneta")).FilePath;
                using var restored = MonetaBackup.Restore(backup, PathFor("released-restored.moneta"));
                Assert.AreEqual(target.Snapshot.LedgerDigest, restored.Snapshot.LedgerDigest);
                Assert.AreEqual(changes[0].Value, restored.GetTransaction(rows[0].Id));
            }
        }
        Assert.AreSame(baseline, source.Snapshot);
        Assert.AreEqual(action == "reopen" ? ReconciliationStatus.Reconciled : rows[0].Status, source.GetTransaction(rows[0].Id)!.Status);
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        Assert.AreEqual(1, changes.Length);
        Assert.AreEqual(1, batch.Events.Count);
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void FailedPendingSaveAsPreservesSourceHistoryDraftAndWal()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var session = Validate(source, rows[0]);
        var (changes, batch) = Reopening(source, session, rows[0]);
        batch = batch with { Sessions = [batch.Sessions[0] with { ExpectedRevision = 99 }] };
        var baseline = source.Snapshot;
        var database = LiveBytes(source.FilePath);
        var wal = LiveBytes(source.FilePath + "-wal");
        Assert.ThrowsException<DocumentException>(() => MonetaBackup.SaveAs(source, PathFor("failed.moneta"), baseline, changes, batch));
        Assert.AreSame(baseline, source.Snapshot);
        Assert.AreEqual(ReconciliationSessionState.Validated, source.ReadReconciliation(session.Id).Session.State);
        Assert.AreEqual(99L, batch.Sessions[0].ExpectedRevision);
        CollectionAssert.AreEqual(database, LiveBytes(source.FilePath));
        CollectionAssert.AreEqual(wal, LiveBytes(source.FilePath + "-wal"));
        Assert.IsFalse(File.Exists(PathFor("failed.moneta")));
        AssertNoWorkFiles();
    }

    [TestMethod]
    public void StaleHistoryCannotBeCertifiedOrCopiedEvenThoughAllBalancesAreUnchanged()
    {
        var (metadata, rows) = Sample();
        using var source = MonetaFile.Create(PathFor("source.moneta"), metadata, Inserts(rows));
        var session = Validate(source, rows[0]);
        using (var raw = Raw(source.FilePath))
        using (var command = raw.CreateCommand())
        {
            command.CommandText = "UPDATE Reconciliations SET statement_number='External historical edit';";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }
        Assert.AreEqual(source.Snapshot.AccountBalances![rows[0].AccountId],
            MonetaFile.Inspect(source.FilePath).AccountBalances![rows[0].AccountId]);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.Create(source, PathFor("stale.moneta"))).Error);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.SaveAs(source, PathFor("stale-copy.moneta"), source.Snapshot, [])).Error);
        Assert.AreEqual(session.Id, source.ReadReconciliation(session.Id).Session.Id);
        Assert.IsFalse(File.Exists(PathFor("stale.moneta")));
        Assert.IsFalse(File.Exists(PathFor("stale.moneta.manifest.json")));
        Assert.IsFalse(File.Exists(PathFor("stale-copy.moneta")));
        AssertNoWorkFiles();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ActualCertifiedV4PreservesDurableValidationReopeningAndLegacyReleaseWhileOnlyCopyUpgrades(bool restore)
    {
        using var source = CreateHistoricalSource(out var rows, out var sessions);
        var input = BuildHistoricalV4(source, rows);
        var original = File.ReadAllBytes(input);
        var manifest = File.ReadAllBytes(input + ".manifest.json");
        var attributes = File.GetAttributes(input);
        File.SetAttributes(input, attributes | FileAttributes.ReadOnly);
        try
        {
            using (var migrated = restore
                ? MonetaBackup.Restore(input, PathFor("upgraded.moneta"))
                : MonetaBackup.Migrate(input, PathFor("upgraded.moneta")))
            {
                AssertComplete(source, migrated, rows, sessions);
                Assert.AreEqual(7, migrated.Snapshot.SchemaVersion);
                Assert.AreEqual(0L, migrated.Snapshot.TransferCount);
                Assert.AreEqual(ReconciliationSessionState.Reopened, migrated.ReadReconciliation(sessions[0]).Session.State);
                Assert.AreEqual(ReconciliationStatus.Unmarked, migrated.GetTransaction(rows[2].Id)!.Status);
                using var raw = Raw(migrated.FilePath);
                using var command = raw.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM ReconciliationHistory WHERE kind=2 AND snapshot_version=1 AND snapshot_json IS NOT NULL;";
                Assert.AreEqual(1L, command.ExecuteScalar());
            }
            using var reopened = MonetaFile.Open(PathFor("upgraded.moneta"));
            AssertComplete(source, reopened, rows, sessions);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(input));
            CollectionAssert.AreEqual(manifest, File.ReadAllBytes(input + ".manifest.json"));
            Assert.IsTrue(File.GetAttributes(input).HasFlag(FileAttributes.ReadOnly));
            Assert.AreEqual(DocumentError.MigrationRequired, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(input)).Error);
        }
        finally { File.SetAttributes(input, attributes); }
        AssertNoWorkFiles();
    }

    [DataTestMethod]
    [DataRow("event")]
    [DataRow("released-snapshot")]
    public void HistoricalV4CertificateRejectsEqualBalanceDurableHistoryTamperBeforeMigration(string mutation)
    {
        using var source = CreateHistoricalSource(out var rows, out _);
        var input = BuildHistoricalV4(source, rows);
        using (var raw = Raw(input))
        using (var command = raw.CreateCommand())
        {
            command.CommandText = mutation == "event"
                ? "UPDATE ReconciliationHistory SET event_id=$id WHERE kind=2;"
                : "UPDATE ReconciliationHistory SET snapshot_json=replace(snapshot_json,'Native 2','Hidden 2') WHERE kind=2;";
            if (mutation == "event") command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }
        RefreshFileHash(input);
        var original = File.ReadAllBytes(input);
        Assert.AreEqual(DocumentError.VerificationFailed, Assert.ThrowsException<DocumentException>(() =>
            MonetaBackup.Restore(input, PathFor("rejected.moneta"))).Error);
        Assert.IsFalse(File.Exists(PathFor("rejected.moneta")));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(input));
        AssertNoWorkFiles();
    }

    private MonetaFile CreateHistoricalSource(out Transaction[] rows, out Guid[] sessions)
    {
        var (metadata, originals) = Sample();
        originals[2] = originals[2] with { Status = ReconciliationStatus.Reconciled };
        var source = MonetaFile.Create(PathFor("history-source.moneta"), metadata, Inserts(originals));
        try
        {
            var stamp = DateTimeOffset.UtcNow;
            var legacy = originals[2];
            source.Save(source.Snapshot, [new(legacy with { Status = ReconciliationStatus.Unmarked,
                Revision = 2, ModifiedUtc = stamp }, 1)],
                new([], [new(Guid.NewGuid(), null, legacy.Id, ReconciliationEventKind.LegacyReleased,
                    stamp, "Historical legacy release") { OperationBefore = legacy }]));
            var first = Validate(source, originals[0]);
            Reopen(source, first, originals[0]);
            var again = Validate(source, source.GetTransaction(originals[0].Id)!);
            var yen = Validate(source, originals[1]);
            rows = originals.Select(row => source.GetTransaction(row.Id)!).ToArray();
            sessions = [first.Id, again.Id, yen.Id];
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    private string BuildHistoricalV4(MonetaFile source, Transaction[] rows)
    {
        var input = PathFor("actual-v4.moneta");
        var metadata = source.Snapshot with { SchemaVersion = 4 };
        // Seed legacy R rows without a dangling session FK. The complete historical
        // graph and its original links are installed together before certification.
        BackupRestoreTests.BuildLegacy(input, metadata, 4,
            rows.Select(row => row with { ReconciliationId = null }).ToArray());
        using (var raw = Raw(input))
        using (var command = raw.CreateCommand())
        {
            // Populate actual v4 DDL, never downgrade a current database's version tag.
            // ATTACH reads the committed SQLite view, including its source WAL.
            command.CommandText = "ATTACH DATABASE $source AS history;";
            command.Parameters.AddWithValue("$source", source.FilePath);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            using (var transaction = raw.BeginTransaction())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    PRAGMA defer_foreign_keys=ON;
                    INSERT INTO Reconciliations SELECT * FROM history.Reconciliations;
                    INSERT INTO ReconciliationEntries SELECT * FROM history.ReconciliationEntries;
                    INSERT INTO ReconciliationHistory SELECT * FROM history.ReconciliationHistory;
                    UPDATE Transactions SET reconciliation_id=(
                        SELECT original.reconciliation_id FROM history.Transactions AS original
                        WHERE original.transaction_id=Transactions.transaction_id);
                    """;
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            command.Transaction = null;
            command.CommandText = "DETACH DATABASE history;";
            command.ExecuteNonQuery();
        }
        metadata = metadata with { LedgerDigest = FrozenV4LedgerDigest(input) };
        File.WriteAllText(input + ".manifest.json", JsonSerializer.Serialize(new
        {
            ManifestVersion = 1,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            Length = new FileInfo(input).Length,
            SchemaVersion = 4,
            DocumentId = metadata.Id,
            SnapshotDigest = LedgerBackupTests.LegacyDigest(metadata),
            CurrencyCount = metadata.Currencies.Count,
            AccountCount = metadata.Accounts.Count
        }));
        return input;
    }

    // Frozen v4 canonical stream for the historical fixture (which has no catalogs).
    private static string FrozenV4LedgerDigest(string path)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string? value)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, value is null ? -1 : Encoding.UTF8.GetByteCount(value));
            digest.AppendData(length);
            if (value is not null) digest.AppendData(Encoding.UTF8.GetBytes(value));
        }
        void Row(string kind, SqliteDataReader reader)
        {
            Field(kind);
            for (var index = 0; index < reader.FieldCount; index++)
                Field(reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
        }
        Field("Moneta-normalized-ledger-v4");
        using var raw = Raw(path);
        using var command = raw.CreateCommand();
        command.CommandText = """
            SELECT transaction_id,account_id,date,label,amount_minor,native_code,direction,status,notes,payee_id,
                category_id,subcategory_id,payment_method_id,is_deleted,created_utc,modified_utc,revision,marked_on,reconciliation_id
            FROM Transactions ORDER BY transaction_id COLLATE BINARY;
            """;
        using (var reader = command.ExecuteReader()) while (reader.Read()) Row("transaction", reader);
        command.CommandText = """
            SELECT session_id,account_id,native_code,statement_date,statement_number,opening_minor,expected_minor,
                calculated_minor,previous_session_id,validated_utc,state,revision,reopened_utc,reopen_reason
            FROM Reconciliations ORDER BY session_id COLLATE BINARY;
            """;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                Row("session", reader);
                using var entries = raw.CreateCommand();
                entries.CommandText = "SELECT session_id,transaction_id,snapshot_version,snapshot_json FROM ReconciliationEntries WHERE session_id=$id ORDER BY transaction_id COLLATE BINARY;";
                entries.Parameters.AddWithValue("$id", reader.GetString(0));
                using var entryReader = entries.ExecuteReader();
                while (entryReader.Read()) Row("entry", entryReader);
            }
        }
        command.CommandText = "SELECT event_id,session_id,transaction_id,kind,occurred_utc,reason,snapshot_version,snapshot_json FROM ReconciliationHistory ORDER BY event_id COLLATE BINARY;";
        using (var reader = command.ExecuteReader()) while (reader.Read()) Row("event", reader);
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static (FinancialDocument, Transaction[]) Sample(int count = 3)
    {
        var currencies = new[] { new Currency("EUR", "Euro", 2), new Currency("JPY", "Yen", 0), new Currency("KWD", "Dinar", 3) };
        var accounts = currencies.Select((currency, index) => new Account(Guid.NewGuid(), "Account " + index, currency, 10m)
            { CreatedUtc = Created, ModifiedUtc = Created }).ToArray();
        var metadata = new FinancialDocument(Guid.NewGuid(), "Reconciliation backup", Created, Created, currencies, accounts)
            { SelectedAccountId = accounts[0].Id };
        var rows = Enumerable.Range(0, count).Select(index =>
        {
            var account = accounts[index < 3 ? index : 0];
            var amount = account.Currency.DecimalPlaces switch { 0 => 7m, 3 => 0.123m, _ => 1.25m };
            return new Transaction(Guid.NewGuid(), account.Id, new DateOnly(2025, 1, 2), "Native " + index,
                new Payee(""), new Category(""), amount, account.Currency, TransactionDirection.Credit,
                index == 0 ? ReconciliationStatus.Marked : ReconciliationStatus.Unmarked, "Backup notes 日本")
            { CreatedUtc = Created, ModifiedUtc = Created, MarkedOn = index == 0 ? new DateOnly(2025, 1, 3) : null };
        }).ToArray();
        return (metadata, rows);
    }

    private static TransactionChange[] Inserts(IEnumerable<Transaction> rows) => rows.Select(row => new TransactionChange(row, 0)).ToArray();

    private static (TransactionChange[], ReconciliationBatch) Validation(MonetaFile source, Transaction original)
    {
        var stamp = DateTimeOffset.UtcNow;
        var account = source.Snapshot.Accounts.Single(account => account.Id == original.AccountId);
        var candidates = source.ReadReconciliationCandidates(account, Today, new HashSet<Guid> { original.Id }, 0, 100);
        var closing = candidates.OpeningBalance + FinancialRules.SignedAmount(original);
        var session = new ReconciliationSession(Guid.NewGuid(), account.Id, Today, "Statement 日本", candidates.OpeningBalance,
            closing, closing, candidates.PreviousSessionId, stamp);
        var changed = original with { Status = ReconciliationStatus.Reconciled, MarkedOn = original.MarkedOn ?? Today,
            ReconciliationId = session.Id, Revision = original.Revision + 1, ModifiedUtc = stamp };
        return ([new(changed, original.Revision)], new([new(session, 0, [new(session.Id, original)])],
            [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Validated, stamp, "")]));
    }

    private static ReconciliationSession Validate(MonetaFile source, Transaction original)
    {
        var (changes, batch) = Validation(source, original);
        source.Save(source.Snapshot, changes, batch);
        return batch.Sessions.Single().Value;
    }

    private static (TransactionChange[], ReconciliationBatch) Reopening(MonetaFile source, ReconciliationSession session, Transaction original)
    {
        var stamp = DateTimeOffset.UtcNow;
        var current = source.GetTransaction(original.Id)!;
        var reopened = session with { State = ReconciliationSessionState.Reopened, Revision = 2,
            ReopenedUtc = stamp, ReopenReason = "Statement correction 日本" };
        var changed = current with { Status = original.Status, MarkedOn = original.MarkedOn, ReconciliationId = null,
            Revision = current.Revision + 1, ModifiedUtc = stamp };
        return ([new(changed, current.Revision)], new([new(reopened, 1, [])],
            [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Reopened, stamp, reopened.ReopenReason!)]));
    }

    private static void Reopen(MonetaFile source, ReconciliationSession session, Transaction original)
    {
        var (changes, batch) = Reopening(source, session, original);
        source.Save(source.Snapshot, changes, batch);
    }

    private static void AssertComplete(MonetaFile expected, MonetaFile actual, Transaction[] rows, Guid[] sessions)
    {
        BackupRestoreTests.AssertCompleteSnapshot(expected.Snapshot, actual.Snapshot);
        Assert.AreEqual(expected.Snapshot.LedgerDigest, actual.Snapshot.LedgerDigest);
        Assert.AreEqual(expected.Snapshot.OperationCount, actual.Snapshot.OperationCount);
        Assert.AreEqual(expected.Snapshot.ReconciliationCount, actual.Snapshot.ReconciliationCount);
        Assert.AreEqual(expected.Snapshot.ReconciliationEventCount, actual.Snapshot.ReconciliationEventCount);
        foreach (var account in expected.Snapshot.Accounts)
        {
            Assert.AreEqual(expected.Snapshot.AccountBalances![account.Id], actual.Snapshot.AccountBalances![account.Id]);
            Assert.AreEqual(expected.Snapshot.AccountPointedBalances![account.Id], actual.Snapshot.AccountPointedBalances![account.Id]);
            var history = expected.ReadReconciliationHistory(account.Id, 0, 100);
            var copied = actual.ReadReconciliationHistory(account.Id, 0, 100);
            Assert.AreEqual(history.TotalCount, copied.TotalCount);
            CollectionAssert.AreEqual(history.Sessions.ToArray(), copied.Sessions.ToArray());
        }
        foreach (var row in rows) Assert.AreEqual(expected.GetTransaction(row.Id), actual.GetTransaction(row.Id));
        foreach (var id in sessions) AssertDetails(expected.ReadReconciliation(id), actual.ReadReconciliation(id));
    }

    private static void AssertDetails(ReconciliationDetails expected, ReconciliationDetails actual)
    {
        Assert.AreEqual(expected.Session, actual.Session);
        CollectionAssert.AreEqual(expected.Entries.ToArray(), actual.Entries.ToArray());
        CollectionAssert.AreEqual(expected.Events.ToArray(), actual.Events.ToArray());
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
