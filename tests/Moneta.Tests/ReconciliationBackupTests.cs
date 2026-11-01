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
