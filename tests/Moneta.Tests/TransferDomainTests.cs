using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.Tests;

[TestClass]
public sealed class TransferDomainTests
{
    private static readonly DateOnly Day = new(2026, 5, 12);
    private static readonly Currency[] Currencies = [new("EUR", "Euro", 2), new("BYN", "Rouble biélorusse", 2), new("RUB", "Rouble russe", 2), new("KWD", "Dinar", 3)];

    private static FinancialDocument Document(params Account[] accounts)
    {
        var now = DateTimeOffset.UtcNow;
        if (accounts.Length == 0) accounts = Currencies.Select((currency, index) => new Account(Guid.NewGuid(), $"Compte {index}", currency, 1000m)).ToArray();
        return new FinancialDocument(Guid.NewGuid(), "Virements synthétiques", now, now, Currencies, accounts) { SelectedAccountId = accounts[0].Id };
    }

    private static TransferDraft Draft(MainViewModel model, int source = 0, int destination = 1, decimal debit = 10m, decimal credit = 35m) =>
        new(model.Accounts[source].Account.Id, model.Accounts[destination].Account.Id, Day, debit, credit, "Référence synthétique", "Notes communes");

    [TestMethod]
    public void SixFxDirectionsAndFourthCurrencyPreserveExactEnteredAmountsAndAllBalances()
    {
        for (var source = 0; source < 3; source++)
        for (var destination = 0; destination < 3; destination++)
        {
            if (source == destination) continue;
            var model = new MainViewModel(Document());
            var details = model.CreateTransfer(Draft(model, source, destination, 10.01m, 35.02m));
            Assert.AreEqual(989.99m, model.Accounts[source].Balance);
            Assert.AreEqual(1035.02m, model.Accounts[destination].Balance);
            Assert.AreEqual(10.01m, details.Debit.Amount);
            Assert.AreEqual(35.02m, details.Credit.Amount);
            Assert.AreEqual(Day, details.Value.ExchangeDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(details.Value.RateSource));
            Assert.IsTrue(model.PendingChanges.All(change => change.ExpectedRevision == 0));
            Assert.AreEqual(1, details.QuoteScale);
            StringAssert.Contains(TransferRules.FormatEffectiveRate(details), "Rapport exact");
        }
        var fourth = new MainViewModel(Document());
        var kwd = fourth.CreateTransfer(Draft(fourth, 0, 3, 2.01m, 0.123m));
        Assert.AreEqual(1000.123m, fourth.Accounts[3].Balance);
        Assert.AreEqual(0.123m, kwd.Credit.Amount);
    }

    [TestMethod]
    public void SameCurrencyRequiresEqualityAndKeepsFeesSeparateInEachNativeCurrency()
    {
        foreach (var currency in Currencies.Take(3))
        {
            var model = new MainViewModel(Document(new(Guid.NewGuid(), "Source", currency, 100m), new(Guid.NewGuid(), "Cible", currency, 10m), new(Guid.NewGuid(), "Frais", Currencies[3], 5m)));
            var draft = Draft(model, debit: 3.21m, credit: 3.21m) with { FeeAccountId = model.Accounts[2].Account.Id, FeeAmount = 0.123m };
            var details = model.CreateTransfer(draft);
            Assert.IsNull(details.Value.ExchangeDate);
            Assert.IsNull(details.Value.RateSource);
            Assert.AreEqual(96.79m, model.Accounts[0].Balance);
            Assert.AreEqual(13.21m, model.Accounts[1].Balance);
            Assert.AreEqual(4.877m, model.Accounts[2].Balance);
            Assert.AreEqual(TransactionDirection.Debit, details.Fee!.Direction);
            Assert.AreEqual(3, model.PendingChanges.Count);
            Assert.ThrowsException<ArgumentException>(() => model.CreateTransfer(draft with { CreditedAmount = 3.22m }));
        }
        foreach (var accountIndex in new[] { 0, 1, 2 })
        {
            var model = new MainViewModel(Document());
            var details = model.CreateTransfer(Draft(model) with { FeeAccountId = model.Accounts[accountIndex].Account.Id, FeeAmount = 1m });
            Assert.AreEqual(model.Accounts[accountIndex].Account.Id, details.Fee!.AccountId);
            Assert.AreEqual(3, model.PendingChanges.Count);
        }
    }

