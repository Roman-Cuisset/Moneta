using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class MonetaFileTests
{
    private string directory = null!;
    private string FileName => Path.Combine(directory, "synthetic.moneta");
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonetaFileTests-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [TestMethod]
    public void CreateAndReopenPreserveIdentityMetadataCurrenciesAndSameCurrencyAccounts()
    {
        var snapshot = Synthetic();
        using (var created = MonetaFile.Create(FileName, snapshot)) AssertDocument(snapshot, created.Snapshot);
        using (var opened = MonetaFile.Open(FileName)) AssertDocument(snapshot, opened.Snapshot);
        AssertDocument(snapshot, MonetaFile.Inspect(FileName));
        using var stream = File.OpenRead(FileName);
        var header = new byte[16];
        stream.ReadExactly(header);
        CollectionAssert.AreEqual("SQLite format 3\0"u8.ToArray(), header);
    }

    [TestMethod]
    public void SaveIsExplicitAtomicAndPreservesAccountIdsAndMetadata()
    {
        var original = Synthetic();
        using (var file = MonetaFile.Create(FileName, original))
        {
            var changed = original with
            {
                Name = "Dossier synthétique renommé",
                ModifiedUtc = original.ModifiedUtc.AddMinutes(1),
                Accounts = original.Accounts.Select((account, index) => account with
                {
                    Name = "Compte modifié " + index,
                    Notes = "Écriture Unicode : 日本語",
                    IsArchived = index == 1,
                    ModifiedUtc = account.ModifiedUtc.AddMinutes(1)
                }).Reverse().ToArray()
            };
            AssertDocument(original, MonetaFile.Inspect(FileName));
            var invalid = changed with { Accounts = changed.Accounts.Select(account =>
                account.Id == original.Accounts[0].Id ? account with { OpeningBalance = -1234.56m } : account).ToArray() };
            Assert.AreEqual(DocumentError.InvalidData, Assert.ThrowsException<DocumentException>(() => file.Save(invalid)).Error);
            AssertDocument(original, MonetaFile.Inspect(FileName));
            file.Save(changed);
            AssertDocument(changed, file.Snapshot);
            AssertDocument(changed, MonetaFile.Inspect(FileName));
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual("Dossier synthétique renommé", reopened.Snapshot.Name);
        Assert.IsTrue(reopened.Snapshot.Accounts.Single(account => account.Id == original.Accounts[1].Id).IsArchived);
    }

    [TestMethod]
    public void SaveCanGrowAndReorderCurrenciesAndAccountsWithoutOrdinalCollisions()
    {
        var expected = Synthetic();
        using (var file = MonetaFile.Create(FileName, expected))
        {
            foreach (var code in new[] { "GBP", "KWD", "USD" })
            {
                var currency = new Currency(code, "Devise synthétique " + code, 2);
                var added = NewAccount(currency, 12.34m, expected.Accounts.Count);
                expected = expected with
                {
                    ModifiedUtc = expected.ModifiedUtc.AddMinutes(1),
                    Currencies = new[] { currency }.Concat(expected.Currencies.Reverse()).ToArray(),
                    Accounts = new[] { added }.Concat(expected.Accounts.Reverse()).ToArray(),
                    SelectedAccountId = added.Id
                };
                file.Save(expected);
                AssertDocument(expected, file.Snapshot);
                AssertDocument(expected, MonetaFile.Inspect(FileName));
            }
            // Reordering without growth must also use a range disjoint from all old ordinals.
            expected = expected with
            {
                ModifiedUtc = expected.ModifiedUtc.AddMinutes(1),
                Currencies = expected.Currencies.Reverse().ToArray(),
                Accounts = expected.Accounts.Reverse().ToArray()
            };
            file.Save(expected);
            AssertDocument(expected, MonetaFile.Inspect(FileName));
        }
        using var reopened = MonetaFile.Open(FileName);
        AssertDocument(expected, reopened.Snapshot);
    }

    [TestMethod]
    public void MonetaryValuesRemainExactAcrossScalesAndIntegerBoundaries()
    {
        var currencies = new[]
        {
            new Currency("EUR", "Euro synthétique", 2),
            new Currency("JPY", "Yen synthétique", 0),
            new Currency("KWD", "Dinar synthétique", 3),
            new Currency("XTS", "Précision synthétique", 28)
        };
        var amounts = new[] { 0m, -0.01m, 0.01m, 92233720368547758.07m, -92233720368547758.07m };
        var accounts = amounts.Select((amount, index) => NewAccount(currencies[0], amount, index)).Concat(new[]
        {
            NewAccount(currencies[1], 9223372036854775807m, 10),
            NewAccount(currencies[1], -9223372036854775807m, 11),
            NewAccount(currencies[2], -0.001m, 12),
            NewAccount(currencies[3], 0.0000000000000000000000000001m, 13)
        }).ToArray();
        var expected = Synthetic() with { Currencies = currencies, Accounts = accounts, SelectedAccountId = accounts[0].Id };
        using (MonetaFile.Create(FileName, expected)) { }
        using var reopened = MonetaFile.Open(FileName);
        CollectionAssert.AreEqual(accounts.Select(account => account.OpeningBalance).ToArray(), reopened.Snapshot.Accounts.Select(account => account.OpeningBalance).ToArray());
        using var raw = Raw(FileName);
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Accounts WHERE typeof(opening_minor)<>'integer';";
        Assert.AreEqual(0L, command.ExecuteScalar());
    }

    [TestMethod]
    public void InvalidFractionOverflowIdsAndDatesAreRejectedBeforeCreation()
    {
        var valid = Synthetic();
        foreach (var amount in new[] { 0.001m, 92233720368547758.08m, -92233720368547758.08m })
        {
            var bad = valid with { Accounts = new[] { valid.Accounts[0] with { OpeningBalance = amount } }, SelectedAccountId = valid.Accounts[0].Id };
            AssertError(DocumentError.InvalidData, () => MonetaFile.Create(FileName, bad));
            Assert.IsFalse(File.Exists(FileName));
        }
        AssertError(DocumentError.InvalidData, () => MonetaFile.Create(FileName, valid with { Id = Guid.Empty }));
        AssertError(DocumentError.InvalidData, () => MonetaFile.Create(FileName, valid with { ModifiedUtc = valid.CreatedUtc.AddTicks(-1) }));
        AssertError(DocumentError.InvalidData, () => MonetaFile.Create(FileName, valid with { Accounts = new[] { valid.Accounts[0], valid.Accounts[0] } }));
    }

    [TestMethod]
    public void MissingOpenNeverCreatesAndExistingCreateNeverOverwrites()
    {
        AssertError(DocumentError.MissingFile, () => MonetaFile.Open(FileName));
        Assert.IsFalse(File.Exists(FileName));
        using (MonetaFile.Create(FileName, Synthetic())) { }
        var bytes = File.ReadAllBytes(FileName);
        AssertError(DocumentError.DestinationExists, () => MonetaFile.Create(FileName, Synthetic()));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
        var bp = Path.Combine(directory, "protected.bp");
        File.WriteAllText(bp, "not SQLite; must never be touched");
        var original = File.ReadAllBytes(bp);
        AssertError(DocumentError.InvalidFormat, () => MonetaFile.Open(bp));
        AssertError(DocumentError.InvalidFormat, () => MonetaFile.Create(bp, Synthetic()));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(bp));
        Assert.IsFalse(File.Exists(bp + ".lock"));
    }

    [DataTestMethod]
    [DataRow("-wal")]
    [DataRow("-shm")]
    [DataRow("-journal")]
    public void CreateRejectsOrphanCompanionsWithoutChangingTheirBytes(string suffix)
    {
        var companion = FileName + suffix;
        var bytes = "Synthetic recovery data must be preserved exactly"u8.ToArray();
        File.WriteAllBytes(companion, bytes);
        AssertError(DocumentError.DestinationExists, () => MonetaFile.Create(FileName, Synthetic()));
        Assert.IsFalse(File.Exists(FileName));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(companion));
    }

    [TestMethod]
    public void CreateRejectsValidOrphanWalFromAnotherDocument()
    {
        var sourcePath = Path.Combine(directory, "wal-source.moneta");
        var original = Synthetic();
        using var source = MonetaFile.Create(sourcePath, original);
        source.Save(original with { Name = "Données validées dans WAL", ModifiedUtc = original.ModifiedUtc.AddMinutes(1) });
        byte[] wal;
        using (var stream = new FileStream(sourcePath + "-wal", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var bytes = new MemoryStream())
        {
            stream.CopyTo(bytes);
            wal = bytes.ToArray();
        }
        Assert.IsTrue(wal.Length > 32, "The synthetic source must contain committed WAL frames.");
        File.WriteAllBytes(FileName + "-wal", wal);
        AssertError(DocumentError.DestinationExists, () => MonetaFile.Create(FileName, Synthetic()));
        Assert.IsFalse(File.Exists(FileName));
        CollectionAssert.AreEqual(wal, File.ReadAllBytes(FileName + "-wal"));
        Assert.AreEqual("Données validées dans WAL", MonetaFile.Inspect(sourcePath).Name);
    }

    [TestMethod]
    public void EmptyTruncatedForeignAndCorruptFilesAreNotAcceptedOrRecreated()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), "SQLite format"u8.ToArray(), "PK synthetic ZIP"u8.ToArray() })
        {
            File.WriteAllBytes(FileName, bytes);
            AssertError(DocumentError.InvalidFormat, () => MonetaFile.Open(FileName));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
        }
        File.Delete(FileName);
        using (var connection = Raw(FileName, create: true)) Execute(connection, "CREATE TABLE NotMoneta(value TEXT); INSERT INTO NotMoneta VALUES('synthetic');");
        var foreign = File.ReadAllBytes(FileName);
        AssertError(DocumentError.InvalidFormat, () => MonetaFile.Open(FileName));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(FileName));
        var corrupt = new byte[4096];
        "SQLite format 3\0"u8.CopyTo(corrupt);
        File.WriteAllBytes(FileName, corrupt);
        AssertError(DocumentError.Corrupt, () => MonetaFile.Open(FileName));
        CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(FileName));
    }

    [TestMethod]
    public void FutureVersionAndMetadataDisagreementAreRefusedWithoutDowngrade()
    {
        using (MonetaFile.Create(FileName, Synthetic())) { }
        using (var connection = Raw(FileName)) Execute(connection, $"PRAGMA user_version={MonetaFile.CurrentSchemaVersion + 1};");
        var future = File.ReadAllBytes(FileName);
        AssertError(DocumentError.UnsupportedVersion, () => MonetaFile.Open(FileName));
        CollectionAssert.AreEqual(future, File.ReadAllBytes(FileName));
        using (var connection = Raw(FileName)) Execute(connection, $"PRAGMA user_version={MonetaFile.CurrentSchemaVersion}; UPDATE Document SET schema_version=1;");
        AssertError(DocumentError.InvalidFormat, () => MonetaFile.Open(FileName));
    }

    [DataTestMethod]
    [DataRow("CREATE TABLE Unexpected(value TEXT);")]
    [DataRow("CREATE VIEW Unexpected AS SELECT name FROM Accounts;")]
    [DataRow("CREATE TRIGGER Unexpected AFTER UPDATE ON Accounts BEGIN UPDATE Accounts SET notes='mutated'; END;")]
    [DataRow("CREATE INDEX Unexpected ON Accounts(name);")]
    [DataRow("ALTER TABLE Accounts ADD COLUMN unexpected TEXT;")]
    public void UnknownSchemaObjectsAreRejected(string mutation)
    {
        using (MonetaFile.Create(FileName, Synthetic())) { }
        using (var connection = Raw(FileName)) Execute(connection, mutation);
        var before = File.ReadAllBytes(FileName);
        AssertError(DocumentError.InvalidFormat, () => MonetaFile.Open(FileName));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(FileName));
    }

    [TestMethod]
    public void SecondMonetaOpenIsRejectedAndCloseReleasesOwnership()
    {
        using (var first = MonetaFile.Create(FileName, Synthetic()))
            AssertError(DocumentError.InUse, () => MonetaFile.Open(FileName));
        using var second = MonetaFile.Open(FileName);
        Assert.AreEqual(3, second.Snapshot.Accounts.Count);
    }

    [TestMethod]
    public void RawSqliteWriterLockLeavesSaveAndSnapshotUnchangedThenAllowsRetry()
    {
        var original = Synthetic();
        using var file = MonetaFile.Create(FileName, original);
        var changed = original with { Name = "Nom non validé par commit", ModifiedUtc = original.ModifiedUtc.AddMinutes(1) };
        using (var blocker = Raw(FileName))
        using (var transaction = blocker.BeginTransaction())
        {
            using var command = blocker.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Document SET name='Modification externe non validée';";
            command.ExecuteNonQuery();
            AssertError(DocumentError.Locked, () => file.Save(changed));
            AssertDocument(original, file.Snapshot);
            transaction.Rollback();
        }
        AssertDocument(original, MonetaFile.Inspect(FileName));
        file.Save(changed);
        AssertDocument(changed, MonetaFile.Inspect(FileName));
    }

    [TestMethod]
    public void SaveRejectsRemovalNativeCurrencyChangesAndScaleChanges()
    {
        var original = Synthetic();
        using var file = MonetaFile.Create(FileName, original);
        AssertError(DocumentError.InvalidData, () => file.Save(original with { Accounts = original.Accounts.Take(1).ToArray() }));
        var replacement = NewAccount(original.Currencies[1], original.Accounts[0].OpeningBalance, 0) with { Id = original.Accounts[0].Id, CreatedUtc = original.Accounts[0].CreatedUtc };
        AssertError(DocumentError.InvalidData, () => file.Save(original with { Accounts = new[] { replacement }.Concat(original.Accounts.Skip(1)).ToArray() }));
        AssertError(DocumentError.InvalidData, () => file.Save(original with { Currencies = original.Currencies.Skip(1).ToArray() }));
        AssertError(DocumentError.InvalidData, () => file.Save(original with
        {
            Currencies = new[] { original.Currencies[0], original.Currencies[1] with { DecimalPlaces = 1 } }
        }));
        AssertDocument(original, MonetaFile.Inspect(FileName));
    }

    [TestMethod]
    public void SaveRejectsSchemaInjectedByAnotherSqliteClientWithoutExecutingItsTrigger()
    {
        var original = Synthetic();
        using var file = MonetaFile.Create(FileName, original);
        using (var raw = Raw(FileName))
            Execute(raw, "CREATE TRIGGER Unexpected AFTER UPDATE ON Accounts BEGIN UPDATE Accounts SET notes='injected'; END;");
        AssertError(DocumentError.InvalidFormat, () => file.Save(original with { Name = "Must not commit" }));
        AssertDocument(original, file.Snapshot);
        using (var raw = Raw(FileName)) Execute(raw, "DROP TRIGGER Unexpected;");
        AssertDocument(original, MonetaFile.Inspect(FileName));
    }

    [DataTestMethod]
    [DataRow("UPDATE Currencies SET decimal_places=3 WHERE code='EUR';")]
    [DataRow("UPDATE Document SET document_id='4bc05ce9-1d39-491a-95d5-d12b5c604cf0';")]
    [DataRow("UPDATE Accounts SET native_code='JPY';")]
    [DataRow("UPDATE Accounts SET notes='Modification externe conservée';")]
    public void SaveRejectsStaleSnapshotAndPreservesExternalFinancialChanges(string mutation)
    {
        var original = Synthetic();
        using var file = MonetaFile.Create(FileName, original);
        using (var raw = Raw(FileName)) Execute(raw, mutation);
        var external = MonetaFile.Inspect(FileName);
        AssertError(DocumentError.InvalidData, () => file.Save(original with
        {
            Name = "Ne doit pas écraser la modification externe",
            ModifiedUtc = original.ModifiedUtc.AddMinutes(1)
        }));
        AssertDocument(original, file.Snapshot);
        AssertDocument(external, MonetaFile.Inspect(FileName));
    }

    [TestMethod]
    public void BusyCheckpointIsReportedWithoutDeletingRecoveryWal()
    {
        var original = Synthetic();
        var file = MonetaFile.Create(FileName, original);
        try
        {
            using var readerConnection = Raw(FileName);
            using var transaction = readerConnection.BeginTransaction(deferred: true);
            using (var command = readerConnection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT name FROM Document;";
                _ = command.ExecuteScalar();
            }
            var changed = original with { Name = "Après lecteur", ModifiedUtc = original.ModifiedUtc.AddMinutes(1) };
            file.Save(changed);
            AssertError(DocumentError.Locked, file.Close);
            Assert.IsTrue(File.Exists(FileName + "-wal"));
            AssertError(DocumentError.InUse, () => MonetaFile.Open(FileName));
            AssertDocument(changed, file.Snapshot);
            transaction.Rollback();
            file.Save(changed with { Name = "Nouvelle sauvegarde après checkpoint refusé" });
            file.Close();
        }
        finally { file.Dispose(); }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual("Nouvelle sauvegarde après checkpoint refusé", reopened.Snapshot.Name);
    }

    [TestMethod]
    public void BundledSqliteVersionAndPragmasMeetStorageContract()
    {
        using var file = MonetaFile.Create(FileName, Synthetic());
        using var raw = Raw(FileName);
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = Version.Parse((string)command.ExecuteScalar()!);
        TestContext.WriteLine("Bundled SQLite runtime version: " + version);
        Assert.IsTrue(version >= new Version(3, 51, 3), "WAL-reset fix required; actual SQLite " + version);
        command.CommandText = "PRAGMA journal_mode;";
        Assert.AreEqual("wal", command.ExecuteScalar());
        command.CommandText = "PRAGMA application_id;";
        Assert.AreEqual((long)MonetaFile.ApplicationId, command.ExecuteScalar());
        command.CommandText = "PRAGMA user_version;";
        Assert.AreEqual((long)MonetaFile.CurrentSchemaVersion, command.ExecuteScalar());
    }

    [TestMethod]
    public void SchemaFourCreatesStrictHistoryAndNullablePointingFieldsWithoutInventedRecords()
    {
        using var file = MonetaFile.Create(FileName, Synthetic());
        Assert.AreEqual(6, file.Snapshot.SchemaVersion);
        Assert.AreEqual(0L, file.Snapshot.ReconciliationCount);
        Assert.AreEqual(0L, file.Snapshot.ReconciliationEventCount);
        using var raw = Raw(FileName);
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_list WHERE name IN ('Reconciliations','ReconciliationEntries','ReconciliationHistory') AND strict=1;";
        Assert.AreEqual(3L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name IN ('marked_on','reconciliation_id') AND [notnull]=0;";
        Assert.AreEqual(2L, command.ExecuteScalar());
        foreach (var account in file.Snapshot.Accounts)
            Assert.AreEqual(account.OpeningBalance, file.Snapshot.AccountPointedBalances![account.Id]);
    }

    private static FinancialDocument Synthetic()
    {
        var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var currencies = new[]
        {
            new Currency("EUR", "Euro synthétique", 2) { Symbol = "€", DisplayRules = new("fr-FR", true, false) },
            new Currency("JPY", "Yen synthétique", 0) { Symbol = "¥", DisplayRules = new("ja-JP", true, true) }
        };
        var accounts = new[] { NewAccount(currencies[0], 123.45m, 0), NewAccount(currencies[0], -67.89m, 1), NewAccount(currencies[0], 0m, 2) };
        return new(Guid.NewGuid(), "Dossier synthétique — éà", created, created.AddDays(1), currencies, accounts) { SelectedAccountId = accounts[1].Id };
    }

    private static Account NewAccount(Currency currency, decimal amount, int index)
    {
        var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        return new(Guid.NewGuid(), "Compte synthétique " + index, currency, amount)
        {
            Type = index == 1 ? null : "Courant fictif",
            Notes = index == 2 ? "" : "Notes synthétiques non personnelles",
            CreatedUtc = created,
            ModifiedUtc = created.AddHours(1),
            IsArchived = index == 2
        };
    }

    private static SqliteConnection Raw(string path, bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3 }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AssertError(DocumentError expected, Action action) => Assert.AreEqual(expected, Assert.ThrowsException<DocumentException>(action).Error);

    private static void AssertDocument(FinancialDocument expected, FinancialDocument actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.Name, actual.Name);
        Assert.AreEqual(expected.SchemaVersion, actual.SchemaVersion);
        Assert.AreEqual(expected.CreatedUtc, actual.CreatedUtc);
        Assert.AreEqual(expected.ModifiedUtc, actual.ModifiedUtc);
        Assert.AreEqual(expected.SelectedAccountId, actual.SelectedAccountId);
        CollectionAssert.AreEqual(expected.Currencies.ToArray(), actual.Currencies.ToArray());
        CollectionAssert.AreEqual(expected.Accounts.ToArray(), actual.Accounts.ToArray());
    }
}
