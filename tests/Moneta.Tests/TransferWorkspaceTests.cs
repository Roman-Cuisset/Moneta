using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class TransferWorkspaceTests
{
    private static readonly DateOnly Day = new(2026, 5, 12);
    private string directory = null!;
    private string FileName(string name) => Path.Combine(directory, name + ".moneta");
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "Moneta-transfer-workspace-" + Guid.NewGuid()));
    [TestCleanup] public void Cleanup() => Directory.Delete(directory, true);

    private static TransferDraft Prepare(DocumentWorkspace workspace)
    {
        workspace.New("Virements synthétiques");
        var model = workspace.Model!;
        var source = model.AddAccount("Source EUR", model.CurrencyRegistry.Get("EUR"), null, 100m, null);
        var destination = model.AddAccount("Cible BYN", model.CurrencyRegistry.Get("BYN"), null, 200m, null);
        var fees = model.AddAccount("Frais RUB", model.CurrencyRegistry.Get("RUB"), null, 300m, null);
        model.SelectedAccount = source;
        return new(source.Account.Id, destination.Account.Id, Day, 10m, 35m, "Référence synthétique", "Notes communes", FeeAccountId: fees.Account.Id, FeeAmount: 2m);
    }

    [TestMethod]
    public void NewTransferWithSeparateFeeSavesReopensAndEditsWithIndependentStatusRevisions()
    {
        using var workspace = new DocumentWorkspace();
        var draft = Prepare(workspace);
        var model = workspace.Model!;
        var initial = model.CreateTransfer(draft);
        Assert.IsFalse(File.Exists(FileName("source")));
        workspace.Save(FileName("source"));
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(0, model.PendingTransfers.Count);
        Assert.AreEqual(initial.Value.Id, model.Rows.Single().TransferId);
        model.SetPointing([new(initial.Debit.Id, initial.Debit.Revision)], true, Day);
        workspace.Save();
        var pointed = model.GetTransfer(initial.Value.Id);
        Assert.AreEqual(2L, pointed.Debit.Revision);
        Assert.AreEqual(1L, pointed.Value.Revision);
        var edited = model.EditTransfer(initial.Value.Id, 1, draft with { DebitedAmount = 11m, CreditedAmount = 36m, FeeAmount = 0 });
        Assert.AreEqual(2L, model.PendingChanges.Single(change => change.Value.Id == initial.Debit.Id).ExpectedRevision);
        Assert.AreEqual(1L, model.PendingChanges.Single(change => change.Value.Id == initial.Credit.Id).ExpectedRevision);
        workspace.Save();
        workspace.New("Autre dossier");
        workspace.Open(FileName("source"));
        model = workspace.Model!;
        var reopened = model.GetTransfer(initial.Value.Id);
        Assert.AreEqual(edited.Value, reopened.Value);
        Assert.AreEqual(11m, reopened.Debit.Amount);
        Assert.AreEqual(36m, reopened.Credit.Amount);
        Assert.AreEqual(initial.Fee!.Id, reopened.Fee!.Id);
        Assert.AreEqual(0m, reopened.Fee.Amount);
        Assert.AreEqual(ReconciliationStatus.Marked, reopened.Debit.Status);
        Assert.AreEqual(Day, reopened.Debit.MarkedOn);
        Assert.AreEqual(ReconciliationStatus.Unmarked, reopened.Credit.Status);
        Assert.AreEqual(89m, model.Accounts.Single(account => account.Account.Id == draft.SourceAccountId).Balance);
        Assert.AreEqual(236m, model.Accounts.Single(account => account.Account.Id == draft.DestinationAccountId).Balance);
        Assert.AreEqual(300m, model.Accounts.Single(account => account.Account.Id == draft.FeeAccountId).Balance);
        model.ShowMarkingDates = true;
        Assert.AreEqual(initial.Value.Id, model.Rows.Single().TransferId);
    }

    [TestMethod]
    public void SaveAsCopiesTransferDraftWithoutMutatingSourceAndDeletionRetainsAllRelations()
    {
        using var workspace = new DocumentWorkspace();
        var draft = Prepare(workspace);
        var model = workspace.Model!;
        var original = model.CreateTransfer(draft);
        workspace.Save(FileName("source"));
        var changed = model.EditTransfer(original.Value.Id, 1, draft with { CreditedAmount = 36m });
        workspace.Save(FileName("copy"));
        using (var source = MonetaFile.Open(FileName("source")))
            Assert.AreEqual(35m, source.ReadTransfer(original.Value.Id).Credit.Amount);
        workspace.New("Fermer la copie avant sa vérification indépendante");
        using (var copy = MonetaFile.Open(FileName("copy")))
            Assert.AreEqual(36m, copy.ReadTransfer(original.Value.Id).Credit.Amount);
        workspace.Open(FileName("copy"));
        model = workspace.Model!;
        model.DeleteTransfer(changed.Value.Id, changed.Value.Revision);
        Assert.AreEqual(3, model.PendingChanges.Count);
        Assert.IsTrue(model.PendingChanges.All(change => change.Value.IsDeleted));
        workspace.Save();
        workspace.New("Autre dossier");
        workspace.Open(FileName("copy"));
        model = workspace.Model!;
        var deleted = model.GetTransfer(original.Value.Id);
        Assert.IsTrue(deleted.Value.IsDeleted);
        Assert.IsTrue(deleted.Debit.IsDeleted && deleted.Credit.IsDeleted && deleted.Fee!.IsDeleted);
        Assert.AreEqual(original.Value.Id, model.FindTransferId(original.Fee!.Id));
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual(100m, model.Accounts.Single(account => account.Account.Id == draft.SourceAccountId).Balance);
        Assert.AreEqual(200m, model.Accounts.Single(account => account.Account.Id == draft.DestinationAccountId).Balance);
        Assert.AreEqual(300m, model.Accounts.Single(account => account.Account.Id == draft.FeeAccountId).Balance);
        Assert.AreEqual(1L, model.CaptureDocument().TransferCount);
    }

    [TestMethod]
    public void PersistedFirstFeeCanBeAddedThenZeroedWithoutReplacingItsIdentity()
    {
        using var workspace = new DocumentWorkspace();
        var withFee = Prepare(workspace);
        var draft = withFee with { FeeAccountId = null, FeeAmount = 0 };
        var model = workspace.Model!;
        var original = model.CreateTransfer(draft);
        workspace.Save(FileName("fees"));
        var added = model.EditTransfer(original.Value.Id, 1, withFee);
        Assert.AreEqual(0L, model.PendingChanges.Single(change => change.Value.Id == added.Fee!.Id).ExpectedRevision);
        workspace.Save();
        var zero = model.EditTransfer(added.Value.Id, added.Value.Revision, withFee with { FeeAmount = 0 });
        workspace.Save();
        workspace.New("Autre dossier");
        workspace.Open(FileName("fees"));
        var reopened = workspace.Model!.GetTransfer(original.Value.Id);
        Assert.AreEqual(added.Fee!.Id, reopened.Fee!.Id);
        Assert.AreEqual(zero.Value.Revision, reopened.Value.Revision);
        Assert.AreEqual(0m, reopened.Fee.Amount);
    }

    [DataTestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    public void FinancialEditAndIndependentPointingCanShareOneSave(bool pointFirst, int transition)
    {
        using var workspace = new DocumentWorkspace();
        var draft = Prepare(workspace);
        var model = workspace.Model!;
        var transfer = model.CreateTransfer(draft);
        workspace.Save(FileName("combined"));
        if (transition != 0)
        {
            model.SetPointing([new(transfer.Debit.Id, 1)], true, Day);
            workspace.Save();
        }
        void Point()
        {
            var current = model.GetTransfer(transfer.Value.Id);
            model.SetPointing([new(current.Debit.Id, current.Debit.Revision)], transition != 1,
                transition == 2 ? Day.AddDays(1) : Day);
        }
        if (pointFirst) Point();
        model.EditTransfer(transfer.Value.Id, 1, draft with { DebitedAmount = 11m, CreditedAmount = 36m });
        if (!pointFirst) Point();
        workspace.Save();
        workspace.New("Fermer");
        workspace.Open(FileName("combined"));
        var reopened = workspace.Model!.GetTransfer(transfer.Value.Id);
        Assert.AreEqual(11m, reopened.Debit.Amount);
        Assert.AreEqual(36m, reopened.Credit.Amount);
        Assert.AreEqual(transition == 1 ? ReconciliationStatus.Unmarked : ReconciliationStatus.Marked, reopened.Debit.Status);
        Assert.AreEqual(transition == 1 ? (DateOnly?)null : transition == 2 ? Day.AddDays(1) : Day, reopened.Debit.MarkedOn);
        Assert.AreEqual(ReconciliationStatus.Unmarked, reopened.Credit.Status);
        Assert.AreEqual(2L, reopened.Value.Revision);
        Assert.AreEqual(89m, workspace.Model.Accounts.Single(account => account.Account.Id == draft.SourceAccountId).Balance);
    }

    [TestMethod]
    public void DeletionDiscardsUnpersistedFirstFeeButRetainsBothPersistedLegTombstones()
    {
        using var workspace = new DocumentWorkspace();
        var withFee = Prepare(workspace);
        var model = workspace.Model!;
        var transfer = model.CreateTransfer(withFee with { FeeAccountId = null, FeeAmount = 0m });
        workspace.Save(FileName("cancel-fee"));
        var edited = model.EditTransfer(transfer.Value.Id, 1, withFee);
        model.DeleteTransfer(edited.Value.Id, edited.Value.Revision);
        Assert.AreEqual(2, model.PendingChanges.Count);
        Assert.IsTrue(model.PendingChanges.All(change => change.Value.IsDeleted && change.ExpectedRevision == 1));
        Assert.AreEqual(300m, model.Accounts.Single(account => account.Account.Id == withFee.FeeAccountId).Balance);
        workspace.Save();
        workspace.New("Fermer");
        workspace.Open(FileName("cancel-fee"));
        var reopened = workspace.Model!.GetTransfer(transfer.Value.Id);
        Assert.IsTrue(reopened.Value.IsDeleted && reopened.Debit.IsDeleted && reopened.Credit.IsDeleted);
        Assert.IsNull(reopened.Fee);
        Assert.IsNull(workspace.Model.FindTransferId(edited.Fee!.Id));
        Assert.AreEqual(2L, workspace.Model.CaptureDocument().OperationCount);
        Assert.AreEqual(100m, workspace.Model.Accounts.Single(account => account.Account.Id == withFee.SourceAccountId).Balance);
        Assert.AreEqual(200m, workspace.Model.Accounts.Single(account => account.Account.Id == withFee.DestinationAccountId).Balance);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FeeCorrectedToZeroBeforeItsFirstSaveRetainsItsIdentity(bool transferAlreadySaved)
    {
        using var workspace = new DocumentWorkspace();
        var withFee = Prepare(workspace);
        var model = workspace.Model!;
        var transfer = model.CreateTransfer(transferAlreadySaved
            ? withFee with { FeeAccountId = null, FeeAmount = 0m } : withFee);
        if (transferAlreadySaved)
        {
            workspace.Save(FileName("zero-draft"));
            transfer = model.EditTransfer(transfer.Value.Id, transfer.Value.Revision, withFee);
        }
        var feeId = transfer.Fee!.Id;
        model.EditTransfer(transfer.Value.Id, transfer.Value.Revision, withFee with { FeeAmount = 0m });
        if (transferAlreadySaved) workspace.Save();
        else workspace.Save(FileName("zero-draft"));
        workspace.New("Fermer");
        workspace.Open(FileName("zero-draft"));
        var reopened = workspace.Model!.GetTransfer(transfer.Value.Id);
        Assert.AreEqual(feeId, reopened.Fee!.Id);
        Assert.AreEqual(0m, reopened.Fee.Amount);
        Assert.AreEqual(300m, workspace.Model.Accounts.Single(account => account.Account.Id == withFee.FeeAccountId).Balance);
        Assert.AreEqual(3L, workspace.Model.CaptureDocument().OperationCount);
    }

    [TestMethod]
    public void NonVisibleReconciliationBlocksWholeTransferUntilReopenHasBeenSaved()
    {
        using var workspace = new DocumentWorkspace();
        var draft = Prepare(workspace);
        var model = workspace.Model!;
        var transfer = model.CreateTransfer(draft);
        workspace.Save(FileName("reconciliation"));
        model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Debit.Id);
        model.SetPointing([new(transfer.Credit.Id, transfer.Credit.Revision)], true, Day);
        workspace.Save();
        var session = model.StageReconciliation(new(Day, 235m), [transfer.Credit.Id]);
        // Navigation remains available while the global reconciliation draft guard is active.
        model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Credit.Id);
        Assert.ThrowsException<InvalidOperationException>(() => model.CreateTransfer(draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(transfer.Value.Id, 1, draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransfer(transfer.Value.Id, 1));
        workspace.Save();
        Assert.IsTrue(model.GetTransfer(transfer.Value.Id).HasReconciledLeg);
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(transfer.Value.Id, 1, draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransfer(transfer.Value.Id, 1));
        model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Debit.Id);
        model.ReopenReconciliation(session.Id, session.Revision, "Correction synthétique documentée");
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(transfer.Value.Id, 1, draft));
        workspace.Save();
        var edited = model.EditTransfer(transfer.Value.Id, 1, draft with { CreditedAmount = 36m });
        Assert.AreEqual(ReconciliationStatus.Marked, edited.Credit.Status);
        workspace.Save();
        Assert.IsFalse(workspace.HasUnsavedChanges);
    }

    [TestMethod]
    public void ReopenedNavigationUsesIdAnchorBeyondFirstHundredRows()
    {
        using var workspace = new DocumentWorkspace();
        var draft = Prepare(workspace);
        var model = workspace.Model!;
        model.SelectedAccount = model.Accounts.Single(account => account.Account.Id == draft.DestinationAccountId);
        for (var index = 0; index < 205; index++)
            model.AddTransaction(new(Day.AddDays(-1), 1m, TransactionDirection.Credit, "Avant contrepartie"));
        model.SelectedAccount = model.Accounts.Single(account => account.Account.Id == draft.SourceAccountId);
        var transfer = model.CreateTransfer(draft);
        workspace.Save(FileName("navigation"));
        workspace.New("Autre dossier");
        workspace.Open(FileName("navigation"));
        model = workspace.Model!;
        model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Debit.Id);
        Assert.AreEqual(draft.DestinationAccountId, model.SelectedAccountId);
        Assert.AreEqual(transfer.Credit.Id, model.SelectedRow!.Transaction.Id);
        Assert.AreEqual(transfer.Value.Id, model.SelectedRow.TransferId);
        Assert.IsTrue(model.CanPreviousPage);
        Assert.IsFalse(model.IsDirty);
        model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Fee!.Id);
        Assert.AreEqual(transfer.Debit.Id, model.SelectedRow!.Transaction.Id);
        Assert.AreEqual(draft.SourceAccountId, model.SelectedAccountId);
    }
}