    [TestMethod]
    public void InvalidDraftsLeaveExistingDraftRowsSelectionAndOtherAccountBalancesIntact()
    {
        var model = new MainViewModel(Document());
        var first = model.CreateTransfer(Draft(model));
        var rows = model.Rows;
        var selected = model.SelectedRow;
        var pending = model.PendingChanges.ToArray();
        var transfers = model.PendingTransfers.ToArray();
        var balances = model.Accounts.Select(value => value.Balance).ToArray();
        var invalid = new[]
        {
            Draft(model) with { DebitedAmount = 0 }, Draft(model) with { CreditedAmount = -1 },
            Draft(model) with { DebitedAmount = 0.001m }, Draft(model) with { CreditedAmount = decimal.MaxValue },
            Draft(model) with { DestinationAccountId = model.Accounts[0].Account.Id },
            Draft(model) with { DestinationAccountId = Guid.NewGuid() }, Draft(model) with { Date = default },
            Draft(model) with { Reference = new string('x', 121) }, Draft(model) with { Notes = new string('x', 2001) },
            Draft(model) with { Reference = "Réf\0invalide" }, Draft(model) with { Notes = "Notes\0invalides" },
            Draft(model) with { RateSource = "Source\0invalide" },
            Draft(model) with { FeeAccountId = model.Accounts[2].Account.Id, FeeAmount = 0 },
            Draft(model) with { FeeAmount = 1 }
        };
        foreach (var draft in invalid)
        {
            Assert.ThrowsException<ArgumentException>(() => NormalizeArgument(() => model.CreateTransfer(draft)));
            Assert.AreSame(rows, model.Rows);
            Assert.AreSame(selected, model.SelectedRow);
            CollectionAssert.AreEqual(pending, model.PendingChanges.ToArray());
            CollectionAssert.AreEqual(transfers, model.PendingTransfers.ToArray());
            CollectionAssert.AreEqual(balances, model.Accounts.Select(value => value.Balance).ToArray());
        }
        var archivedDoc = Document();
        archivedDoc = archivedDoc with { Accounts = archivedDoc.Accounts.Select((account, index) => index == 1 ? account with { IsArchived = true } : account).ToArray() };
        var archived = new MainViewModel(archivedDoc);
        Assert.ThrowsException<InvalidOperationException>(() => archived.CreateTransfer(Draft(archived)));
        Assert.AreEqual(first, model.GetTransfer(first.Value.Id));
    }

