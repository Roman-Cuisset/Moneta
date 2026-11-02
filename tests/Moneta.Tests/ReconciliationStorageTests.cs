using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class ReconciliationStorageTests
{
    private string directory = null!;
    private string FileName => Path.Combine(directory, "pointing.moneta");
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonetaPointingStorage-" + Guid.NewGuid().ToString("N"))).FullName;
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [TestMethod]
    public void PointingDatesAndGeneratedTotalsRoundTripWithoutInventingLegacyDates()
    {
        var metadata = Document();
        var rows = new[]
        {
            Operation(metadata, 1.11m),
            Operation(metadata, 2.22m) with { Status = ReconciliationStatus.Marked, MarkedOn = new(2026, 1, 5) },
            Operation(metadata, 3.33m) with { Status = ReconciliationStatus.Marked },
            Operation(metadata, 4.44m) with { Status = ReconciliationStatus.Reconciled }
        };
        using (var file = Create(metadata, rows))
        {
            Assert.AreEqual(111.10m, file.Snapshot.AccountBalances![rows[0].AccountId]);
            Assert.AreEqual(109.99m, file.Snapshot.AccountPointedBalances![rows[0].AccountId]);
            var page = file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>());
            Assert.AreEqual(new AccountLedgerTotals(111.10m, 109.99m, 1.11m), page.Totals);
            Assert.IsNull(file.GetTransaction(rows[2].Id)!.MarkedOn);
            Assert.IsNull(file.GetTransaction(rows[3].Id)!.MarkedOn);
            Assert.IsNull(file.GetTransaction(rows[3].Id)!.ReconciliationId);
            Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<Guid, decimal>)file.Snapshot.AccountPointedBalances!).Clear());
            var marked = Next(rows[0] with { Status = ReconciliationStatus.Marked, MarkedOn = Today });
            file.Save(file.Snapshot, [new(marked, 1)]);
            Assert.AreEqual(marked, file.GetTransaction(marked.Id));
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(new DateOnly(2026, 1, 5), reopened.GetTransaction(rows[1].Id)!.MarkedOn);
        Assert.IsNull(reopened.GetTransaction(rows[2].Id)!.MarkedOn);
        Assert.AreEqual(0L, reopened.Snapshot.ReconciliationCount);
    }

    [TestMethod]
    public void NewPointingRejectsFutureDatesAndFutureOperationsButPreservesExistingFlags()
    {
        var metadata = Document();
        var unmarked = Operation(metadata) with { Date = Today.AddDays(1) };
        var legacy = Operation(metadata) with { Date = Today.AddDays(1), Status = ReconciliationStatus.Marked };
        using var file = Create(metadata, unmarked, legacy);
        var before = file.Snapshot;
        Invalid(() => file.Save(before, [new(Next(unmarked with { Status = ReconciliationStatus.Marked, MarkedOn = Today }), 1)]));
        Invalid(() => file.Save(before, [new(Next(legacy with { MarkedOn = Today.AddDays(1) }), 1)]));
        Invalid(() => file.Save(before, [new(Next(legacy with { Status = ReconciliationStatus.Unmarked, MarkedOn = Today }), 1)]));
        var intentionalFinancialEdit = Next(legacy with { Amount = 7.89m, Date = Today.AddDays(2) });
        file.Save(before, [new(intentionalFinancialEdit, 1)]);
        Assert.AreEqual(ReconciliationStatus.Marked, file.GetTransaction(legacy.Id)!.Status);
        Assert.IsNull(file.GetTransaction(legacy.Id)!.MarkedOn);
    }

    [TestMethod]
    public void PointedTotalsUseExactDecimalLightStreamsAndPendingOverlayAcrossPages()
    {
        var metadata = Document();
        var rows = Enumerable.Range(0, 253).Select(index => Operation(metadata, 0.01m) with
        {
            Id = Guid.Parse(index.ToString("x8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000001"),
            Date = new DateOnly(2026, 1, 1).AddDays(index % 17),
            Status = index % 2 == 0 ? ReconciliationStatus.Marked : ReconciliationStatus.Unmarked,
            MarkedOn = index % 2 == 0 ? new DateOnly(2026, 1, 2) : null
        }).ToArray();
        using var file = Create(metadata, rows);
        var edit = Next(rows[1] with { Status = ReconciliationStatus.Marked, MarkedOn = Today });
        var pending = new TransactionChange[] { new(edit, 1) };
        var first = file.ReadPage(metadata.Accounts[0], 0, 100, pending);
        var last = file.ReadPage(metadata.Accounts[0], 200, 100, pending);
        Assert.AreEqual(100, first.Transactions.Count); Assert.AreEqual(53, last.Transactions.Count);
        Assert.AreEqual(new AccountLedgerTotals(102.53m, 101.28m, 1.25m), first.Totals);
        Assert.AreEqual(first.Totals, last.Totals);
        Assert.AreEqual(first.Totals!.CurrentBalance - first.Totals.PointedBalance, first.Totals.UnmarkedAmount);
        Assert.AreEqual(101.27m, file.Snapshot.AccountPointedBalances![rows[0].AccountId]);
        var huge = Operation(metadata, 92233720368547758.07m) with { Status = ReconciliationStatus.Marked, MarkedOn = Today };
        var second = huge with { Id = Guid.NewGuid() };
        file.Save(file.Snapshot, [new(huge, 0), new(second, 0)]);
        var totals = file.ReadPage(metadata.Accounts[0], 0, 1, Array.Empty<TransactionChange>()).Totals!;
        Assert.AreEqual(184467440737095617.41m, totals.PointedBalance);
        Assert.AreEqual(184467440737095618.67m, totals.CurrentBalance);
    }

    [TestMethod]
    public void CandidatePagesValidateEverySelectedIdAndIncludeOldOutstandingRows()
    {
        var metadata = Document();
        var rows = Enumerable.Range(0, 205).Select(index => Operation(metadata, index % 3 == 0 ? 0.03m : 0.01m) with
        { Date = new DateOnly(2026, 1, 1).AddDays(index % 12) }).ToArray();
        var cross = Operation(metadata) with { AccountId = metadata.Accounts[1].Id };
        var future = Operation(metadata) with { Date = new DateOnly(2026, 2, 1) };
        using var file = Create(metadata, rows.Concat([cross, future]).ToArray());
        var ids = rows.Select(value => value.Id).ToHashSet();
        var first = file.ReadReconciliationCandidates(metadata.Accounts[0], new(2026, 1, 31), ids, 0, 100);
        var last = file.ReadReconciliationCandidates(metadata.Accounts[0], new(2026, 1, 31), ids, 200, 100);
        Assert.AreEqual(205L, first.TotalCount); Assert.AreEqual(100, first.Transactions.Count); Assert.AreEqual(5, last.Transactions.Count);
        Assert.AreEqual(205L, first.SelectedCount); Assert.AreEqual(rows.Sum(value => value.Amount), first.SelectedNet);
        foreach (var invalid in new[] { Guid.NewGuid(), cross.Id, future.Id })
            Invalid(() => file.ReadReconciliationCandidates(metadata.Accounts[0], new(2026, 1, 31), new HashSet<Guid> { rows[0].Id, invalid }, 0, 100));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => file.ReadReconciliationCandidates(metadata.Accounts[0], Today, ids, 0, 101));
        CollectionAssert.AreEquivalent(rows.Select(value => value.Id).ToArray(), file.ReadTransactions(ids.ToArray()).Select(value => value.Id).ToArray());
        Invalid(() => file.ReadTransactions([Guid.NewGuid()]));
    }

    [TestMethod]
    public void ValidationReopeningAndRevalidationRetainImmutableSnapshotsAndHistory()
    {
        var metadata = Document();
        var original = Operation(metadata) with { Status = ReconciliationStatus.Marked, MarkedOn = new(2026, 1, 3) };
        var twin = original with { Id = Guid.NewGuid() };
        Guid firstId; string finalDigest;
        using (var file = Create(metadata, original, twin))
        {
            var validation = Validation(file, new(2026, 1, 31), original, twin); firstId = validation.Session.Id;
            file.Save(file.Snapshot, validation.Changes, validation.Batch);
            var current = file.GetTransaction(original.Id)!;
            Assert.AreEqual(ReconciliationStatus.Reconciled, current.Status); Assert.AreEqual(firstId, current.ReconciliationId);
            Assert.AreEqual(original.MarkedOn, current.MarkedOn); Assert.AreEqual(2L, current.Revision);
            Assert.AreEqual(1L, file.Snapshot.ReconciliationCount); Assert.AreEqual(1L, file.Snapshot.ReconciliationEventCount);
            var details = file.ReadReconciliation(firstId);
            CollectionAssert.AreEquivalent(new[] { original, twin }, details.Entries.Select(value => value.Original).ToArray());
            var reopening = Reopening(file, firstId);
            file.Save(file.Snapshot, reopening.Changes, reopening.Batch);
            var restored = file.GetTransaction(original.Id)!;
            Assert.AreEqual(original.Status, restored.Status); Assert.AreEqual(original.MarkedOn, restored.MarkedOn);
            Assert.IsNull(restored.ReconciliationId); Assert.AreEqual(3L, restored.Revision);
            Assert.AreEqual(ReconciliationSessionState.Reopened, file.ReadReconciliation(firstId).Session.State);
            Assert.AreEqual(2, file.ReadReconciliation(firstId).Events.Count);
            var edited = Next(restored with { Amount = 2.34m, Label = "Correction après réouverture" });
            file.Save(file.Snapshot, [new(edited, restored.Revision)]);
            Assert.AreEqual(original, file.ReadReconciliation(firstId).Entries.Single(value => value.Original.Id == original.Id).Original);
            var again = Validation(file, new(2026, 1, 31), edited, file.GetTransaction(twin.Id)!);
            file.Save(file.Snapshot, again.Changes, again.Batch);
            Assert.AreNotEqual(firstId, again.Session.Id);
            Assert.AreEqual(2L, file.Snapshot.ReconciliationCount); Assert.AreEqual(3L, file.Snapshot.ReconciliationEventCount);
            var history = file.ReadReconciliationHistory(metadata.Accounts[0].Id, 0, 1);
            Assert.AreEqual(2L, history.TotalCount); Assert.AreEqual(1, history.Sessions.Count);
            Assert.AreEqual(1, file.ReadReconciliationHistory(metadata.Accounts[0].Id, 1, 1).Sessions.Count);
            finalDigest = file.Snapshot.LedgerDigest!;
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(finalDigest, reopened.Snapshot.LedgerDigest);
        Assert.AreEqual(original, reopened.ReadReconciliation(firstId).Entries.Single(value => value.Original.Id == original.Id).Original);
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public void SaveAfterMidnightPreservesStagedLocalDatesExistingDatesAndSession(int localDayOffset)
    {
        var validatedUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var validationDate = DateOnly.FromDateTime(validatedUtc.UtcDateTime);
        var stagedDate = validationDate.AddDays(localDayOffset);
        var metadata = Document();
        var unmarked = Operation(metadata);
        var unknownDate = Operation(metadata) with { Status = ReconciliationStatus.Marked };
        var dated = Operation(metadata) with { Status = ReconciliationStatus.Marked, MarkedOn = validationDate.AddDays(-30) };
        ReconciliationSession session;
        TransactionChange[] staged;
        using (var file = Create(metadata, unmarked, unknownDate, dated))
        {
            var validation = ValidationAt(file, validationDate.AddDays(-2), validatedUtc, stagedDate, unmarked, unknownDate, dated);
            session = validation.Session; staged = validation.Changes;
            file.Save(file.Snapshot, staged, validation.Batch);
            Assert.AreEqual(stagedDate, file.GetTransaction(unmarked.Id)!.MarkedOn);
            Assert.AreEqual(stagedDate, file.GetTransaction(unknownDate.Id)!.MarkedOn);
            Assert.AreEqual(dated.MarkedOn, file.GetTransaction(dated.Id)!.MarkedOn);
            var details = file.ReadReconciliation(session.Id);
            Assert.AreEqual(session, details.Session);
            Assert.AreEqual(validatedUtc, details.Events.Single().OccurredUtc);
            CollectionAssert.AreEquivalent(new[] { unmarked, unknownDate, dated }, details.Entries.Select(value => value.Original).ToArray());
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(session, reopened.ReadReconciliation(session.Id).Session);
        foreach (var change in staged) Assert.AreEqual(change.Value, reopened.GetTransaction(change.Value.Id));
    }

    [TestMethod]
    public void OvernightDraftRejectsMissingDistantOrRewrittenMarkingDatesAtomically()
    {
        var validatedUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var validationDate = DateOnly.FromDateTime(validatedUtc.UtcDateTime);
        var metadata = Document();
        var unmarked = Operation(metadata);
        var dated = Operation(metadata) with { Status = ReconciliationStatus.Marked, MarkedOn = validationDate.AddDays(-30) };
        using var file = Create(metadata, unmarked, dated);
        var validation = ValidationAt(file, validationDate.AddDays(-2), validatedUtc, validationDate, unmarked, dated);
        var baseline = file.Snapshot;
        var undatedPatch = validation.Changes[0]; var datedPatch = validation.Changes[1];
        var invalid = new TransactionChange[][]
        {
            [undatedPatch with { Value = undatedPatch.Value with { MarkedOn = null } }, datedPatch],
            [undatedPatch with { Value = undatedPatch.Value with { MarkedOn = validationDate.AddDays(-2) } }, datedPatch],
            [undatedPatch with { Value = undatedPatch.Value with { MarkedOn = validationDate.AddDays(2) } }, datedPatch],
            [undatedPatch, datedPatch with { Value = datedPatch.Value with { MarkedOn = validationDate } }]
        };
        foreach (var changes in invalid)
        {
            Invalid(() => file.Save(baseline with { Name = "Ne doit pas être commis" }, changes, validation.Batch));
            Assert.AreSame(baseline, file.Snapshot);
            Assert.AreEqual(unmarked, file.GetTransaction(unmarked.Id)); Assert.AreEqual(dated, file.GetTransaction(dated.Id));
            var persisted = MonetaFile.Inspect(FileName);
            Assert.AreEqual(baseline.Name, persisted.Name);
            Assert.AreEqual(baseline.LedgerDigest, persisted.LedgerDigest);
            using var raw = Raw(FileName);
            Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM Reconciliations;"));
            Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationEntries;"));
            Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationHistory;"));
        }
        file.Save(baseline, validation.Changes, validation.Batch);
        Assert.AreEqual(validation.Session, file.ReadReconciliation(validation.Session.Id).Session);
    }

    [TestMethod]
    public void CreationRejectsFormalSessionReferencesWithoutAnAlreadySavedSource()
    {
        var metadata = Document();
        var attested = Operation(metadata) with { Status = ReconciliationStatus.Reconciled, ReconciliationId = Guid.NewGuid(), MarkedOn = Today };
        Invalid(() => MonetaFile.Create(FileName, metadata, [new(attested, 0)]));
        Assert.IsFalse(File.Exists(FileName));
    }

    [TestMethod]
    public void EmptyBalancedStatementsChainExactlyAndOnlyLatestCanReopen()
    {
        var metadata = Document(); var oldOutstanding = Operation(metadata);
        using var file = Create(metadata, oldOutstanding);
        var first = Validation(file, new(2026, 1, 10)); file.Save(file.Snapshot, first.Changes, first.Batch);
        var second = Validation(file, new(2026, 1, 31), oldOutstanding); file.Save(file.Snapshot, second.Changes, second.Batch);
        Assert.AreEqual(first.Session.Id, second.Session.PreviousSessionId);
        Assert.AreEqual(first.Session.ExpectedClosingBalance, second.Session.OpeningBalance);
        var older = Reopening(file, first.Session.Id);
        Invalid(() => file.Save(file.Snapshot, older.Changes, older.Batch));
        var latest = Reopening(file, second.Session.Id); file.Save(file.Snapshot, latest.Changes, latest.Batch);
        older = Reopening(file, first.Session.Id); file.Save(file.Snapshot, older.Changes, older.Batch);
        Assert.AreEqual(2L, file.Snapshot.ReconciliationCount); Assert.AreEqual(4L, file.Snapshot.ReconciliationEventCount);
        Assert.IsNull(file.ReadReconciliationCandidates(metadata.Accounts[0], Today, new HashSet<Guid>(), 0, 100).PreviousSessionId);
    }

    [TestMethod]
    public void EveryReconciledRowRejectsOrdinaryFinancialDeletePointingAndDateChanges()
    {
        var metadata = Document(); var first = Operation(metadata);
        using var file = Create(metadata, first);
        var validation = Validation(file, new(2026, 1, 31), first); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        var current = file.GetTransaction(first.Id)!; var before = file.Snapshot;
        foreach (var invalid in ProtectedMutations(current)) Invalid(() => file.Save(before, [new(invalid, current.Revision)]));
        Assert.AreSame(before, file.Snapshot); Assert.AreEqual(current, file.GetTransaction(first.Id));
        var reopening = Reopening(file, validation.Session.Id);
        var money = reopening.Changes.Select(change => change with { Value = change.Value with { Amount = 999m } }).ToArray();
        Invalid(() => file.Save(before, money, reopening.Batch));
        Assert.AreEqual(ReconciliationSessionState.Validated, file.ReadReconciliation(validation.Session.Id).Session.State);
    }

    [TestMethod]
    public void LegacyReleaseRequiresReasonAndExactAuditedSnapshotAndNeverChangesMoney()
    {
        var metadata = Document(); var legacy = Operation(metadata) with { Status = ReconciliationStatus.Reconciled };
        using var file = Create(metadata, legacy);
        foreach (var invalid in ProtectedMutations(legacy)) Invalid(() => file.Save(file.Snapshot, [new(invalid, 1)]));
        var blocked = Validation(file, new(2026, 1, 31));
        Invalid(() => file.Save(file.Snapshot, blocked.Changes, blocked.Batch));
        var released = Next(legacy with { Status = ReconciliationStatus.Unmarked });
        var audit = new ReconciliationEvent(Guid.NewGuid(), null, legacy.Id, ReconciliationEventKind.LegacyReleased, released.ModifiedUtc, "Relevé historique indisponible") { OperationBefore = legacy };
        Invalid(() => file.Save(file.Snapshot, [new(released, 1)], new([], [audit with { Reason = " " }])));
        Invalid(() => file.Save(file.Snapshot, [new(released with { Amount = 5m }, 1)], new([], [audit])));
        Invalid(() => file.Save(file.Snapshot, [new(released, 1)], new([], [audit with { OperationBefore = legacy with { Label = "Faux" } }])));
        file.Save(file.Snapshot, [new(released, 1)], new([], [audit]));
        Assert.AreEqual(legacy.Amount, file.GetTransaction(legacy.Id)!.Amount); Assert.IsNull(file.GetTransaction(legacy.Id)!.MarkedOn);
        Assert.AreEqual(1L, file.Snapshot.ReconciliationEventCount); Assert.AreEqual(0L, file.Snapshot.ReconciliationCount);
        var validation = Validation(file, new(2026, 1, 31), released); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        Assert.AreEqual(1L, file.Snapshot.ReconciliationCount);
        using var raw = Raw(FileName);
        Assert.AreEqual(1L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationHistory WHERE kind=2 AND snapshot_version=1 AND snapshot_json IS NOT NULL;"));
    }

    [TestMethod]
    public void SessionHistoryPermanentlyFreezesOpeningBalanceEvenAfterReopening()
    {
        var metadata = Document(); using var file = Create(metadata);
        var validation = Validation(file, new(2026, 1, 31)); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        var opening = file.Snapshot with { Accounts = file.Snapshot.Accounts.Select(account => account.Id == metadata.Accounts[0].Id ? account with { OpeningBalance = 101m } : account).ToArray() };
        Invalid(() => file.Save(opening));
        var reopening = Reopening(file, validation.Session.Id); file.Save(file.Snapshot, reopening.Changes, reopening.Batch);
        Invalid(() => file.Save(opening));
        Assert.AreEqual(100m, file.Snapshot.Accounts[0].OpeningBalance);
    }

    [TestMethod]
    public void SessionValidationRejectsFalseZeroWrongMembershipDuplicateAndStaleSnapshotsAtomically()
    {
        var metadata = Document(); var value = Operation(metadata); var cross = Operation(metadata) with { AccountId = metadata.Accounts[1].Id };
        using var file = Create(metadata, value, cross);
        var valid = Validation(file, new(2026, 1, 31), value); var baseline = file.Snapshot;
        var change = valid.Batch.Sessions[0];
        var invalidChanges = new[]
        {
            change with { Value = valid.Session with { ExpectedClosingBalance = 100m, CalculatedBalance = 100m } },
            change with { Value = valid.Session with { OpeningBalance = 0m, ExpectedClosingBalance = value.Amount, CalculatedBalance = value.Amount } },
            change with { Entries = [new(valid.Session.Id, cross)] },
            change with { Entries = [change.Entries[0], change.Entries[0]] },
            change with { Entries = [new(valid.Session.Id, value with { Revision = 7 })] },
            change with { Entries = [new(Guid.NewGuid(), value)] }
        };
        foreach (var invalid in invalidChanges)
        {
            Invalid(() => file.Save(baseline with { Name = "Ne doit pas être commis" }, valid.Changes, valid.Batch with { Sessions = [invalid] }));
            Assert.AreSame(baseline, file.Snapshot); Assert.AreEqual(value, file.GetTransaction(value.Id));
            Assert.AreEqual(0L, MonetaFile.Inspect(FileName).ReconciliationCount);
        }
        Invalid(() => file.Save(baseline, valid.Changes, valid.Batch with { Events = [] }));
        Invalid(() => file.Save(baseline, [], valid.Batch));
        file.Save(baseline, valid.Changes, valid.Batch);
        Invalid(() => file.Save(file.Snapshot, valid.Changes, valid.Batch));
        Assert.AreEqual(1L, file.Snapshot.ReconciliationCount);
    }

    [TestMethod]
    public void FailedLaterRowRollsBackSessionEntriesAuditMetadataAndEarlierPatch()
    {
        var metadata = Document(); var first = Operation(metadata); var other = Operation(metadata);
        using var file = Create(metadata, first, other);
        var validation = Validation(file, new(2026, 1, 31), first); var baseline = file.Snapshot;
        var stale = Next(other) with { Revision = 3 };
        Invalid(() => file.Save(baseline with { Name = "Nom non commis" }, validation.Changes.Append(new(stale, 2)).ToArray(), validation.Batch));
        Assert.AreSame(baseline, file.Snapshot); Assert.AreEqual(first, file.GetTransaction(first.Id));
        using var raw = Raw(FileName);
        Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM Reconciliations;"));
        Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationEntries;"));
        Assert.AreEqual(0L, Scalar(raw, "SELECT COUNT(*) FROM ReconciliationHistory;"));
    }

    [TestMethod]
    public void WriterLockPreservesFullSessionDraftAndAllowsExactRetry()
    {
        var metadata = Document(); var first = Operation(metadata); using var file = Create(metadata, first);
        var validation = Validation(file, new(2026, 1, 31), first); var baseline = file.Snapshot;
        using (var raw = Raw(FileName))
        using (var transaction = raw.BeginTransaction())
        {
            using var command = raw.CreateCommand(); command.Transaction = transaction; command.CommandText = "UPDATE Document SET name='Verrou';"; command.ExecuteNonQuery();
            Assert.AreEqual(DocumentError.Locked, Assert.ThrowsException<DocumentException>(() => file.Save(baseline, validation.Changes, validation.Batch)).Error);
            Assert.AreSame(baseline, file.Snapshot); Assert.AreEqual(0L, file.Snapshot.ReconciliationCount);
            transaction.Rollback();
        }
        file.Save(baseline, validation.Changes, validation.Batch);
        Assert.AreEqual(1L, file.Snapshot.ReconciliationCount);
    }

    [TestMethod]
    public void CatalogueRenamePreservesHistoricalNamesAndReopeningFinancialIdentity()
    {
        var metadata = Document(); var value = Operation(metadata); using var file = Create(metadata, value);
        var validation = Validation(file, new(2026, 1, 31), value); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        file.Save(file.Snapshot with { Payees = [metadata.Payees[0] with { Name = "Nom actuel" }] });
        Assert.AreEqual("Nom actuel", file.GetTransaction(value.Id)!.Payee.Name);
        Assert.AreEqual("Tiers historique", file.ReadReconciliation(validation.Session.Id).Entries[0].Original.Payee.Name);
        var reopening = Reopening(file, validation.Session.Id); file.Save(file.Snapshot, reopening.Changes, reopening.Batch);
        Assert.AreEqual("Nom actuel", file.GetTransaction(value.Id)!.Payee.Name);
        Assert.AreEqual("Tiers historique", file.ReadReconciliation(validation.Session.Id).Entries[0].Original.Payee.Name);
    }

    [DataTestMethod]
    [DataRow("UPDATE Reconciliations SET expected_minor=expected_minor+1,calculated_minor=calculated_minor+1;")]
    [DataRow("UPDATE Reconciliations SET opening_minor=opening_minor+1,expected_minor=expected_minor+1,calculated_minor=calculated_minor+1;")]
    [DataRow("DELETE FROM ReconciliationEntries;")]
    [DataRow("DELETE FROM ReconciliationHistory;")]
    [DataRow("UPDATE Transactions SET reconciliation_id=NULL;")]
    [DataRow("UPDATE Transactions SET amount_minor=amount_minor+1;")]
    [DataRow("UPDATE Transactions SET marked_on=NULL;")]
    [DataRow("UPDATE ReconciliationEntries SET snapshot_json=replace(snapshot_json,'\"Version\":1','\"Version\":2');")]
    [DataRow("UPDATE ReconciliationEntries SET snapshot_json=replace(snapshot_json,'\"AmountMinor\":123','\"AmountMinor\":123.1');")]
    [DataRow("UPDATE ReconciliationEntries SET snapshot_json=substr(snapshot_json,1,length(snapshot_json)-1)||',\"Unexpected\":0}';")]
    [DataRow("UPDATE ReconciliationHistory SET occurred_utc='2026-01-01T12:00:00.0000000+00:00';")]
    public void FullReadRejectsCorruptSnapshotsMembershipFalseZeroAndHistoryWithoutRewriting(string mutation)
    {
        var metadata = Document(); var value = Operation(metadata);
        using (var file = Create(metadata, value))
        { var validation = Validation(file, new(2026, 1, 31), value); file.Save(file.Snapshot, validation.Changes, validation.Batch); }
        using (var raw = Raw(FileName)) Execute(raw, "PRAGMA foreign_keys=OFF; PRAGMA ignore_check_constraints=ON;" + mutation);
        var bytes = File.ReadAllBytes(FileName);
        Assert.AreEqual(DocumentError.Corrupt, Assert.ThrowsException<DocumentException>(() => MonetaFile.Inspect(FileName)).Error);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
    }

    [DataTestMethod]
    [DataRow("UPDATE Transactions SET marked_on='2026-02-30';")]
    [DataRow("UPDATE Transactions SET status=0;")]
    [DataRow("UPDATE Transactions SET reconciliation_id='11111111-1111-1111-1111-111111111111';")]
    [DataRow("UPDATE Reconciliations SET previous_session_id=session_id;")]
    [DataRow("UPDATE Reconciliations SET native_code='JPY';")]
    [DataRow("UPDATE Reconciliations SET revision=2;")]
    [DataRow("UPDATE ReconciliationEntries SET snapshot_version=2;")]
    [DataRow("UPDATE ReconciliationEntries SET transaction_id='11111111-1111-1111-1111-111111111111';")]
    [DataRow("UPDATE ReconciliationHistory SET kind=2;")]
    public void NewStrictSqlConstraintsAndForeignKeysRejectInvalidWrites(string mutation)
    {
        var metadata = Document(); var value = Operation(metadata); using var file = Create(metadata, value);
        var validation = Validation(file, new(2026, 1, 31), value); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        using var raw = Raw(FileName); Execute(raw, "PRAGMA foreign_keys=ON;");
        Assert.ThrowsException<SqliteException>(() => Execute(raw, mutation));
        Assert.AreEqual(1L, MonetaFile.Inspect(FileName).ReconciliationCount);
    }

    [DataTestMethod]
    [DataRow("DROP INDEX Reconciliations_AccountDate;")]
    [DataRow("DROP INDEX ReconciliationEntries_Transaction;")]
    [DataRow("CREATE INDEX UnexpectedHistory ON ReconciliationHistory(reason);")]
    public void ReconciliationSchemaUsesExactVersionedIndexWhitelist(string mutation)
    {
        using (Create(Document())) { }
        using (var raw = Raw(FileName)) Execute(raw, mutation);
        Assert.AreEqual(DocumentError.InvalidFormat, Assert.ThrowsException<DocumentException>(() => MonetaFile.Inspect(FileName)).Error);
    }

    [TestMethod]
    public void ExternalHistoricalChangesInvalidateSavedBaselineEvenWithForgedCallerDigest()
    {
        var metadata = Document(); var value = Operation(metadata); using var file = Create(metadata, value);
        var validation = Validation(file, new(2026, 1, 31), value); file.Save(file.Snapshot, validation.Changes, validation.Batch);
        var reopening = Reopening(file, validation.Session.Id); file.Save(file.Snapshot, reopening.Changes, reopening.Batch);
        var baseline = file.Snapshot;
        using (var raw = Raw(FileName)) Execute(raw, "UPDATE ReconciliationEntries SET snapshot_json=replace(snapshot_json,'Achat synth','Autre synth');");
        var external = MonetaFile.Inspect(FileName);
        Assert.AreNotEqual(baseline.LedgerDigest, external.LedgerDigest);
        Invalid(() => file.Save(baseline with { LedgerDigest = external.LedgerDigest, Name = "Ne pas écraser" }));
        Assert.AreSame(baseline, file.Snapshot);
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ActualSchemasOneTwoAndThreeMigrateOnlyOnCopiesAndPreserveLegacyRWithoutDates(int version)
    {
        var metadata = Document() with { SchemaVersion = version };
        var legacy = Operation(metadata) with { Status = ReconciliationStatus.Reconciled };
        var rows = version == 3 ? new[] { legacy, legacy with { Id = Guid.NewGuid(), Status = ReconciliationStatus.Marked } } : Array.Empty<Transaction>();
        BackupRestoreTests.BuildLegacy(FileName, metadata, version, rows);
        var before = File.ReadAllBytes(FileName);
        Assert.AreEqual(DocumentError.MigrationRequired, Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(FileName)).Error);
        using var migrated = MonetaBackup.Migrate(FileName, Path.Combine(directory, "migrated.moneta"));
        Assert.AreEqual(5, migrated.Snapshot.SchemaVersion);
        Assert.AreEqual(metadata.Id, migrated.Snapshot.Id); Assert.AreEqual(metadata.CreatedUtc, migrated.Snapshot.CreatedUtc);
        Assert.AreEqual(metadata.ModifiedUtc, migrated.Snapshot.ModifiedUtc);
        Assert.AreEqual((long)rows.Length, migrated.Snapshot.OperationCount);
        Assert.AreEqual(0L, migrated.Snapshot.ReconciliationCount); Assert.AreEqual(0L, migrated.Snapshot.ReconciliationEventCount);
        foreach (var row in rows)
        {
            Assert.AreEqual(row, migrated.GetTransaction(row.Id)); Assert.IsNull(migrated.GetTransaction(row.Id)!.MarkedOn);
            Assert.IsNull(migrated.GetTransaction(row.Id)!.ReconciliationId);
        }
        CollectionAssert.AreEqual(before, File.ReadAllBytes(FileName));
    }

    [DataTestMethod]
    [DataRow("UPDATE Reconciliations SET previous_session_id=NULL,opening_minor=10000,expected_minor=10000,calculated_minor=10000 WHERE previous_session_id IS NOT NULL;")]
    [DataRow("UPDATE Reconciliations SET account_id=(SELECT account_id FROM Accounts ORDER BY ordinal DESC LIMIT 1) WHERE previous_session_id IS NOT NULL;")]
    [DataRow("UPDATE Reconciliations SET statement_date='2026-01-09' WHERE previous_session_id IS NOT NULL;")]
    public void FullReadRejectsActiveForksCrossAccountPredecessorsAndNonIncreasingStatementDates(string mutation)
    {
        var metadata = Document(); var value = Operation(metadata);
        using (var file = Create(metadata, value))
        {
            var first = Validation(file, new(2026, 1, 10), value); file.Save(file.Snapshot, first.Changes, first.Batch);
            var second = Validation(file, new(2026, 1, 31)); file.Save(file.Snapshot, second.Changes, second.Batch);
        }
        using (var raw = Raw(FileName)) Execute(raw, "PRAGMA foreign_keys=OFF; PRAGMA ignore_check_constraints=ON;" + mutation);
        Assert.AreEqual(DocumentError.Corrupt, Assert.ThrowsException<DocumentException>(() => MonetaFile.Inspect(FileName)).Error);
    }

    [TestMethod]
    public void StatementAndHistoricalSnapshotPreserveScaleTwentyEightAsIntegerMinorUnits()
    {
        var metadata = Document();
        var currency = new Currency("XTS", "Précision synthétique", 28);
        var account = new Account(Guid.NewGuid(), "Compte précis", currency, 0.0000000000000000000000000001m)
        { CreatedUtc = Created, ModifiedUtc = Created };
        metadata = metadata with { Currencies = [currency], Accounts = [account], SelectedAccountId = account.Id };
        var value = Operation(metadata, 0.0000000000000000000000000001m);
        using var file = Create(metadata, value);
        var validation = Validation(file, new(2026, 1, 31), value);
        file.Save(file.Snapshot, validation.Changes, validation.Batch);
        var details = file.ReadReconciliation(validation.Session.Id);
        Assert.AreEqual(0.0000000000000000000000000002m, details.Session.ExpectedClosingBalance);
        Assert.AreEqual(value.Amount, details.Entries[0].Original.Amount);
        using var raw = Raw(FileName);
        Assert.AreEqual(1L, Scalar(raw, "SELECT COUNT(*) FROM Reconciliations WHERE typeof(opening_minor)='integer' AND opening_minor=1 AND expected_minor=2 AND calculated_minor=2;"));
    }

    [TestMethod]
    public void NewStatementRejectsFutureOrNonIncreasingDatesAndLeavesAllDraftDataUncommitted()
    {
        var metadata = Document(); using var file = Create(metadata);
        var future = Validation(file, Today.AddDays(1));
        Invalid(() => file.Save(file.Snapshot, future.Changes, future.Batch));
        Assert.AreEqual(0L, file.Snapshot.ReconciliationCount);
        var first = Validation(file, new(2026, 1, 31)); file.Save(file.Snapshot, first.Changes, first.Batch);
        var duplicateDate = Validation(file, new(2026, 1, 31));
        Invalid(() => file.Save(file.Snapshot, duplicateDate.Changes, duplicateDate.Batch));
        Assert.AreEqual(1L, file.Snapshot.ReconciliationCount);
    }

    private MonetaFile Create(FinancialDocument metadata, params Transaction[] rows) => MonetaFile.Create(FileName, metadata, rows.Select(value => new TransactionChange(value, 0)).ToArray());
    private static FinancialDocument Document()
    {
        var euro = new Currency("EUR", "Euro synthétique", 2); var yen = new Currency("JPY", "Yen synthétique", 0);
        var account = new Account(Guid.NewGuid(), "Compte synthétique", euro, 100m) { CreatedUtc = Created, ModifiedUtc = Created };
        return new(Guid.NewGuid(), "Pointage synthétique", Created, Created, [euro, yen], [account, account with { Id = Guid.NewGuid(), Name = "Autre compte" }])
        { SelectedAccountId = account.Id, Payees = [new(Guid.NewGuid(), "Tiers historique")] };
    }
    private static Transaction Operation(FinancialDocument metadata, decimal amount = 1.23m) => new(Guid.NewGuid(), metadata.Accounts[0].Id,
        new(2026, 1, 4), "Achat synthétique", new(metadata.Payees[0].Name), new(""), amount, metadata.Currencies[0], TransactionDirection.Credit, ReconciliationStatus.Unmarked, "Notes")
    { CreatedUtc = Created, ModifiedUtc = Created, PayeeId = metadata.Payees[0].Id };
    private static Transaction Next(Transaction value) => value with { Revision = checked(value.Revision + 1), ModifiedUtc = DateTimeOffset.UtcNow > value.ModifiedUtc ? DateTimeOffset.UtcNow : value.ModifiedUtc.AddTicks(1) };
    private static IEnumerable<Transaction> ProtectedMutations(Transaction value)
    {
        yield return Next(value with { Label = "Interdit" }); yield return Next(value with { Amount = 7.89m });
        yield return Next(value with { Direction = TransactionDirection.Debit }); yield return Next(value with { Date = value.Date.AddDays(1) });
        yield return Next(value with { IsDeleted = true, ReconciliationId = null });
        yield return Next(value with { Status = ReconciliationStatus.Unmarked, MarkedOn = null, ReconciliationId = null });
        yield return Next(value with { MarkedOn = Today.AddDays(-1) });
    }
    private static (ReconciliationBatch Batch, TransactionChange[] Changes, ReconciliationSession Session) Validation(MonetaFile file, DateOnly date, params Transaction[] originals) =>
        ValidationAt(file, date, DateTimeOffset.UtcNow, Today, originals);

    private static (ReconciliationBatch Batch, TransactionChange[] Changes, ReconciliationSession Session) ValidationAt(
        MonetaFile file, DateOnly date, DateTimeOffset validatedUtc, DateOnly markedOn, params Transaction[] originals)
    {
        var account = file.Snapshot.Accounts[0];
        var preview = file.ReadReconciliationCandidates(account, date, new HashSet<Guid>(), 0, 1);
        var calculated = checked(preview.OpeningBalance + originals.Sum(value => value.Direction == TransactionDirection.Credit ? value.Amount : -value.Amount));
        var session = new ReconciliationSession(Guid.NewGuid(), account.Id, date, "Relevé synthétique", preview.OpeningBalance, calculated, calculated, preview.PreviousSessionId, validatedUtc);
        var entries = originals.Select(value => new ReconciliationEntry(session.Id, value)).ToArray();
        var changes = originals.Select(value => new TransactionChange(value with
        {
            Status = ReconciliationStatus.Reconciled, ReconciliationId = session.Id, MarkedOn = value.MarkedOn ?? markedOn,
            Revision = checked(value.Revision + 1), ModifiedUtc = validatedUtc
        }, value.Revision)).ToArray();
        return (new([new(session, 0, entries)], [new(Guid.NewGuid(), session.Id, null, ReconciliationEventKind.Validated, validatedUtc, "")]), changes, session);
    }
    private static (ReconciliationBatch Batch, TransactionChange[] Changes) Reopening(MonetaFile file, Guid id)
    {
        var details = file.ReadReconciliation(id); var now = DateTimeOffset.UtcNow;
        if (now < details.Session.ValidatedUtc) now = details.Session.ValidatedUtc;
        var session = details.Session with { State = ReconciliationSessionState.Reopened, Revision = 2, ReopenedUtc = now, ReopenReason = "Correction explicite" };
        var changes = details.Entries.Select(entry =>
        {
            var current = file.GetTransaction(entry.Original.Id)!;
            return new TransactionChange(Next(current with { Status = entry.Original.Status, MarkedOn = entry.Original.MarkedOn, ReconciliationId = null }), current.Revision);
        }).ToArray();
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
