using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.Tests;

[TestClass]
public sealed class ReconciliationDomainTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static readonly DateOnly Day = Today.AddDays(-10);
    private static readonly Currency Euro = new("EUR", "Euro", 2);

    [TestMethod]
    public void PointingBatchesUsePageTotalsAndNeverRereadSource()
    {
        var fixture = new Fixture(10m);
        fixture.Add(2m, TransactionDirection.Credit);
        fixture.Add(3m, TransactionDirection.Debit);
        var model = fixture.Open();
        var reads = fixture.Source.PageReads;
        var rows = model.Rows;
        var first = rows[0];
        model.SelectedRow = first;
        model.SetPointing(Selections(model), true);
        Assert.AreEqual(reads, fixture.Source.PageReads);
        Assert.AreEqual(9m, model.SelectedAccount!.Balance);
        Assert.AreEqual(9m, model.PointedBalance);
        Assert.AreEqual(0m, model.UnmarkedAmount);
        Assert.AreEqual(first.Balance, model.Rows[0].Balance);
        Assert.AreEqual(first.Transaction.Id, model.SelectedRow!.Transaction.Id);
        Assert.IsTrue(model.Rows.All(row => row.Transaction.MarkedOn == Today));
        Assert.IsTrue(model.PendingChanges.All(change => change.ExpectedRevision == 1 && change.Value.Revision == 2));
        model.TogglePointing(Selections(model));
        Assert.AreEqual(10m, model.PointedBalance);
        Assert.AreEqual(-1m, model.UnmarkedAmount);
        Assert.IsTrue(model.Rows.All(row => row.Transaction.MarkedOn is null));
        Assert.AreEqual(reads, fixture.Source.PageReads);
        Assert.IsTrue(model.PendingChanges.All(change => change.Value.Revision == 2));
    }

    [TestMethod]
    public void PagedTotalsIncludeOffPageRowsAndRebasePendingPointingOnNavigation()
    {
        var fixture = new Fixture(7m);
        for (var index = 0; index < 205; index++)
            fixture.Add(1m, TransactionDirection.Credit, index == 204 ? ReconciliationStatus.Marked : ReconciliationStatus.Unmarked);
        var model = fixture.Open();
        Assert.AreEqual(100, model.Rows.Count);
        Assert.AreEqual(8m, model.PointedBalance);
        Assert.AreEqual(204m, model.UnmarkedAmount);
        var reads = fixture.Source.PageReads;
        fixture.Source.FailPage = true;
        model.SetPointing([Selection(model.Rows[0])], true);
        Assert.AreEqual(reads, fixture.Source.PageReads);
        Assert.AreEqual(9m, model.PointedBalance);
        fixture.Source.FailPage = false;
        model.NextPage();
        Assert.AreEqual(9m, model.PointedBalance);
        model.SetPointing([Selection(model.Rows[0])], true);
        Assert.AreEqual(10m, model.PointedBalance);
        model.PreviousPage();
        Assert.AreEqual(ReconciliationStatus.Marked, model.Rows[0].Transaction.Status);
        Assert.AreEqual(10m, model.PointedBalance);
        Assert.AreEqual(202m, model.UnmarkedAmount);
        Assert.AreEqual(212m, model.SelectedAccount!.Balance);
    }

    [TestMethod]
    public void NewDocumentPendingInsertPointingKeepsInsertionRevisionAndDemoIsReadOnly()
    {
        var fixture = new Fixture(7m);
        var model = new MainViewModel(fixture.Document);
        model.AddTransaction(new(Day, 1m, TransactionDirection.Credit, "Brouillon"));
        model.SetPointing(Selections(model), true);
        Assert.AreEqual(8m, model.PointedBalance);
        Assert.AreEqual(0m, model.UnmarkedAmount);
        Assert.AreEqual(0L, model.PendingChanges.Single().ExpectedRevision);
        Assert.AreEqual(1L, model.PendingChanges.Single().Value.Revision);
        var value = model.Rows[0].Transaction;
        var demo = new MainViewModel(new DemoPortfolio([fixture.Account], [value]));
        Assert.AreEqual(8m, demo.PointedBalance);
        Assert.ThrowsException<InvalidOperationException>(() => demo.TogglePointing(Selections(demo)));
        Assert.IsFalse(demo.CanReconcile);
    }

    [TestMethod]
    public void MixedToggleAndZeroAmountsAreIndividualExactTransitions()
    {
        var fixture = new Fixture(5m);
        fixture.Add(0m, TransactionDirection.Credit);
        fixture.Add(2m, TransactionDirection.Credit, ReconciliationStatus.Marked, Day);
        var model = fixture.Open();
        model.TogglePointing(Selections(model));
        Assert.AreEqual(ReconciliationStatus.Marked, model.Rows[0].Transaction.Status);
        Assert.AreEqual(ReconciliationStatus.Unmarked, model.Rows[1].Transaction.Status);
        Assert.AreEqual(5m, model.PointedBalance);
        Assert.AreEqual(2m, model.UnmarkedAmount);
        Assert.AreEqual(2, model.PendingChanges.Count);
    }

    [TestMethod]
    public void InvalidMemberRejectsWholeBatchWithoutChangingRowsOrSelection()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit);
        fixture.Add(1m, TransactionDirection.Credit, ReconciliationStatus.Reconciled);
        var model = fixture.Open();
        model.SelectedRow = model.Rows[0];
        var rows = model.Rows;
        var selected = model.SelectedRow;
        Assert.ThrowsException<InvalidOperationException>(() => model.SetPointing(Selections(model), true));
        Assert.AreSame(rows, model.Rows);
        Assert.AreSame(selected, model.SelectedRow);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.IsFalse(model.IsDirty);
        var selection = new TransactionSelection(rows[0].Transaction.Id, rows[0].Transaction.Revision);
        Assert.ThrowsException<ArgumentException>(() => model.SetPointing([selection, selection], true));
        Assert.ThrowsException<InvalidOperationException>(() => model.SetPointing([selection with { Revision = 999 }], true));
        Assert.ThrowsException<InvalidOperationException>(() => model.SetPointing([new(Guid.NewGuid(), 1)], true));
        Assert.AreSame(rows, model.Rows);
    }

    [TestMethod]
    public void NewFuturePointingAndFutureMarkDatesRejectButLegacyFlagsRemainReadable()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit, date: Today.AddDays(1));
        fixture.Add(2m, TransactionDirection.Credit, ReconciliationStatus.Marked, null, Today.AddDays(2));
        var model = fixture.Open();
        Assert.ThrowsException<InvalidOperationException>(() => model.SetPointing([Selection(model.Rows[0])], true));
        var legacy = model.Rows[1].Transaction;
        Assert.IsNull(legacy.MarkedOn);
        Assert.ThrowsException<ArgumentException>(() => model.ChangeMarkingDate(Selection(model.Rows[1]), Today.AddDays(1)));
        model.SetPointing([Selection(model.Rows[1])], false);
        Assert.AreEqual(ReconciliationStatus.Unmarked, model.Rows[1].Transaction.Status);
        Assert.AreEqual(ReconciliationStatus.Unmarked, model.Rows[0].Transaction.Status);
    }

    [TestMethod]
    public void MarkDateCanPrecedeBookingAndF11PreservesOriginalDateAndSelection()
    {
        var fixture = new Fixture();
        fixture.Add(2m, TransactionDirection.Debit);
        var model = fixture.Open();
        model.SetPointing(Selections(model), true);
        model.ChangeMarkingDate(Selection(model.Rows[0]), Day.AddDays(-2));
        model.SelectedRow = model.Rows[0];
        var originalText = model.Rows[0].DateText;
        model.ShowMarkingDates = true;
        Assert.AreEqual("Date de pointage", model.DateColumnHeader);
        Assert.AreEqual(model.Rows[0].MarkedOnText, model.Rows[0].DisplayDateText);
        Assert.AreNotEqual(originalText, model.Rows[0].DisplayDateText);
        Assert.AreEqual(originalText, model.Rows[0].DateText);
        Assert.IsNotNull(model.SelectedRow);
        model.ShowMarkingDates = false;
        Assert.AreEqual(originalText, model.Rows[0].DisplayDateText);
    }

    [TestMethod]
    public void MarkedFinancialEditPreservesPointingFieldsAndReconciledRowsAreProtected()
    {
        var fixture = new Fixture();
        fixture.Add(2m, TransactionDirection.Credit, ReconciliationStatus.Marked, Day);
        fixture.Add(3m, TransactionDirection.Credit, ReconciliationStatus.Reconciled);
        var model = fixture.Open();
        var original = model.Rows[0].Transaction;
        var edited = model.EditTransaction(original.Id, original.Revision, new TransactionDraft(Day.AddDays(1), 4m, TransactionDirection.Debit, "Modifié"));
        Assert.AreEqual(original.Status, edited.Status);
        Assert.AreEqual(original.MarkedOn, edited.MarkedOn);
        Assert.AreEqual(-4m + 3m, model.PointedBalance);
        var reconciled = model.Rows.Single(row => row.Transaction.Status == ReconciliationStatus.Reconciled).Transaction;
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransaction(reconciled.Id, reconciled.Revision, new(Day, 1m, TransactionDirection.Debit, "Interdit")));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(reconciled.Id, reconciled.Revision));
        Assert.ThrowsException<InvalidOperationException>(() => model.ChangeMarkingDate(new(reconciled.Id, reconciled.Revision), Day));
    }

    [TestMethod]
    public void SessionValidationStagesSnapshotsAndKeepsDurableTruthUntilSaved()
    {
        var fixture = new Fixture(10m);
        fixture.Add(2m, TransactionDirection.Credit);
        fixture.Add(3m, TransactionDirection.Debit, ReconciliationStatus.Marked, Day.AddDays(-1));
        var model = fixture.Open();
        var originals = model.Rows.Select(row => row.Transaction).ToArray();
        var session = model.StageReconciliation(new(Day, 9m, "Relevé 1"), originals.Select(value => value.Id).ToArray());
        Assert.IsTrue(model.IsDirty);
        Assert.IsFalse(model.CanReconcile);
        Assert.AreEqual(0, fixture.Source.Sessions.Count);
        Assert.IsTrue(fixture.Source.Transactions.All(value => value.Status != ReconciliationStatus.Reconciled));
        Assert.AreEqual(1, model.PendingReconciliations.Sessions.Count);
        Assert.AreEqual(1, model.PendingReconciliations.Events.Count);
        Assert.AreEqual(0L, model.PendingReconciliations.Sessions[0].ExpectedRevision);
        CollectionAssert.AreEqual(originals, model.PendingReconciliations.Sessions[0].Entries.Select(entry => entry.Original).ToArray());
        Assert.IsTrue(model.Rows.All(row => row.Transaction.Status == ReconciliationStatus.Reconciled && row.Transaction.ReconciliationId == session.Id));
        Assert.AreEqual(9m, model.PointedBalance);
        Assert.ThrowsException<InvalidOperationException>(() => model.StageReconciliation(new(Day, 9m), []));
        fixture.Save(model);
        Assert.IsFalse(model.IsDirty);
        Assert.IsTrue(model.CanReconcile);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.AreEqual(1, fixture.Source.Sessions.Count);
    }

    [TestMethod]
    public void EmptyBalancedStatementAndIdenticalLegitimateOperationsAreAccepted()
    {
        var fixture = new Fixture(10m);
        var model = fixture.Open();
        var empty = model.StageReconciliation(new(Day, 10m), []);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.AreEqual(0, model.PendingReconciliations.Sessions[0].Entries.Count);
        fixture.Save(model);
        fixture.Add(1m, TransactionDirection.Credit, date: Day.AddDays(1));
        fixture.Add(1m, TransactionDirection.Credit, date: Day.AddDays(1));
        model = fixture.Open();
        var session = model.StageReconciliation(new(Day.AddDays(1), 12m), model.Rows.Select(row => row.Transaction.Id).ToArray());
        Assert.AreEqual(empty.Id, session.PreviousSessionId);
        Assert.AreEqual(2, model.PendingReconciliations.Sessions[0].Entries.Count);
    }

    [TestMethod]
    public void InvalidSelectionGapDatesPrecisionAndDirtyWorkspaceDoNotStageAnything()
    {
        var fixture = new Fixture(10m);
        fixture.Add(1m, TransactionDirection.Credit);
        var model = fixture.Open();
        var id = model.Rows[0].Transaction.Id;
        var rows = model.Rows;
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Day, 12m), [id]));
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Today.AddDays(1), 11m), [id]));
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Day, 11.001m), [id]));
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Day, 11m), [id, id]));
        Assert.ThrowsException<InvalidOperationException>(() => model.StageReconciliation(new(Day, 11m), [Guid.NewGuid()]));
        Assert.AreSame(rows, model.Rows);
        Assert.IsFalse(model.IsDirty);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        model.SetPointing(Selections(model), true);
        Assert.ThrowsException<InvalidOperationException>(() => model.StageReconciliation(new(Day, 11m), [id]));
    }

    [TestMethod]
    public void ReadFailuresRetainBothPendingBatchesAndCurrentView()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit);
        var model = fixture.Open();
        var rows = model.Rows;
        var id = rows[0].Transaction.Id;
        fixture.Source.FailTransactions = true;
        Assert.ThrowsException<IOException>(() => model.StageReconciliation(new(Day, 1m), [id]));
        Assert.AreSame(rows, model.Rows);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.AreEqual(0, model.PendingChanges.Count);
        fixture.Source.FailTransactions = false;
        fixture.Source.FailPage = true;
        Assert.ThrowsException<IOException>(() => model.StageReconciliation(new(Day, 1m), [id]));
        Assert.AreSame(rows, model.Rows);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        fixture.Source.FailPage = false;
        model.StageReconciliation(new(Day, 1m), [id]);
        var staged = model.PendingReconciliations;
        // No save callback means the draft must remain intact after a failed SQL operation.
        Assert.AreSame(staged, model.PendingReconciliations);
        Assert.AreEqual(1, model.PendingChanges.Count);
        fixture.Save(model, failRefresh: true);
        Assert.IsFalse(model.IsDirty);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.AreEqual(0, model.PendingChanges.Count);
        StringAssert.Contains(model.LedgerErrorText, "Enregistrement effectué");
    }

    [TestMethod]
    public void ReopenRestoresOnlyOriginalPointingAndRetainsHistoryThenRevalidationUsesNewId()
    {
        var fixture = new Fixture(10m);
        fixture.Add(1m, TransactionDirection.Credit);
        fixture.Add(2m, TransactionDirection.Credit, ReconciliationStatus.Marked, Day);
        var model = fixture.Open();
        var ids = model.Rows.Select(row => row.Transaction.Id).ToArray();
        var first = model.StageReconciliation(new(Day, 13m), ids);
        fixture.Save(model);
        model.ReopenReconciliation(first.Id, 1, "Correction explicite");
        Assert.AreEqual(ReconciliationStatus.Unmarked, model.Rows[0].Transaction.Status);
        Assert.IsNull(model.Rows[0].Transaction.MarkedOn);
        Assert.AreEqual(ReconciliationStatus.Marked, model.Rows[1].Transaction.Status);
        Assert.AreEqual(Day, model.Rows[1].Transaction.MarkedOn);
        Assert.IsTrue(model.Rows.All(row => row.Transaction.ReconciliationId is null && row.Transaction.Revision == 3));
        Assert.AreEqual(2, fixture.Source.Entries[first.Id].Count);
        fixture.Save(model);
        var again = model.StageReconciliation(new(Day, 13m), ids);
        Assert.AreNotEqual(first.Id, again.Id);
        fixture.Save(model);
        Assert.AreEqual(2, fixture.Source.Sessions.Count);
        Assert.AreEqual(3, fixture.Source.Events.Count);
        Assert.AreEqual(ReconciliationSessionState.Reopened, fixture.Source.Sessions[first.Id].State);
        Assert.IsNull(again.PreviousSessionId);
    }

    [TestMethod]
    public void ReopenEnforcesLatestActiveRevisionReasonAndUnchangedFinancialPayload()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit);
        var model = fixture.Open();
        var first = model.StageReconciliation(new(Day, 1m), model.Rows.Select(row => row.Transaction.Id).ToArray());
        fixture.Save(model);
        var second = model.StageReconciliation(new(Day.AddDays(1), 1m), []);
        fixture.Save(model);
        Assert.ThrowsException<InvalidOperationException>(() => model.ReopenReconciliation(first.Id, 1, "Ancien"));
        Assert.ThrowsException<InvalidOperationException>(() => model.ReopenReconciliation(second.Id, 2, "Périmé"));
        Assert.ThrowsException<ArgumentException>(() => model.ReopenReconciliation(second.Id, 1, " "));
        model.ReopenReconciliation(second.Id, 1, "Dernier d’abord");
        fixture.Save(model);
        var original = fixture.Source.Transactions[0];
        fixture.Source.Transactions[0] = original with { Amount = 2m };
        Assert.ThrowsException<InvalidOperationException>(() => model.ReopenReconciliation(first.Id, 1, "Contenu altéré"));
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        fixture.Source.Transactions[0] = original;
        var snapshot = fixture.Source.Entries[first.Id][0];
        fixture.Source.Entries[first.Id] = [snapshot with { Original = snapshot.Original with { Payee = new Payee("Ancien nom"), Category = new Category("Ancienne catégorie") } }];
        model.ReopenReconciliation(first.Id, 1, "Noms de catalogue modifiables");
        Assert.AreEqual(ReconciliationSessionState.Reopened, model.PendingReconciliations.Sessions[0].Value.State);
    }

    [TestMethod]
    public void LegacyReconciliationRequiresAuditedExplicitReleaseAndNeverInventsDate()
    {
        var fixture = new Fixture();
        fixture.Add(2m, TransactionDirection.Credit, ReconciliationStatus.Reconciled);
        var model = fixture.Open();
        var original = model.Rows[0].Transaction;
        StringAssert.Contains(model.Rows[0].MarkText, "hérité");
        Assert.AreEqual(string.Empty, model.Rows[0].MarkedOnText);
        Assert.ThrowsException<InvalidOperationException>(() => model.StageReconciliation(new(Day, 0m), []));
        Assert.ThrowsException<ArgumentException>(() => model.ReleaseLegacyReconciliation(Selection(model.Rows[0]), ""));
        model.ReleaseLegacyReconciliation(Selection(model.Rows[0]), "Ancien relevé introuvable");
        var change = model.PendingChanges.Single();
        Assert.AreEqual(ReconciliationStatus.Unmarked, change.Value.Status);
        Assert.IsNull(change.Value.MarkedOn);
        Assert.AreEqual(original.Amount, change.Value.Amount);
        Assert.AreEqual(original, model.PendingReconciliations.Events.Single().OperationBefore);
        Assert.AreEqual(ReconciliationEventKind.LegacyReleased, model.PendingReconciliations.Events.Single().Kind);
        fixture.Save(model);
        model.StageReconciliation(new(Day, 2m), [original.Id]);
        Assert.AreEqual(1, model.PendingReconciliations.Sessions.Count);
    }

    [TestMethod]
    public void NativePrecisionAndHistoricalComparisonNeverUseDisplayNamesOrForeignCurrency()
    {
        var fixture = new Fixture(currency: new Currency("KWD", "Dinar", 3));
        fixture.Add(0.001m, TransactionDirection.Credit);
        var model = fixture.Open();
        var original = model.Rows[0].Transaction;
        Assert.IsTrue(ReconciliationRules.FinancialPayloadEquals(original, original with { Currency = original.Currency with { Name = "Renommé" }, Payee = new("Renommé"), Category = new("Renommée"), PaymentMode = "Renommé" }));
        foreach (var changed in new[] { original with { Amount = 0.002m }, original with { Date = Day.AddDays(1) }, original with { Label = "Autre" }, original with { Details = "Autres notes" }, original with { PayeeId = Guid.NewGuid() }, original with { Currency = new("KWD", "Dinar", 2) } })
            Assert.IsFalse(ReconciliationRules.FinancialPayloadEquals(original, changed));
        model.StageReconciliation(new(Day, 0.001m), [original.Id]);
        Assert.AreEqual(0.001m, model.PointedBalance);
        Assert.AreEqual(0m, model.UnmarkedAmount);
    }

    [TestMethod]
    public void HistoricalRulesPermitClockRewindButRejectInexactAndDuplicateEntries()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit);
        var original = fixture.Source.Transactions[0];
        var session = new ReconciliationSession(Guid.NewGuid(), fixture.Account.Id, Today.AddDays(10), "", 0m, 1m, 1m, null, DateTimeOffset.UtcNow.AddDays(10));
        var entry = new ReconciliationEntry(session.Id, original);
        ReconciliationRules.ValidateSession(session, fixture.Account, null, [entry]);
        Assert.ThrowsException<ArgumentException>(() => ReconciliationRules.ValidateSession(session, fixture.Account, null, [entry, entry]));
        Assert.ThrowsException<ArgumentException>(() => ReconciliationRules.ValidateSession(session with { ExpectedClosingBalance = 1.001m }, fixture.Account, null, [entry]));
        Assert.ThrowsException<ArgumentException>(() => ReconciliationRules.ValidateSession(session with { OpeningBalance = 1m }, fixture.Account, null, [entry]));
    }

    [TestMethod]
    public void ReadValidationPreservesLegacyUnknownAndFutureDatesButRejectsInconsistentFields()
    {
        var fixture = new Fixture();
        fixture.Add(1m, TransactionDirection.Credit);
        var original = fixture.Source.Transactions[0];
        FinancialRules.ValidateTransaction(original with { Status = ReconciliationStatus.Marked }, fixture.Document);
        FinancialRules.ValidateTransaction(original with { Status = ReconciliationStatus.Marked, MarkedOn = Today.AddDays(5) }, fixture.Document);
        FinancialRules.ValidateTransaction(original with { Status = ReconciliationStatus.Reconciled }, fixture.Document);
        FinancialRules.ValidateTransaction(original with { Status = ReconciliationStatus.Reconciled, IsDeleted = true }, fixture.Document);
        foreach (var invalid in new[]
        {
            original with { MarkedOn = Day },
            original with { Status = ReconciliationStatus.Marked, MarkedOn = default(DateOnly) },
            original with { ReconciliationId = Guid.NewGuid() },
            original with { Status = ReconciliationStatus.Reconciled, ReconciliationId = Guid.Empty },
            original with { Status = ReconciliationStatus.Reconciled, ReconciliationId = Guid.NewGuid(), IsDeleted = true }
        })
            Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateTransaction(invalid, fixture.Document));
    }

    [TestMethod]
    public void ActiveChainCannotForkOrRepeatStatementDate()
    {
        var fixture = new Fixture(5m);
        var model = fixture.Open();
        var first = model.StageReconciliation(new(Day, 5m), []);
        fixture.Save(model);
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Day, 5m), []));
        Assert.ThrowsException<ArgumentException>(() => model.StageReconciliation(new(Day.AddDays(-1), 5m), []));
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.IsFalse(model.IsDirty);
        var next = model.StageReconciliation(new(Day.AddDays(1), 5m), []);
        Assert.AreEqual(first.Id, next.PreviousSessionId);
        Assert.AreEqual(first.ExpectedClosingBalance, next.OpeningBalance);
    }

    [DataTestMethod]
    [DataRow("Validation")]
    [DataRow("EmptyValidation")]
    [DataRow("Reopen")]
    [DataRow("LegacyRelease")]
    public void ReconciliationDraftRequiresSaveBeforeAnyLedgerMutation(string action)
    {
        var fixture = new Fixture();
        fixture.Add(2m, TransactionDirection.Credit,
            action == "LegacyRelease" ? ReconciliationStatus.Reconciled : ReconciliationStatus.Unmarked);
        fixture.Add(3m, TransactionDirection.Credit, ReconciliationStatus.Marked, Day);
        var model = fixture.Open();
        var id = model.Rows[0].Transaction.Id;
        if (action == "LegacyRelease")
            model.ReleaseLegacyReconciliation(Selection(model.Rows[0]), "Réexamen explicite");
        else
        {
            var session = action == "EmptyValidation"
                ? model.StageReconciliation(new(Day, 0m), [])
                : model.StageReconciliation(new(Day, 2m), [id]);
            if (action == "Reopen")
            {
                fixture.Save(model);
                model.ReopenReconciliation(session.Id, session.Revision, "Correction explicite");
            }
        }

        var batch = model.PendingReconciliations;
        var changes = model.PendingChanges.ToArray();
        var rows = model.Rows;
        var selected = model.SelectedRow;
        var balance = model.SelectedAccount!.Balance;
        var pointed = model.PointedBalance;
        var unmarked = model.UnmarkedAmount;
        var pageReads = fixture.Source.PageReads;
        var other = rows.Single(row => row.Transaction.Id != id).Transaction;
        var draft = new TransactionDraft(Day, 4m, TransactionDirection.Debit, "Interdit", "Nouveau tiers", "Nouvelle catégorie");
        Action[] attempts =
        [
            () => model.AddTransaction(draft),
            () => model.EditTransaction(id, rows.Single(row => row.Transaction.Id == id).Transaction.Revision, draft),
            () => model.DeleteTransaction(id, rows.Single(row => row.Transaction.Id == id).Transaction.Revision),
            () => model.EditTransaction(other.Id, other.Revision, draft),
            () => model.DeleteTransaction(other.Id, other.Revision),
            () => model.TogglePointing([new(other.Id, other.Revision)]),
            () => model.SetPointing([new(other.Id, other.Revision)], false),
            () => model.ChangeMarkingDate(new(other.Id, other.Revision), Day.AddDays(-1))
        ];
        foreach (var attempt in attempts)
        {
            var error = Assert.ThrowsException<InvalidOperationException>(attempt);
            StringAssert.Contains(error.Message, "Ctrl+S");
            Assert.AreSame(batch, model.PendingReconciliations);
            CollectionAssert.AreEqual(changes, model.PendingChanges.ToArray());
            Assert.AreSame(rows, model.Rows);
            Assert.AreSame(selected, model.SelectedRow);
            Assert.AreEqual(balance, model.SelectedAccount.Balance);
            Assert.AreEqual(pointed, model.PointedBalance);
            Assert.AreEqual(unmarked, model.UnmarkedAmount);
            Assert.AreEqual(pageReads, fixture.Source.PageReads);
            Assert.IsTrue(model.IsDirty);
        }

        fixture.Save(model);
        Assert.IsTrue(model.PendingReconciliations.IsEmpty);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.IsFalse(model.IsDirty);
        foreach (var change in changes)
            Assert.AreEqual(change.Value, fixture.Source.Transactions.Single(value => value.Id == change.Value.Id));
        foreach (var change in batch.Sessions)
            Assert.AreEqual(change.Value, fixture.Source.Sessions[change.Value.Id]);
        foreach (var entry in batch.Events)
            Assert.IsTrue(fixture.Source.Events.Contains(entry));
        other = model.Rows.Single(row => row.Transaction.Id == other.Id).Transaction;
        model.SetPointing([new(other.Id, other.Revision)], false);
        Assert.IsTrue(model.IsDirty, "Saving the reconciliation draft must unlock ordinary pointing.");
    }

    private static TransactionSelection Selection(LedgerRow row) => new(row.Transaction.Id, row.Transaction.Revision);
    private static TransactionSelection[] Selections(MainViewModel model) => model.Rows.Select(Selection).ToArray();

    private sealed class Fixture
    {
        public Account Account { get; }
        public FinancialDocument Document { get; private set; }
        public MemorySource Source { get; } = new();
        public Fixture(decimal opening = 0m, Currency? currency = null)
        {
            currency ??= Euro;
            Account = new Account(Guid.NewGuid(), "Compte synthétique", currency, opening);
            var now = DateTimeOffset.UtcNow;
            Document = new FinancialDocument(Guid.NewGuid(), "Dossier synthétique", now, now, [currency], [Account]) { SelectedAccountId = Account.Id };
        }
        public void Add(decimal amount, TransactionDirection direction, ReconciliationStatus status = ReconciliationStatus.Unmarked, DateOnly? markedOn = null, DateOnly? date = null)
        {
            var now = DateTimeOffset.UtcNow;
            Source.Transactions.Add(new Transaction(Guid.NewGuid(), Account.Id, date ?? Day, "Identique", new(""), new(""), amount, Account.Currency, direction, status, "Notes")
            { MarkedOn = markedOn, CreatedUtc = now, ModifiedUtc = now });
        }
        public MainViewModel Open() => new(Document with { OperationCount = Source.Transactions.Count }, Source);
        public void Save(MainViewModel model, bool failRefresh = false)
        {
            foreach (var change in model.PendingChanges)
            {
                Source.Transactions.RemoveAll(value => value.Id == change.Value.Id);
                Source.Transactions.Add(change.Value);
            }
            foreach (var change in model.PendingReconciliations.Sessions)
            {
                Source.Sessions[change.Value.Id] = change.Value;
                if (change.ExpectedRevision == 0) Source.Entries[change.Value.Id] = change.Entries;
            }
            Source.Events.AddRange(model.PendingReconciliations.Events);
            Document = model.CaptureDocument() with { OperationCount = Source.Transactions.Count };
            Source.FailPage = failRefresh;
            model.AcceptSaved(Document);
        }
    }

    private sealed class MemorySource : ILedgerSource, IReconciliationSource
    {
        public List<Transaction> Transactions { get; } = [];
        public Dictionary<Guid, ReconciliationSession> Sessions { get; } = [];
        public Dictionary<Guid, IReadOnlyList<ReconciliationEntry>> Entries { get; } = [];
        public List<ReconciliationEvent> Events { get; } = [];
        public bool FailPage { get; set; }
        public bool FailTransactions { get; set; }
        public int PageReads { get; private set; }
        public LedgerPage ReadPage(Account account, int offset, int pageSize, IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null)
        {
            PageReads++;
            if (FailPage) throw new IOException("Lecture synthétique indisponible");
            var overlay = Transactions.ToDictionary(value => value.Id);
            foreach (var change in pending) overlay[change.Value.Id] = change.Value;
            var active = overlay.Values.Where(value => value.AccountId == account.Id && !value.IsDeleted).OrderBy(value => value.Date).ThenBy(value => value.CreatedUtc).ToArray();
            var balance = account.OpeningBalance;
            var pointed = balance;
            var before = balance;
            var page = new List<Transaction>();
            for (var index = 0; index < active.Length; index++)
            {
                if (index == offset) before = balance;
                balance += FinancialRules.SignedAmount(active[index]);
                if (active[index].Status != ReconciliationStatus.Unmarked) pointed += FinancialRules.SignedAmount(active[index]);
                if (index >= offset && index < offset + pageSize) page.Add(active[index]);
            }
            return new(page, before, balance, active.LongLength, offset) { Totals = new(balance, pointed, balance - pointed) };
        }
        public ReconciliationCandidatePage ReadReconciliationCandidates(Account account, DateOnly statementDate, IReadOnlySet<Guid> selectedIds, int offset, int pageSize)
        {
            var latest = Sessions.Values.Where(value => value.AccountId == account.Id && value.State == ReconciliationSessionState.Validated).OrderByDescending(value => value.StatementDate).FirstOrDefault();
            var eligible = Transactions.Where(value => value.AccountId == account.Id && !value.IsDeleted && value.Status != ReconciliationStatus.Reconciled && value.Date <= statementDate).ToArray();
            var selected = eligible.Where(value => selectedIds.Contains(value.Id)).ToArray();
            if (selected.Length != selectedIds.Count) throw new InvalidOperationException("Sélection inéligible");
            return new(eligible.Skip(offset).Take(pageSize).ToArray(), eligible.LongLength, offset,
                latest?.ExpectedClosingBalance ?? account.OpeningBalance, selected.Sum(FinancialRules.SignedAmount), selected.LongLength, latest?.Id,
                Transactions.LongCount(value => value.AccountId == account.Id && !value.IsDeleted && value.Status == ReconciliationStatus.Reconciled && value.ReconciliationId is null));
        }
        public IReadOnlyList<Transaction> ReadTransactions(IReadOnlyCollection<Guid> ids)
        {
            if (FailTransactions) throw new IOException("Lecture synthétique indisponible");
            return Transactions.Where(value => ids.Contains(value.Id)).ToArray();
        }
        public ReconciliationHistoryPage ReadReconciliationHistory(Guid accountId, int offset, int pageSize)
        {
            var history = Sessions.Values.Where(value => value.AccountId == accountId).ToArray();
            return new(history.Skip(offset).Take(pageSize).ToArray(), history.LongLength, offset);
        }
        public ReconciliationDetails ReadReconciliation(Guid sessionId) => new(Sessions[sessionId], Entries[sessionId], Events.Where(value => value.SessionId == sessionId).ToArray());
    }
}