    [TestMethod]
    public void PendingLegsPointIndependentlyKeepLinksAcrossF11AndCannotBeEditedAlone()
    {
        var model = new MainViewModel(Document());
        var draft = Draft(model) with { FeeAccountId = model.Accounts[2].Account.Id, FeeAmount = 1m };
        var details = model.CreateTransfer(draft);
        model.SetPointing([new(details.Debit.Id, details.Debit.Revision)], true, Day);
        details = model.GetTransfer(details.Value.Id);
        Assert.AreEqual(ReconciliationStatus.Marked, details.Debit.Status);
        Assert.AreEqual(ReconciliationStatus.Unmarked, details.Credit.Status);
        Assert.AreEqual(ReconciliationStatus.Unmarked, details.Fee!.Status);
        Assert.AreEqual(990m, model.PointedBalance);
        Assert.AreEqual(0m, model.UnmarkedAmount);
        Assert.AreEqual(1L, details.Value.Revision);
        model.ShowMarkingDates = true;
        Assert.AreEqual(details.Value.Id, model.Rows.Single().TransferId);
        Assert.IsTrue(model.Rows.Single().IsTransfer);
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransaction(details.Debit.Id, details.Debit.Revision, new(Day, 4m, TransactionDirection.Debit, "Interdit")));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(details.Debit.Id, details.Debit.Revision));
        var edited = model.EditTransfer(details.Value.Id, details.Value.Revision, draft with { DebitedAmount = 11m });
        Assert.AreEqual(ReconciliationStatus.Marked, edited.Debit.Status);
        Assert.AreEqual(Day, edited.Debit.MarkedOn);
        Assert.AreEqual(details.Debit.Id, edited.Debit.Id);
        Assert.AreEqual(details.Credit.Id, edited.Credit.Id);
        model.DeleteTransfer(edited.Value.Id, edited.Value.Revision);
        Assert.AreEqual(0, model.PendingTransfers.Count);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.IsTrue(model.Accounts.All(account => account.Balance == 1000m));
    }

    [TestMethod]
    public void ExistingFeesCanBecomeZeroButCannotBeRemovedOrMovedAndFirstFeeCanBeAdded()
    {
        var model = new MainViewModel(Document());
        var draft = Draft(model);
        var first = model.CreateTransfer(draft);
        var added = model.EditTransfer(first.Value.Id, first.Value.Revision, draft with { FeeAccountId = model.Accounts[2].Account.Id, FeeAmount = 2m });
        var feeId = added.Fee!.Id;
        var zeroDraft = draft with { FeeAccountId = model.Accounts[2].Account.Id, FeeAmount = 0 };
        var zero = model.EditTransfer(first.Value.Id, added.Value.Revision, zeroDraft);
        Assert.AreEqual(feeId, zero.Fee!.Id);
        Assert.AreEqual(0m, zero.Fee.Amount);
        Assert.AreEqual(3, model.PendingChanges.Count);
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(first.Value.Id, zero.Value.Revision, draft));
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(first.Value.Id, zero.Value.Revision, zeroDraft with { FeeAccountId = model.Accounts[0].Account.Id }));
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(first.Value.Id, zero.Value.Revision, zeroDraft with { SourceAccountId = model.Accounts[3].Account.Id }));
        Assert.AreEqual(zero, model.GetTransfer(first.Value.Id));
    }

    [TestMethod]
    public void OtherAccountReadFailureAndProgressiveOverflowDoNotPublishAnyPartOfTransfer()
    {
        var document = Document();
        var source = new MemorySource();
        var model = new MainViewModel(document, source);
        var first = model.CreateTransfer(Draft(model));
        var rows = model.Rows;
        var pending = model.PendingChanges.ToArray();
        var balances = model.Accounts.Select(account => account.Balance).ToArray();
        source.FailingAccount = model.Accounts[1].Account.Id;
        Assert.ThrowsException<IOException>(() => model.CreateTransfer(Draft(model)));
        Assert.AreSame(rows, model.Rows);
        CollectionAssert.AreEqual(pending, model.PendingChanges.ToArray());
        CollectionAssert.AreEqual(balances, model.Accounts.Select(account => account.Balance).ToArray());
        source.FailingAccount = null;
        source.OverflowAccount = model.Accounts[1].Account.Id;
        Assert.ThrowsException<OverflowException>(() => model.EditTransfer(first.Value.Id, first.Value.Revision, Draft(model, credit: 36m)));
        Assert.AreSame(rows, model.Rows);
        Assert.AreEqual(first, model.GetTransfer(first.Value.Id));
        CollectionAssert.AreEqual(pending, model.PendingChanges.ToArray());
        CollectionAssert.AreEqual(balances, model.Accounts.Select(account => account.Balance).ToArray());
    }

    [TestMethod]
    public void ReconciledNonVisibleCreditOrFeeBlocksWholeTransferButNotNavigation()
    {
        foreach (var feeProtected in new[] { false, true })
        {
            var document = Document();
            var seed = new MainViewModel(document);
            var draft = Draft(seed) with { FeeAccountId = seed.Accounts[2].Account.Id, FeeAmount = 1m };
            var original = seed.CreateTransfer(draft);
            var protectedLeg = feeProtected ? original.Fee! : original.Credit;
            var source = new MemorySource();
            source.Transfers[original.Value.Id] = original.Value;
            foreach (var change in seed.PendingChanges) source.Transactions[change.Value.Id] = change.Value;
            source.Transactions[protectedLeg.Id] = protectedLeg with { Status = ReconciliationStatus.Reconciled };
            var model = new MainViewModel(document with { OperationCount = 3, TransferCount = 1 }, source);
            Assert.IsFalse(model.Rows.Any(row => row.Transaction.Id == protectedLeg.Id));
            Assert.ThrowsException<InvalidOperationException>(() => model.EditTransfer(original.Value.Id, 1, draft));
            Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransfer(original.Value.Id, 1));
            Assert.AreEqual(0, model.PendingChanges.Count);
            Assert.IsFalse(model.IsDirty);
            model.NavigateToTransferCounterpart(original.Value.Id, original.Debit.Id);
            Assert.AreEqual(original.Credit.Id, model.SelectedRow!.Transaction.Id);
        }
    }

    [TestMethod]
    public void NavigationAnchorsCounterpartBeyondPage100AndLegitimateDuplicatesRemainDistinct()
    {
        var model = new MainViewModel(Document());
        model.SelectedAccount = model.Accounts[1];
        for (var index = 0; index < 205; index++) model.AddTransaction(new(Day.AddDays(-1), 1m, TransactionDirection.Credit, "Avant virement"));
        model.SelectedAccount = model.Accounts[0];
        var first = model.CreateTransfer(Draft(model));
        var second = model.CreateTransfer(Draft(model));
        Assert.AreNotEqual(first.Value.Id, second.Value.Id);
        Assert.AreNotEqual(first.Debit.Id, second.Debit.Id);
        model.NavigateToTransferCounterpart(first.Value.Id, first.Debit.Id);
        Assert.AreSame(model.Accounts[1], model.SelectedAccount);
        Assert.AreEqual(first.Credit.Id, model.SelectedRow!.Transaction.Id);
        Assert.IsTrue(model.CanPreviousPage);
        Assert.AreEqual(first.Value.Id, model.SelectedRow.TransferId);
        model.NavigateToTransferCounterpart(first.Value.Id, first.Credit.Id);
        Assert.AreEqual(first.Debit.Id, model.SelectedRow!.Transaction.Id);
    }

    [TestMethod]
    public void RateFormattingNeverPublishesOverflowOrFalseZeroAndDurableValidationAllowsArchivedHistoricalAccounts()
    {
        var tiny = new Currency("TNY", "Précision extrême", 28);
        var unit = new Currency("UNT", "Unité", 0);
        var hugeRatio = TransferRules.FormatEffectiveRate(0.0000000000000000000000000001m, tiny, 9223372036854775807m, unit);
        var tinyRatio = TransferRules.FormatEffectiveRate(9223372036854775807m, unit, 0.0000000000000000000000000001m, tiny);
        Assert.IsFalse(hugeRatio.Contains("approximatif", StringComparison.Ordinal));
        Assert.IsFalse(tinyRatio.Contains("approximatif", StringComparison.Ordinal));
        StringAssert.Contains(hugeRatio, "Rapport exact");
        StringAssert.Contains(tinyRatio, "Rapport exact");
        foreach (var reverse in new[] { false, true })
        {
            var source = new Account(Guid.NewGuid(), "Source extrême", reverse ? unit : tiny, 0m);
            var destination = new Account(Guid.NewGuid(), "Cible extrême", reverse ? tiny : unit, 0m);
            var now = DateTimeOffset.UtcNow;
            var extreme = new MainViewModel(new FinancialDocument(Guid.NewGuid(), "Rapport extrême", now, now, [tiny, unit], [source, destination])
                { SelectedAccountId = source.Id });
            var valid = extreme.CreateTransfer(new(source.Id, destination.Id, Day,
                reverse ? 9223372036854775807m : 0.0000000000000000000000000001m,
                reverse ? 0.0000000000000000000000000001m : 9223372036854775807m));
            Assert.AreEqual(2, extreme.PendingChanges.Count);
            Assert.IsFalse(TransferRules.FormatEffectiveRate(valid).Contains("approximatif", StringComparison.Ordinal));
        }
        var model = new MainViewModel(Document());
        var details = model.CreateTransfer(Draft(model));
        var archived = model.CaptureDocument() with { Accounts = model.CaptureDocument().Accounts.Select(account => account with { IsArchived = true }).ToArray() };
        TransferRules.Validate(details, archived);
        var demo = new MainViewModel(new DemoPortfolio([], []));
        Assert.ThrowsException<InvalidOperationException>(() => demo.CreateTransfer(Draft(model)));
        Assert.ThrowsException<InvalidOperationException>(() => demo.EditTransfer(details.Value.Id, 1, Draft(model)));
        Assert.ThrowsException<InvalidOperationException>(() => demo.DeleteTransfer(details.Value.Id, 1));
        Assert.IsFalse(demo.IsDirty);
        Assert.AreEqual(0, demo.PendingTransfers.Count);
    }

    private static void NormalizeArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException exception) { throw new ArgumentException(exception.Message, exception); }
    }

    private sealed class MemorySource : ILedgerSource, ITransferSource
    {
        public Dictionary<Guid, Transaction> Transactions { get; } = [];
        public Dictionary<Guid, Transfer> Transfers { get; } = [];
        public Guid? FailingAccount { get; set; }
        public Guid? OverflowAccount { get; set; }
        public TransferDetails ReadTransfer(Guid id)
        {
            var value = Transfers[id];
            return new(value, Transactions[value.DebitTransactionId], Transactions[value.CreditTransactionId], value.FeeTransactionId is { } fee ? Transactions[fee] : null);
        }
        public Guid? FindTransferId(Guid id) => Transfers.Values.FirstOrDefault(value => value.DebitTransactionId == id || value.CreditTransactionId == id || value.FeeTransactionId == id)?.Id;
        public LedgerPage ReadPage(Account account, int offset, int pageSize, IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null)
        {
            if (FailingAccount == account.Id) throw new IOException("Lecture synthétique indisponible");
            var overlay = new Dictionary<Guid, Transaction>(Transactions);
            foreach (var change in pending) overlay[change.Value.Id] = change.Value;
            var active = overlay.Values.Where(value => value.AccountId == account.Id && !value.IsDeleted)
                .OrderBy(value => value.Date).ThenBy(value => value.CreatedUtc).ThenBy(value => value.Id).ToArray();
            if (anchorId is { } anchor)
            {
                var index = Array.FindIndex(active, value => value.Id == anchor);
                if (index >= 0) offset = index / pageSize * pageSize;
            }
            offset = active.Length == 0 ? 0 : Math.Min(offset, (active.Length - 1) / pageSize * pageSize);
            var balance = account.OpeningBalance;
            var pointed = balance;
            var before = balance;
            for (var index = 0; index < active.Length; index++)
            {
                if (index == offset) before = balance;
                balance = checked(balance + FinancialRules.SignedAmount(active[index]));
                if (active[index].Status != ReconciliationStatus.Unmarked) pointed = checked(pointed + FinancialRules.SignedAmount(active[index]));
            }
            var page = active.Skip(offset).Take(pageSize).ToArray();
            return new(page, OverflowAccount == account.Id ? decimal.MaxValue : before, balance, active.LongLength, offset)
            {
                Totals = new(balance, pointed, balance - pointed),
                TransferIds = page.Select(value => (value.Id, Transfer: FindTransferId(value.Id))).Where(value => value.Transfer is not null)
                    .ToDictionary(value => value.Id, value => value.Transfer!.Value)
            };
        }
    }
}
