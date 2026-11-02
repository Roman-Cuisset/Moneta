using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class TransferStorageTests
{
    private string directory = null!;
    private string FileName => Path.Combine(directory, "transfers.moneta");
    private static readonly DateTimeOffset Created = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 1, 4);
    private static readonly DateOnly Today = DateOnly.FromDateTime(System.DateTime.Today);

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonetaTransferStorage-" + Guid.NewGuid().ToString("N"))).FullName;
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [DataTestMethod]
    [DataRow(0, 2, 0)]
    [DataRow(0, 4, 4)]
    [DataRow(2, 0, 6)]
    [DataRow(2, 4, 2)]
    [DataRow(4, 0, 0)]
    [DataRow(4, 2, 6)]
    [DataRow(0, 1, 0)]
    [DataRow(2, 3, 3)]
    [DataRow(4, 5, 6)]
    public void EveryNativeCurrencyDirectionAndSeparateFeeRoundTripsExactly(int source, int target, int feeAccount)
    {
        var metadata = Document(); var transfer = Details(metadata, source, target, feeAccount);
        string digest;
        using (var file = Create(metadata, transfer))
        {
            Assert.AreEqual(transfer, file.ReadTransfer(transfer.Value.Id));
            Assert.AreEqual(transfer.Value.Id, file.FindTransferId(transfer.Debit.Id));
            Assert.AreEqual(transfer.Value.Id, file.FindTransferId(transfer.Credit.Id));
            Assert.AreEqual(transfer.Value.Id, file.FindTransferId(transfer.Fee!.Id));
            Assert.IsNull(file.FindTransferId(Guid.NewGuid()));
            Assert.AreEqual(3L, file.Snapshot.OperationCount); Assert.AreEqual(1L, file.Snapshot.TransferCount);
            foreach (var account in metadata.Accounts)
            {
                var expected = Legs(transfer).Where(value => value.AccountId == account.Id).Aggregate(account.OpeningBalance,
                    (balance, value) => checked(balance + FinancialRules.SignedAmount(value)));
                Assert.AreEqual(expected, file.Snapshot.AccountBalances![account.Id]);
            }
            digest = file.Snapshot.LedgerDigest!;
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(transfer, reopened.ReadTransfer(transfer.Value.Id));
        Assert.AreEqual(digest, reopened.Snapshot.LedgerDigest);
        Assert.AreEqual(digest, MonetaFile.Inspect(FileName).LedgerDigest);
        using var raw = Raw(FileName);
        Assert.AreEqual(2L, Scalar(raw, "SELECT COUNT(*) FROM pragma_table_list WHERE name IN ('Transfers','TransferLegs') AND strict=1;"));
        Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name LIKE '%transfer%';"));
    }

    [TestMethod]
    public void ScaleTwentyEightAndInt64BoundariesDoNotRequireARepresentableRate()
    {
        var metadata = Document(); var original = Details(metadata, 0, 6);
        original = original with
        {
            Debit = original.Debit with { Amount = 92233720368547758.07m },
            Credit = original.Credit with { Amount = 0.0000000000000000000000000001m }
        };
        using var file = Create(metadata, original);
        Assert.AreEqual(original, file.ReadTransfer(original.Value.Id));
        var reverse = Details(metadata, 6, 0) with { Debit = original.Credit with { Id = Guid.NewGuid(), AccountId = metadata.Accounts[6].Id, Direction = TransactionDirection.Debit, Label = "Virement émis" }, Credit = original.Debit with { Id = Guid.NewGuid(), AccountId = metadata.Accounts[0].Id, Direction = TransactionDirection.Credit, Label = "Virement reçu" } };
        reverse = reverse with { Value = reverse.Value with { DebitTransactionId = reverse.Debit.Id, CreditTransactionId = reverse.Credit.Id } };
        file.Save(file.Snapshot, Patches(reverse), transfers: [Change(reverse)]);
        Assert.AreEqual(reverse, file.ReadTransfer(reverse.Value.Id));
        Assert.AreEqual(0m, file.Snapshot.AccountBalances![metadata.Accounts[0].Id]);
        Assert.AreEqual(0m, file.Snapshot.AccountBalances![metadata.Accounts[6].Id]);
    }

    [TestMethod]
    public void InvalidAmountsIdentityRolesDatesAndTombstonesNeverPublishAnyRows()
    {
        var metadata = Document(); using var file = MonetaFile.Create(FileName, metadata);
        var valid = Details(metadata, 0, 1, 2);
        var invalid = new[]
        {
            valid with { Debit = valid.Debit with { Amount = 0m } },
            valid with { Credit = valid.Credit with { Amount = 0m } },
            valid with { Debit = valid.Debit with { Amount = -1m } },
            valid with { Credit = valid.Credit with { Amount = 1.001m } },
            valid with { Debit = valid.Debit with { Amount = 92233720368547758.08m } },
            valid with { Credit = valid.Credit with { Amount = valid.Credit.Amount + 1m } },
            valid with { Credit = valid.Credit with { AccountId = valid.Debit.AccountId } },
            valid with { Debit = valid.Debit with { AccountId = Guid.NewGuid() } },
            valid with { Debit = valid.Debit with { Currency = metadata.Currencies[1] } },
            valid with { Debit = valid.Debit with { Direction = TransactionDirection.Credit } },
            valid with { Credit = valid.Credit with { Date = Date.AddDays(1) } },
            valid with { Fee = valid.Fee! with { Details = "Notes discordantes" } },
            valid with { Fee = valid.Fee! with { Amount = -1m } },
            valid with { Value = valid.Value with { Reference = new string('x', 121) } },
            valid with { Value = valid.Value with { ExchangeDate = Date, RateSource = "Interdit en même devise" } },
            valid with { Value = valid.Value with { IsDeleted = true } },
            valid with { Debit = valid.Debit with { IsDeleted = true } },
            valid with { Value = valid.Value with { CreditTransactionId = valid.Debit.Id } }
        };
        var baseline = file.Snapshot;
        foreach (var value in invalid)
        {
            Invalid(() => file.Save(baseline with { Name = "Ne doit pas être publié" }, Patches(value), transfers: [Change(value)]));
            Assert.AreSame(baseline, file.Snapshot);
            Assert.AreEqual(0L, MonetaFile.Inspect(FileName).OperationCount);
            Assert.AreEqual(0L, MonetaFile.Inspect(FileName).TransferCount);
        }
        var archived = metadata with { Accounts = metadata.Accounts.Select((account, index) => index == 1 ? account with { IsArchived = true } : account).ToArray() };
        Invalid(() => file.Save(archived, Patches(valid), transfers: [Change(valid)]));
        var fx = Details(metadata, 0, 2);
        Invalid(() => file.Save(baseline, Patches(fx), transfers: [new(fx.Value with { RateSource = null }, 0)]));
    }

    [TestMethod]
    public void CompletePatchesAndImmutableLinksAreRequiredIncludingFirstFeeAndRetainedZeroFee()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2);
        using var file = Create(metadata, original); var baseline = file.Snapshot;
        var edit = Advance(original) with { Debit = Next(original.Debit with { Amount = 15m }) };
        Invalid(() => file.Save(baseline, [new(edit.Debit, 1)], transfers: [Change(edit)]));
        Invalid(() => file.Save(baseline, Patches(edit)));
        Invalid(() => file.Save(baseline, [], transfers: [Change(edit)]));
        Invalid(() => file.Save(baseline, Patches(edit), transfers: [Change(edit), Change(edit)]));
        Invalid(() => file.Save(baseline, Patches(edit), transfers: [new(edit.Value with { DebitTransactionId = Guid.NewGuid() }, 1)]));
        Invalid(() => file.Save(baseline, Patches(edit), transfers: [new(edit.Value with { CreatedUtc = Created.AddTicks(1) }, 1)]));
        Invalid(() => file.Save(baseline, Patches(edit), transfers: [new(edit.Value, 0)]));
        Invalid(() => file.Save(baseline, Patches(edit with { Debit = edit.Debit with { AccountId = metadata.Accounts[1].Id } }), transfers: [Change(edit)]));
        Assert.AreEqual(original, file.ReadTransfer(original.Value.Id));

        var fee = Operation(metadata.Accounts[4], TransactionDirection.Debit, 1.23m, "Frais de virement");
        var withFee = edit with { Value = edit.Value with { FeeTransactionId = fee.Id }, Fee = fee };
        file.Save(file.Snapshot, Patches(withFee), transfers: [Change(withFee)]);
        Assert.AreEqual(withFee, file.ReadTransfer(original.Value.Id));
        var zero = Advance(withFee); zero = zero with { Fee = zero.Fee! with { Amount = 0m } };
        file.Save(file.Snapshot, Patches(zero), transfers: [Change(zero)]);
        Assert.AreEqual(0m, file.ReadTransfer(original.Value.Id).Fee!.Amount);
        Assert.AreEqual(fee.Id, file.ReadTransfer(original.Value.Id).Value.FeeTransactionId);
        var removal = Advance(zero) with { Fee = null }; removal = removal with { Value = removal.Value with { FeeTransactionId = null } };
        Invalid(() => file.Save(file.Snapshot, Patches(removal), transfers: [Change(removal)]));
        var replacement = Advance(zero); replacement = replacement with { Fee = replacement.Fee! with { Id = Guid.NewGuid(), Revision = 1 } };
        replacement = replacement with { Value = replacement.Value with { FeeTransactionId = replacement.Fee!.Id } };
        Invalid(() => file.Save(file.Snapshot, Patches(replacement), transfers: [Change(replacement)]));
        var moved = Advance(zero); moved = moved with { Fee = moved.Fee! with { AccountId = metadata.Accounts[5].Id } };
        Invalid(() => file.Save(file.Snapshot, Patches(moved), transfers: [Change(moved)]));
        var tombstone = Advance(zero);
        tombstone = tombstone with { Value = tombstone.Value with { IsDeleted = true }, Debit = tombstone.Debit with { IsDeleted = true }, Credit = tombstone.Credit with { IsDeleted = true }, Fee = tombstone.Fee! with { IsDeleted = true } };
        file.Save(file.Snapshot, Patches(tombstone), transfers: [Change(tombstone)]);
        Assert.AreEqual(tombstone, file.ReadTransfer(original.Value.Id)); Assert.AreEqual(1L, file.Snapshot.TransferCount);
        Assert.IsNull(file.GetTransaction(fee.Id)); Assert.IsTrue(file.GetTransaction(fee.Id, true)!.IsDeleted);
        Assert.AreEqual(original.Value.Id, file.FindTransferId(fee.Id));
        Assert.AreEqual(3L, file.Snapshot.OperationCount);
    }

    [TestMethod]
    public void ExistingOrdinaryOperationsCannotBeCapturedAsNewTransferLegsAndLegsCannotBeShared()
    {
        var metadata = Document(); var first = Details(metadata, 0, 1); var second = Details(metadata, 0, 1);
        using var file = Create(metadata, first);
        var ordinary = Operation(metadata.Accounts[0], TransactionDirection.Debit, 12.34m, "Opération autonome");
        file.Save(file.Snapshot, [new(ordinary, 0)]);
        var stolen = second with { Debit = Next(ordinary), Value = second.Value with { DebitTransactionId = ordinary.Id } };
        Invalid(() => file.Save(file.Snapshot, Patches(stolen), transfers: [Change(stolen)]));
        stolen = second with { Debit = Next(first.Debit), Value = second.Value with { DebitTransactionId = first.Debit.Id } };
        Invalid(() => file.Save(file.Snapshot, Patches(stolen), transfers: [Change(stolen)]));
        var duplicate = second with { Value = second.Value with { FeeTransactionId = second.Debit.Id }, Fee = second.Debit };
        Invalid(() => file.Save(file.Snapshot, Patches(duplicate), transfers: [Change(duplicate)]));
        file.Save(file.Snapshot, Patches(second), transfers: [Change(second)]);
        Assert.AreEqual(2L, file.Snapshot.TransferCount);
        Assert.AreNotEqual(first.Value.Id, second.Value.Id);
    }

    [TestMethod]
    public void PointingIsIndependentAndHiddenCreditOrFeeRBlocksWholeTransferUntilSeparateReleaseSave()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4);
        original = original with { Credit = original.Credit with { Status = ReconciliationStatus.Reconciled }, Fee = original.Fee! with { Status = ReconciliationStatus.Reconciled } };
        using var file = Create(metadata, original);
        var pointed = Next(original.Debit with { Status = ReconciliationStatus.Marked, MarkedOn = Today });
        file.Save(file.Snapshot, [new(pointed, 1)]);
        var current = file.ReadTransfer(original.Value.Id);
        Assert.AreEqual(1L, current.Value.Revision); Assert.AreEqual(2L, current.Debit.Revision);
        Assert.AreEqual(ReconciliationStatus.Marked, current.Debit.Status);
        Assert.AreEqual(ReconciliationStatus.Reconciled, current.Credit.Status);
        Invalid(() => file.Save(file.Snapshot, Patches(Advance(current)), transfers: [Change(Advance(current))]));
        Release(file, current.Credit);
        current = file.ReadTransfer(original.Value.Id);
        Invalid(() => file.Save(file.Snapshot, Patches(Advance(current)), transfers: [Change(Advance(current))]));
        Release(file, current.Fee!);
        current = file.ReadTransfer(original.Value.Id);
        var edited = Advance(current) with { Debit = Next(current.Debit with { Amount = 15m }) };
        file.Save(file.Snapshot, Patches(edited), transfers: [Change(edited)]);
        Assert.AreEqual(ReconciliationStatus.Marked, file.ReadTransfer(original.Value.Id).Debit.Status);
        Assert.AreEqual(Today, file.ReadTransfer(original.Value.Id).Debit.MarkedOn);
        Assert.AreEqual(2L, file.Snapshot.ReconciliationEventCount);
        Invalid(() => file.Save(file.Snapshot, [new(Next(edited.Debit with { IsDeleted = true }), edited.Debit.Revision)]));
        var stale = Advance(edited);
        var unpointed = Next(edited.Debit with { Status = ReconciliationStatus.Unmarked, MarkedOn = null });
        file.Save(file.Snapshot, [new(unpointed, edited.Debit.Revision)]);
        Invalid(() => file.Save(file.Snapshot, Patches(stale), transfers: [Change(stale)]));
        Assert.AreEqual(2L, file.ReadTransfer(original.Value.Id).Value.Revision);
    }

    [TestMethod]
    public void FormalReconciliationSnapshotsStayV1AndReopeningMustCommitBeforeTransferEdit()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4); using var file = Create(metadata, original);
        var session = Validate(file, original.Credit);
        var current = file.ReadTransfer(original.Value.Id);
        Assert.AreEqual(1L, current.Value.Revision); Assert.AreEqual(ReconciliationStatus.Unmarked, current.Debit.Status);
        Invalid(() => file.Save(file.Snapshot, Patches(Advance(current)), transfers: [Change(Advance(current))]));
        var reopening = Reopening(file, session.Id);
        var after = current with { Credit = reopening.Changes.Single().Value };
        var edited = Advance(after);
        Invalid(() => file.Save(file.Snapshot, Patches(edited), reopening.Batch, [Change(edited)]));
        file.Save(file.Snapshot, reopening.Changes, reopening.Batch);
        current = file.ReadTransfer(original.Value.Id); edited = Advance(current);
        file.Save(file.Snapshot, Patches(edited), transfers: [Change(edited)]);
        var archived = file.ReadReconciliation(session.Id);
        Assert.AreEqual(original.Credit, archived.Entries.Single().Original);
        using var raw = Raw(FileName);
        Assert.AreEqual(1L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationEntries WHERE snapshot_version=1 AND snapshot_json NOT LIKE '%transfer%';"));
    }

    [TestMethod]
    public void PageLinksContainOnlyVisibleRowsAcrossPagesAndPreserveIndependentStatusOverlays()
    {
        var metadata = Document(); var transfers = Enumerable.Range(0, 205).Select(_ => Details(metadata, 0, 2)).ToArray();
        using var file = MonetaFile.Create(FileName, metadata, transfers.SelectMany(Patches).ToArray(), transfers.Select(Change).ToArray());
        var page = file.ReadPage(metadata.Accounts[0], 100, 100, []);
        Assert.AreEqual(100, page.TransferIds.Count); Assert.AreEqual(205L, page.TotalCount);
        CollectionAssert.AreEquivalent(page.Transactions.Select(value => value.Id).ToArray(), page.TransferIds.Keys.ToArray());
        foreach (var row in page.Transactions) Assert.AreEqual(file.FindTransferId(row.Id), page.TransferIds[row.Id]);
        var changed = Next(page.Transactions[0] with { Status = ReconciliationStatus.Marked, MarkedOn = Today });
        var overlay = file.ReadPage(metadata.Accounts[0], 100, 100, [new(changed, 1)]);
        Assert.AreEqual(100, overlay.TransferIds.Count); Assert.AreEqual(page.TransferIds[changed.Id], overlay.TransferIds[changed.Id]);
        Assert.AreEqual(ReconciliationStatus.Marked, overlay.Transactions.Single(value => value.Id == changed.Id).Status);
        var last = file.ReadPage(metadata.Accounts[0], 200, 100, []);
        Assert.AreEqual(5, last.TransferIds.Count);
        Assert.AreEqual(0, file.ReadPage(metadata.Accounts[1], 0, 100, []).TransferIds.Count);
    }

    [DataTestMethod]
    [DataRow("DELETE FROM TransferLegs WHERE role=1;")]
    [DataRow("UPDATE TransferLegs SET transaction_id='11111111-1111-1111-1111-111111111111' WHERE role=1;")]
    [DataRow("UPDATE TransferLegs SET transfer_id='11111111-1111-1111-1111-111111111111' WHERE role=1;")]
    [DataRow("UPDATE Transfers SET is_deleted=1;")]
    [DataRow("UPDATE Transfers SET date='2026-01-05';")]
    [DataRow("UPDATE Transfers SET notes='Discordant';")]
    [DataRow("UPDATE Transfers SET revision=0;")]
    [DataRow("UPDATE Transfers SET exchange_date=NULL;")]
    [DataRow("UPDATE Transactions SET direction=1 WHERE transaction_id IN (SELECT transaction_id FROM TransferLegs WHERE role=0);")]
    public void FullReadRejectsIncompleteOrphanAndInconsistentTransferGraph(string mutation)
    {
        var metadata = Document(); using (Create(metadata, Details(metadata, 0, 2, 4))) { }
        using (var raw = Raw(FileName)) Execute(raw, "PRAGMA foreign_keys=OFF; PRAGMA ignore_check_constraints=ON;" + mutation);
        Assert.AreEqual(DocumentError.Corrupt, Assert.ThrowsException<DocumentException>(() => MonetaFile.Inspect(FileName)).Error);
    }

    [TestMethod]
    public void UniqueLegAndRoleConstraintsRejectDuplicateRelationships()
    {
        var metadata = Document(); var first = Details(metadata, 0, 2); var second = Details(metadata, 0, 2);
        using var file = MonetaFile.Create(FileName, metadata, Patches(first).Concat(Patches(second)).ToArray(), [Change(first), Change(second)]);
        using var raw = Raw(FileName); Execute(raw, "PRAGMA foreign_keys=ON;");
        Assert.ThrowsException<SqliteException>(() => Execute(raw, $"INSERT INTO TransferLegs VALUES('{first.Value.Id:D}',0,'{second.Credit.Id:D}');"));
        Assert.ThrowsException<SqliteException>(() => Execute(raw, $"INSERT INTO TransferLegs VALUES('{first.Value.Id:D}',2,'{second.Credit.Id:D}');"));
        Assert.ThrowsException<SqliteException>(() => Execute(raw, $"INSERT INTO TransferLegs VALUES('{first.Value.Id:D}',3,'{Guid.NewGuid():D}');"));
        Assert.AreEqual(2L, MonetaFile.Inspect(FileName).TransferCount);
    }

    [TestMethod]
    public void SameBalanceRelationshipTamperingAndMetadataChangesInvalidateExternalBaseline()
    {
        var metadata = Document(); var first = Details(metadata, 0, 2); var second = Details(metadata, 0, 2);
        using var file = MonetaFile.Create(FileName, metadata, Patches(first).Concat(Patches(second)).ToArray(), [Change(first), Change(second)]);
        var baseline = file.Snapshot; var temporary = Guid.NewGuid();
        using (var raw = Raw(FileName)) Execute(raw, "PRAGMA foreign_keys=OFF;" +
            $"UPDATE TransferLegs SET transfer_id='{temporary:D}' WHERE transfer_id='{first.Value.Id:D}' AND role=0;" +
            $"UPDATE TransferLegs SET transfer_id='{first.Value.Id:D}' WHERE transfer_id='{second.Value.Id:D}' AND role=0;" +
            $"UPDATE TransferLegs SET transfer_id='{second.Value.Id:D}' WHERE transfer_id='{temporary:D}' AND role=0;");
        var external = MonetaFile.Inspect(FileName);
        Assert.AreEqual(baseline.OperationCount, external.OperationCount);
        foreach (var account in metadata.Accounts) Assert.AreEqual(baseline.AccountBalances![account.Id], external.AccountBalances![account.Id]);
        Assert.AreNotEqual(baseline.LedgerDigest, external.LedgerDigest);
        Invalid(() => file.Save(baseline with { LedgerDigest = external.LedgerDigest, Name = "Ne pas écraser" }));
        Assert.AreSame(baseline, file.Snapshot);
        using (var raw = Raw(FileName)) Execute(raw, "UPDATE Transfers SET reference='Métadonnée externe';");
        Assert.AreNotEqual(external.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
    }

    [TestMethod]
    public void WriterFailureAndInterruptedSqliteTransactionPreserveDurableCompleteTransfer()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4); var edit = Advance(original);
        using (var file = Create(metadata, original))
        {
            var baseline = file.Snapshot;
            using (var raw = Raw(FileName))
            using (var transaction = raw.BeginTransaction())
            {
                using var command = raw.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "UPDATE Document SET name='Non publié'; UPDATE Transfers SET reference='Non publié'; UPDATE Transactions SET amount_minor=999;";
                command.ExecuteNonQuery();
                Assert.AreEqual(DocumentError.Locked, Assert.ThrowsException<DocumentException>(() => file.Save(baseline, Patches(edit), transfers: [Change(edit)])).Error);
                Assert.AreSame(baseline, file.Snapshot);
                // Disposal models an interrupted, uncommitted SQLite writer; no manual journal deletion.
            }
            Assert.AreEqual(original, file.ReadTransfer(original.Value.Id));
            Assert.AreEqual(baseline.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
            file.Save(file.Snapshot, Patches(edit), transfers: [Change(edit)]);
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(edit, reopened.ReadTransfer(original.Value.Id));
        Assert.AreEqual(2L, reopened.ReadTransfer(original.Value.Id).Value.Revision);
    }

    [TestMethod]
    public void V4MigrationPreservesOperationsReopenedStatementsAndLegacyReleaseOnOriginalCopy()
    {
        var metadata = Document(); var ordinary = Operation(metadata.Accounts[0], TransactionDirection.Credit, 1.23m, "Historique synthétique");
        var legacy = Operation(metadata.Accounts[1], TransactionDirection.Credit, 2.34m, "R hérité") with { Status = ReconciliationStatus.Reconciled };
        Guid sessionId; string entryJson; string historyJson;
        using (var source = MonetaFile.Create(FileName, metadata, [new(ordinary, 0), new(legacy, 0)]))
        {
            var session = Validate(source, ordinary); sessionId = session.Id;
            var reopening = Reopening(source, session.Id); source.Save(source.Snapshot, reopening.Changes, reopening.Batch);
            Release(source, legacy);
            using var raw = Raw(FileName);
            entryJson = (string)Scalar(raw, "SELECT snapshot_json FROM ReconciliationEntries;");
            historyJson = (string)Scalar(raw, "SELECT snapshot_json FROM ReconciliationHistory WHERE kind=2;");
        }
        // A real v4 schema: additive v5 tables absent, exact v4 DDL and all 7B data retained.
        using (var raw = Raw(FileName)) Execute(raw, "DROP TABLE TransferLegs; DROP TABLE Transfers; UPDATE Document SET schema_version=4,min_reader=4,min_writer=4; PRAGMA user_version=4; UPDATE MigrationHistory SET version=4,migration_id='create-v4',definition='native-accounts-v4';");
        var bytes = File.ReadAllBytes(FileName);
        Assert.AreEqual(DocumentError.MigrationRequired, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(FileName)).Error);
        var destination = Path.Combine(directory, "migrated.moneta");
        using (var migrated = MonetaBackup.Migrate(FileName, destination))
        {
            Assert.AreEqual(5, migrated.Snapshot.SchemaVersion); Assert.AreEqual(0L, migrated.Snapshot.TransferCount);
            Assert.AreEqual(2L, migrated.Snapshot.OperationCount); Assert.AreEqual(1L, migrated.Snapshot.ReconciliationCount); Assert.AreEqual(3L, migrated.Snapshot.ReconciliationEventCount);
            Assert.AreEqual(ReconciliationSessionState.Reopened, migrated.ReadReconciliation(sessionId).Session.State);
            Assert.AreEqual(ReconciliationStatus.Unmarked, migrated.GetTransaction(legacy.Id)!.Status);
            Assert.AreEqual(metadata.ModifiedUtc, migrated.Snapshot.ModifiedUtc);
            using var raw = Raw(destination);
            Assert.AreEqual(entryJson, Scalar(raw, "SELECT snapshot_json FROM ReconciliationEntries;"));
            Assert.AreEqual(historyJson, Scalar(raw, "SELECT snapshot_json FROM ReconciliationHistory WHERE kind=2;"));
        }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
        using (var raw = Raw(destination)) Execute(raw, "PRAGMA user_version=6;");
        var future = File.ReadAllBytes(destination);
        Assert.AreEqual(DocumentError.UnsupportedVersion, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(destination)).Error);
        CollectionAssert.AreEqual(future, File.ReadAllBytes(destination));
    }

    [TestMethod]
    public void FailedCreationNeverPublishesHalfTransferOrTemporaryDatabase()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4);
        Invalid(() => MonetaFile.Create(FileName, metadata, [new(original.Debit, 0)], [Change(original)]));
        Assert.IsFalse(File.Exists(FileName));
        Assert.AreEqual(0, Directory.EnumerateFiles(directory, ".*.tmp*").Count());
        using var file = Create(metadata, original);
        Assert.AreEqual(original, file.ReadTransfer(original.Value.Id));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(4)]
    public void ArchivedSourceDestinationAndFeeAccountsRejectWholeBatch(int archivedAccount)
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4);
        using var file = MonetaFile.Create(FileName, metadata);
        var archived = metadata with
        {
            Accounts = metadata.Accounts.Select((account, index) => index == archivedAccount ? account with { IsArchived = true } : account).ToArray()
        };
        Invalid(() => file.Save(archived, Patches(original), transfers: [Change(original)]));
        Assert.AreEqual(0L, file.Snapshot.OperationCount); Assert.AreEqual(0L, file.Snapshot.TransferCount);
        Assert.IsFalse(MonetaFile.Inspect(FileName).Accounts[archivedAccount].IsArchived);
    }

    [TestMethod]
    public void MetadataAndCommonLegPayloadCommitTogetherWithoutChangingIndependentPointing()
    {
        var metadata = Document(); var original = Details(metadata, 0, 2, 4);
        original = original with { Debit = original.Debit with { Status = ReconciliationStatus.Marked, MarkedOn = Today } };
        TransferDetails edited;
        using (var file = Create(metadata, original))
        {
            var baseline = file.Snapshot; edited = Advance(original);
            var date = Date.AddDays(1); const string notes = "Correction synthétique 日本語";
            edited = edited with
            {
                Value = edited.Value with { Date = date, Reference = "Référence corrigée", Notes = notes, ExchangeDate = date, RateSource = "Source libre déclarée" },
                Debit = edited.Debit with { Date = date, Details = notes, Amount = 15m },
                Credit = edited.Credit with { Date = date, Details = notes, Amount = 25m },
                Fee = edited.Fee! with { Date = date, Details = notes, Amount = 2m }
            };
            Assert.AreEqual(original, file.ReadTransfer(original.Value.Id));
            Assert.AreEqual(baseline.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
            file.Save(file.Snapshot, Patches(edited), transfers: [Change(edited)]);
            Assert.AreEqual(edited, file.ReadTransfer(original.Value.Id));
            Assert.AreEqual(Today, file.GetTransaction(original.Debit.Id)!.MarkedOn);
            Assert.AreNotEqual(baseline.LedgerDigest, file.Snapshot.LedgerDigest);
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(edited, reopened.ReadTransfer(original.Value.Id));
    }

    private MonetaFile Create(FinancialDocument metadata, TransferDetails transfer) => MonetaFile.Create(FileName, metadata, Patches(transfer), [Change(transfer)]);
    private static FinancialDocument Document()
    {
        var currencies = new[] { new Currency("EUR", "Euro synthétique", 2), new Currency("BYN", "Rouble biélorusse synthétique", 2), new Currency("RUB", "Rouble russe synthétique", 2), new Currency("XTS", "Précision synthétique", 28) };
        var accounts = currencies.SelectMany(currency => Enumerable.Range(0, 2).Select(index => new Account(Guid.NewGuid(), currency.Code + " synthétique " + index, currency, 0m) { CreatedUtc = Created, ModifiedUtc = Created })).ToArray();
        return new(Guid.NewGuid(), "Virements synthétiques", Created, Created, currencies, accounts) { SelectedAccountId = accounts[0].Id };
    }
    private static TransferDetails Details(FinancialDocument metadata, int source, int target, int? feeAccount = null)
    {
        var same = metadata.Accounts[source].Currency.Code == metadata.Accounts[target].Currency.Code;
        var debit = Operation(metadata.Accounts[source], TransactionDirection.Debit, source >= 6 ? 0.0000000000000000000000000001m : 12.34m, "Virement émis");
        var credit = Operation(metadata.Accounts[target], TransactionDirection.Credit, target >= 6 ? 0.0000000000000000000000000002m : same ? debit.Amount : 23.45m, "Virement reçu");
        var fee = feeAccount is { } account ? Operation(metadata.Accounts[account], TransactionDirection.Debit, account >= 6 ? 0.0000000000000000000000000001m : 1.23m, "Frais de virement") : null;
        return new(new(Guid.NewGuid(), debit.Id, credit.Id, fee?.Id, Date, "Référence synthétique", "Notes synthétiques", same ? null : Date, same ? null : "Montants saisis par l’utilisateur", Created, Created), debit, credit, fee);
    }
    private static Transaction Operation(Account account, TransactionDirection direction, decimal amount, string label) => new(Guid.NewGuid(), account.Id, Date, label, new(""), new(""), amount, account.Currency, direction, ReconciliationStatus.Unmarked, "Notes synthétiques") { CreatedUtc = Created, ModifiedUtc = Created };
    private static Transaction Next(Transaction value) => value with { Revision = checked(value.Revision + 1), ModifiedUtc = DateTimeOffset.UtcNow > value.ModifiedUtc ? DateTimeOffset.UtcNow : value.ModifiedUtc.AddTicks(1) };
    private static TransferDetails Advance(TransferDetails value)
    {
        var now = DateTimeOffset.UtcNow > value.Value.ModifiedUtc ? DateTimeOffset.UtcNow : value.Value.ModifiedUtc.AddTicks(1);
        return value with { Value = value.Value with { Revision = checked(value.Value.Revision + 1), ModifiedUtc = now }, Debit = Next(value.Debit), Credit = Next(value.Credit), Fee = value.Fee is null ? null : Next(value.Fee) };
    }
    private static IEnumerable<Transaction> Legs(TransferDetails value) { yield return value.Debit; yield return value.Credit; if (value.Fee is not null) yield return value.Fee; }
    private static TransactionChange[] Patches(TransferDetails value) => Legs(value).Select(transaction => new TransactionChange(transaction, transaction.Revision - 1)).ToArray();
    private static TransferChange Change(TransferDetails value) => new(value.Value, value.Value.Revision - 1);
    private static void Release(MonetaFile file, Transaction original)
    {
        var released = Next(original with { Status = ReconciliationStatus.Unmarked, MarkedOn = null, ReconciliationId = null });
        var audit = new ReconciliationEvent(Guid.NewGuid(), null, original.Id, ReconciliationEventKind.LegacyReleased, released.ModifiedUtc, "Libération synthétique explicite") { OperationBefore = original };
        file.Save(file.Snapshot, [new(released, original.Revision)], new([], [audit]));
    }
    private static ReconciliationSession Validate(MonetaFile file, Transaction original)
    {
        var account = file.Snapshot.Accounts.Single(value => value.Id == original.AccountId);
        var preview = file.ReadReconciliationCandidates(account, Date, new HashSet<Guid>(), 0, 100);
        var closing = checked(preview.OpeningBalance + FinancialRules.SignedAmount(original));
        var now = DateTimeOffset.UtcNow;
        var session = new ReconciliationSession(Guid.NewGuid(), account.Id, Date, "Relevé synthétique", preview.OpeningBalance, closing, closing, preview.PreviousSessionId, now);
        var operation = Next(original with { Status = ReconciliationStatus.Reconciled, MarkedOn = Today, ReconciliationId = session.Id });
        var batch = new ReconciliationBatch([new(session, 0, [new(session.Id, original)])], [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Validated, now, "")]);
        file.Save(file.Snapshot, [new(operation, original.Revision)], batch);
        return session;
    }
    private static (ReconciliationBatch Batch, TransactionChange[] Changes) Reopening(MonetaFile file, Guid id)
    {
        var details = file.ReadReconciliation(id); var now = DateTimeOffset.UtcNow;
        var session = details.Session with { State = ReconciliationSessionState.Reopened, Revision = 2, ReopenedUtc = now, ReopenReason = "Correction synthétique explicite" };
        var changes = details.Entries.Select(entry => { var current = file.GetTransaction(entry.Original.Id)!; return new TransactionChange(Next(current with { Status = entry.Original.Status, MarkedOn = entry.Original.MarkedOn, ReconciliationId = null }), current.Revision); }).ToArray();
        return (new([new(session, 1, [])], [new(Guid.NewGuid(), id, null, ReconciliationEventKind.Reopened, now, session.ReopenReason!)]), changes);
    }
    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3 }.ToString());
        connection.Open(); return connection;
    }
    private static void Execute(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static object Scalar(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar()!; }
    private static void Invalid(Action action) => Assert.AreEqual(DocumentError.InvalidData, Assert.ThrowsException<DocumentException>(action).Error);
}
