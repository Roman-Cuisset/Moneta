using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class LedgerDomainTests
{
    private static readonly Currency Euro = new("EUR", "Euro", 2);
    private static readonly DateOnly Day = new(2026, 10, 1);

    private static FinancialDocument Document(Currency? currency = null, decimal opening = 10m)
    {
        currency ??= Euro;
        var now = DateTimeOffset.UtcNow;
        var account = new Account(Guid.NewGuid(), "Compte natif", currency, opening);
        return new FinancialDocument(Guid.NewGuid(), "Dossier synthétique", now, now, [currency], [account])
        { SelectedAccountId = account.Id };
    }

    private static TransactionDraft Draft(decimal amount = 1.25m, TransactionDirection direction = TransactionDirection.Debit) =>
        new(Day, amount, direction, "Libellé synthétique", " École ", " Quotidien ", " Repas ", " Carte ", "Notes conservées\nDeuxième ligne");

    [TestMethod]
    public void AddIsAnExactNativeDraftWithStableReferencesAndObservableBalance()
    {
        var document = Document();
        var model = new MainViewModel(document);
        var summary = model.SelectedAccount!;
        var notifications = new List<string?>();
        summary.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var first = model.AddTransaction(Draft());
        var second = model.AddTransaction(Draft() with { PayeeName = "e\u0301COLE", PaymentMode = "CARTE", CategoryName = "QUOTIDIEN", SubcategoryName = "repas" });
        Assert.AreNotEqual(first.Id, second.Id, "Identical legitimate entries must not be deduplicated.");
        Assert.AreEqual(first.PayeeId, second.PayeeId);
        Assert.AreEqual(first.CategoryId, second.CategoryId);
        Assert.AreEqual(first.SubcategoryId, second.SubcategoryId);
        Assert.AreEqual(first.PaymentMethodId, second.PaymentMethodId);
        Assert.AreEqual("École", second.Payee.Name);
        Assert.AreEqual(1, model.Payees.Count);
        Assert.AreEqual(2, model.Categories.Count);
        Assert.AreEqual(1, model.PaymentMethods.Count);
        Assert.AreSame(summary, model.SelectedAccount);
        Assert.AreEqual(7.50m, summary.Balance);
        CollectionAssert.Contains(notifications, nameof(AccountSummary.Balance));
        CollectionAssert.Contains(notifications, nameof(AccountSummary.BalanceText));
        Assert.AreEqual(8.75m, model.Rows[0].Balance);
        Assert.AreEqual(7.50m, model.Rows[1].Balance);
        Assert.AreEqual(2, model.PendingChanges.Count);
        Assert.IsTrue(model.PendingChanges.All(change => change.ExpectedRevision == 0 && change.Value.Revision == 1));
        Assert.IsTrue(model.IsDirty);
        Assert.AreEqual(0, document.Payees.Count);
        Assert.AreEqual(0L, model.CaptureDocument().OperationCount);
        Assert.IsNull(model.CaptureDocument().LedgerDigest);
        Assert.AreEqual(TimeSpan.Zero, first.CreatedUtc.Offset);
        Assert.AreEqual(first.CreatedUtc, first.ModifiedUtc);
        Assert.AreEqual(ReconciliationStatus.Unmarked, first.Status);
        FinancialRules.ValidateCatalog(model.CaptureDocument());
        FinancialRules.ValidateTransaction(first, model.CaptureDocument());
    }

    [TestMethod]
    public void RejectedDraftsDoNotPublishReferencesRowsSelectionOrBalance()
    {
        var model = new MainViewModel(Document());
        var selected = model.SelectedAccount;
        var rows = model.Rows;
        var invalid = new[]
        {
            Draft(-1m), Draft(0.001m), Draft(92233720368547758.08m),
            Draft() with { Label = " " }, Draft() with { Label = new string('x', 241) },
            Draft() with { Date = default }, Draft() with { Direction = (TransactionDirection)42 },
            Draft() with { PayeeName = new string('x', 121) },
            Draft() with { PaymentMode = new string('x', 81) },
            Draft() with { Notes = new string('x', 2001) },
            Draft() with { CategoryName = null, SubcategoryName = "Orpheline" }
        };
        foreach (var draft in invalid)
        {
            Assert.ThrowsException<ArgumentException>(() => AddInvalid(model, draft));
            Assert.AreSame(selected, model.SelectedAccount);
            Assert.AreSame(rows, model.Rows);
            Assert.AreEqual(10m, selected!.Balance);
            Assert.AreEqual(0, model.PendingChanges.Count);
            Assert.AreEqual(0, model.Categories.Count);
            Assert.AreEqual(0, model.Payees.Count);
            Assert.AreEqual(0, model.PaymentMethods.Count);
            Assert.IsFalse(model.IsDirty);
        }
    }

    // MSTest's exact-type exception assertion is intentionally normalized here: validation
    // legitimately reports both ArgumentException and its out-of-range subclasses.
    private static void AddInvalid(MainViewModel model, TransactionDraft draft)
    {
        try { model.AddTransaction(draft); }
        catch (ArgumentException exception) { throw new ArgumentException(exception.Message, exception); }
    }

    [TestMethod]
    public void PendingInsertCanBeEditedRepeatedlyThenRemovedWithoutATombstone()
    {
        var model = new MainViewModel(Document());
        var first = model.AddTransaction(Draft());
        var edited = model.EditTransaction(first.Id, first.Revision, Draft(4m, TransactionDirection.Credit));
        Assert.AreEqual(first.Id, edited.Id);
        Assert.AreEqual(first.CreatedUtc, edited.CreatedUtc);
        Assert.AreEqual(1L, edited.Revision);
        Assert.AreEqual(0L, model.PendingChanges.Single().ExpectedRevision);
        Assert.AreEqual(14m, model.SelectedAccount!.Balance);
        model.EditTransaction(edited.Id, edited.Revision, Draft(2m));
        Assert.AreEqual(8m, model.SelectedAccount.Balance);
        model.DeleteTransaction(edited.Id, edited.Revision);
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.AreEqual(10m, model.SelectedAccount.Balance);
        Assert.IsTrue(model.IsDirty, "Created catalogue entries remain staged even when the unsaved operation is removed.");
        Assert.AreEqual(1, model.Payees.Count);
    }

    [TestMethod]
    public void PendingPagingAnchorsAddsAndDateEditsAndUsesProgressiveBalances()
    {
        var model = new MainViewModel(Document(opening: 5m));
        Transaction? last = null;
        for (var index = 0; index < 205; index++)
            last = model.AddTransaction(Draft(1m, TransactionDirection.Credit) with { Date = Day.AddDays(index) });
        Assert.AreEqual(5, model.Rows.Count);
        Assert.IsTrue(model.CanPreviousPage);
        Assert.IsFalse(model.CanNextPage);
        Assert.AreEqual(206m, model.Rows[0].Balance);
        Assert.AreEqual(210m, model.Rows[^1].Balance);
        Assert.AreEqual(last!.Id, model.SelectedRow!.Transaction.Id);
        model.PreviousPage();
        Assert.AreEqual(100, model.Rows.Count);
        Assert.AreEqual(106m, model.Rows[0].Balance);
        Assert.IsTrue(model.CanNextPage);
        model.PreviousPage();
        Assert.AreEqual(6m, model.Rows[0].Balance);
        var first = model.Rows[0].Transaction;
        var moved = model.EditTransaction(first.Id, first.Revision, Draft(2m, TransactionDirection.Credit) with { Date = Day.AddDays(300) });
        Assert.AreEqual(moved.Id, model.Rows[^1].Transaction.Id);
        Assert.AreEqual(moved.Id, model.SelectedRow!.Transaction.Id);
        Assert.AreEqual(211m, model.Rows[^1].Balance);
        Assert.AreEqual(211m, model.SelectedAccount!.Balance);
    }

    [TestMethod]
    public void IndividualMinorBoundsDoNotBoundAggregateBalanceAndUseNativePrecision()
    {
        var kwd = new Currency("KWD", "Dinar", 3);
        var model = new MainViewModel(Document(kwd, 9223372036854775.807m));
        model.AddTransaction(Draft(0m, TransactionDirection.Credit));
        model.AddTransaction(Draft(0.001m, TransactionDirection.Credit));
        model.AddTransaction(Draft(9223372036854775.807m, TransactionDirection.Credit));
        Assert.AreEqual(18446744073709551.615m, model.SelectedAccount!.Balance);
        Assert.AreEqual(18446744073709551.615m, model.Rows[^1].Balance);
        Assert.IsTrue(model.Rows.All(row => row.Transaction.Currency == kwd));
        var yen = new MainViewModel(Document(new Currency("JPY", "Yen", 0), 0m));
        Assert.ThrowsException<ArgumentException>(() => yen.AddTransaction(Draft(0.1m)));
        Assert.AreEqual(0m, yen.AddTransaction(Draft(0m)).Amount);
    }

    [TestMethod]
    public void AccountDocumentDemoAndArchivedBoundariesPreventCrossLedgerEdits()
    {
        var document = Document();
        var other = document.Accounts[0] with { Id = Guid.NewGuid(), Name = "Second" };
        var model = new MainViewModel(document with { Accounts = [document.Accounts[0], other] });
        var first = model.AddTransaction(Draft());
        model.SelectedAccount = model.Accounts[1];
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual(10m, model.SelectedAccount.Balance);
        Assert.ThrowsException<InvalidOperationException>(() => model.EditTransaction(first.Id, first.Revision, Draft()));
        Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(first.Id, first.Revision));
        model.AddTransaction(Draft(3m));
        model.SelectedAccount = model.Accounts[0];
        Assert.AreEqual(first.Id, model.Rows.Single().Transaction.Id);
        Assert.AreEqual(8.75m, model.SelectedAccount.Balance);
        var isolated = new MainViewModel(Document());
        Assert.AreEqual(0, isolated.Payees.Count);
        Assert.AreEqual(0, isolated.PendingChanges.Count);
        Assert.ThrowsException<InvalidOperationException>(() => isolated.DeleteTransaction(first.Id, first.Revision));
        var archived = new MainViewModel(document with { Accounts = [document.Accounts[0] with { IsArchived = true }] });
        Assert.ThrowsException<InvalidOperationException>(() => archived.AddTransaction(Draft()));
        var demo = new MainViewModel(new DemoPortfolio([], []));
        Assert.ThrowsException<InvalidOperationException>(() => demo.AddTransaction(Draft()));
    }

    [TestMethod]
    public void CatalogKeysNormalizeUnicodeAndRejectDuplicatesAndInvalidDepth()
    {
        Assert.AreEqual(FinancialRules.NameKey(" École "), FinancialRules.NameKey("e\u0301COLE"));
        var root = new LedgerCategory(Guid.NewGuid(), "Parent");
        var child = new LedgerCategory(Guid.NewGuid(), "Enfant", root.Id);
        var document = Document() with { Categories = [root, child] };
        FinancialRules.ValidateCatalog(document);
        Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateCatalog(document with
        { Categories = [root, child, new LedgerCategory(Guid.NewGuid(), "Petit-enfant", child.Id)] }));
        Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateCatalog(document with
        { Categories = [root, new LedgerCategory(Guid.NewGuid(), " parent ")] }));
        Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateCatalog(document with
        { Payees = [new(Guid.NewGuid(), "École"), new(Guid.NewGuid(), "e\u0301COLE")] }));
        Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateCatalog(document with
        { PaymentMethods = [new(Guid.Empty, "Carte")] }));
    }

    [TestMethod]
    public void TransactionRulesRejectPhantomReferencesWrongParentCurrencyAndInvalidTombstones()
    {
        var model = new MainViewModel(Document());
        var value = model.AddTransaction(Draft());
        var metadata = model.CaptureDocument();
        var otherRoot = new LedgerCategory(Guid.NewGuid(), "Autre parent");
        var invalid = new[]
        {
            value with { Id = Guid.Empty }, value with { AccountId = Guid.NewGuid() },
            value with { Currency = new Currency("USD", "Dollar", 2) },
            value with { PayeeId = null }, value with { CategoryId = null },
            value with { PaymentMethodId = null }, value with { SubcategoryId = Guid.NewGuid() },
            value with { Revision = 0 }, value with { Status = (ReconciliationStatus)99 },
            value with { CreatedUtc = value.CreatedUtc.ToOffset(TimeSpan.FromHours(1)) },
            value with { ModifiedUtc = value.CreatedUtc.AddTicks(-1) },
            value with { IsDeleted = true, Amount = -1m },
            value with { Valuation = new MoneyValuation(1m, Euro, Day, "Non pris en charge") }
        };
        foreach (var candidate in invalid)
        {
            try { FinancialRules.ValidateTransaction(candidate, metadata); Assert.Fail("Une opération invalide a été acceptée."); }
            catch (ArgumentException) { }
        }
        Assert.ThrowsException<ArgumentException>(() => FinancialRules.ValidateTransaction(value with { CategoryId = otherRoot.Id, Category = new Category(otherRoot.Name) { Subcategory = value.Category.Subcategory } },
            metadata with { Categories = metadata.Categories.Concat([otherRoot]).ToArray() }));
    }

    [TestMethod]
    public void SourceFailuresPreserveStagingSelectionAndCatalogButCommittedSaveClearsDrafts()
    {
        var model = new MainViewModel(Document());
        var inserted = model.AddTransaction(Draft());
        var rows = model.Rows;
        var selected = model.SelectedAccount;
        var catalogs = model.CaptureDocument();
        model.SetLedgerSource(new UnavailableLedger());
        Assert.ThrowsException<IOException>(() => model.AddTransaction(Draft() with { PayeeName = "Nouveau tiers" }));
        Assert.ThrowsException<IOException>(() => model.EditTransaction(inserted.Id, inserted.Revision, Draft(2m)));
        Assert.ThrowsException<IOException>(() => model.DeleteTransaction(inserted.Id, inserted.Revision));
        Assert.AreSame(rows, model.Rows);
        Assert.AreSame(selected, model.SelectedAccount);
        Assert.AreEqual(1, model.PendingChanges.Count);
        Assert.AreEqual(catalogs.Payees.Count, model.Payees.Count);
        Assert.AreEqual(8.75m, selected!.Balance);
        var committed = model.CaptureDocument() with
        {
            OperationCount = 1, LedgerDigest = "storage-authoritative",
            AccountBalances = new Dictionary<Guid, decimal> { [selected.Account.Id] = 8.75m }
        };
        model.AcceptSaved(committed);
        Assert.AreEqual(0, model.PendingChanges.Count);
        Assert.IsFalse(model.IsDirty);
        Assert.AreEqual(committed.LedgerDigest, model.CaptureDocument().LedgerDigest);
        Assert.AreEqual(1L, model.CaptureDocument().OperationCount);
        StringAssert.Contains(model.LedgerErrorText, "Enregistrement effectué");
    }

    [TestMethod]
    public void PersistedEditsKeepOriginalExpectedRevisionStatusIdentityAndSoftDelete()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Moneta-ledger-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var model = new MainViewModel(Document());
            var inserted = model.AddTransaction(Draft());
            var marked = inserted with { Status = ReconciliationStatus.Marked };
            using var file = MonetaFile.Create(Path.Combine(directory, "ledger.moneta"), model.CaptureDocument(), [new TransactionChange(marked, 0)]);
            model.SetLedgerSource(file);
            model.AcceptSaved(file.Snapshot);
            Assert.AreEqual(0, model.PendingChanges.Count);
            Assert.IsFalse(model.IsDirty);
            var edited = model.EditTransaction(inserted.Id, 1, Draft(4m, TransactionDirection.Credit));
            Assert.AreEqual(2L, edited.Revision);
            Assert.AreEqual(1L, model.PendingChanges.Single().ExpectedRevision);
            Assert.AreEqual(ReconciliationStatus.Marked, edited.Status);
            Assert.AreEqual(inserted.CreatedUtc, edited.CreatedUtc);
            Assert.AreEqual(inserted.Currency, edited.Currency);
            Assert.ThrowsException<InvalidOperationException>(() => model.EditTransaction(inserted.Id, 1, Draft()));
            Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransaction(inserted.Id, 1));
            model.EditTransaction(inserted.Id, 2, Draft(3m));
            Assert.AreEqual(2L, model.PendingChanges.Single().Value.Revision);
            Assert.AreEqual(1L, model.PendingChanges.Single().ExpectedRevision);
            Assert.AreEqual(8.75m, file.Snapshot.AccountBalances![inserted.AccountId], "Staging must not write SQL.");
            model.DeleteTransaction(inserted.Id, 2);
            var tombstone = model.PendingChanges.Single();
            Assert.IsTrue(tombstone.Value.IsDeleted);
            Assert.AreEqual(2L, tombstone.Value.Revision);
            Assert.AreEqual(1L, tombstone.ExpectedRevision);
            Assert.AreEqual(0, model.Rows.Count);
            Assert.AreEqual(10m, model.SelectedAccount!.Balance);
            file.Save(model.CaptureDocument(), model.PendingChanges);
            model.AcceptSaved(file.Snapshot);
            Assert.IsNull(file.GetTransaction(inserted.Id));
            Assert.IsTrue(file.GetTransaction(inserted.Id, includeDeleted: true)!.IsDeleted);
            Assert.AreEqual(10m, model.SelectedAccount.Balance);
            Assert.AreEqual(1L, model.CaptureDocument().OperationCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ReadFailureDoesNotChangeSelectedAccountOrPartiallyAddAnAccount()
    {
        var model = new MainViewModel(Document());
        var first = model.SelectedAccount!;
        var second = model.AddAccount("Second", Euro, null, 20m, null);
        model.SelectedAccount = first;
        model.AddTransaction(Draft());
        var rows = model.Rows;
        var changes = model.PendingChanges.ToArray();
        model.SetLedgerSource(new UnavailableLedger());
        Assert.ThrowsException<IOException>(() => model.SelectedAccount = second);
        Assert.ThrowsException<IOException>(() => model.AddAccount("Impossible", Euro, null, 30m, null));
        Assert.AreSame(first, model.SelectedAccount);
        Assert.AreSame(rows, model.Rows);
        Assert.AreEqual(2, model.Accounts.Count);
        CollectionAssert.AreEqual(changes, model.PendingChanges.ToArray());
    }

    [TestMethod]
    public void PersistedMetadataWithoutItsLedgerIsAnExplicitErrorNotAnEmptyRegister()
    {
        var document = Document() with { OperationCount = 1, LedgerDigest = "persisted" };
        Assert.ThrowsException<InvalidOperationException>(() => new MainViewModel(document));
    }

    [TestMethod]
    public void StoredPagesOverlayPendingDateMovesAndPreserveExactTotalsUntilManualSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Moneta-ledger-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var initial = new MainViewModel(Document(opening: 5m));
            for (var index = 0; index < 105; index++)
                initial.AddTransaction(Draft(1m, TransactionDirection.Credit) with { Date = Day.AddDays(index) });
            using var file = MonetaFile.Create(Path.Combine(directory, "pages.moneta"), initial.CaptureDocument(), initial.PendingChanges);
            var model = new MainViewModel(file.Snapshot, file);
            Assert.AreEqual(100, model.Rows.Count);
            Assert.AreEqual(6m, model.Rows[0].Balance);
            Assert.AreEqual(110m, model.SelectedAccount!.Balance);
            var first = model.Rows[0].Transaction;
            model.EditTransaction(first.Id, first.Revision,
                Draft(2m, TransactionDirection.Credit) with { Date = Day.AddDays(200) });
            Assert.AreEqual(5, model.Rows.Count);
            Assert.AreEqual(first.Id, model.SelectedRow!.Transaction.Id);
            Assert.AreEqual(first.Id, model.Rows[^1].Transaction.Id);
            Assert.AreEqual(111m, model.Rows[^1].Balance);
            Assert.AreEqual(111m, model.SelectedAccount.Balance);
            Assert.AreEqual(Day, file.GetTransaction(first.Id)!.Date);
            Assert.AreEqual(110m, file.Snapshot.AccountBalances![first.AccountId]);
            var inserted = model.AddTransaction(Draft(0.01m) with { Date = Day.AddDays(-1) });
            Assert.IsFalse(model.CanPreviousPage);
            Assert.AreEqual(inserted.Id, model.Rows[0].Transaction.Id);
            Assert.AreEqual(4.99m, model.Rows[0].Balance);
            Assert.AreEqual(110.99m, model.SelectedAccount.Balance);
            Assert.AreEqual(2, model.PendingChanges.Count);
            file.Save(model.CaptureDocument(), model.PendingChanges);
            model.AcceptSaved(file.Snapshot);
            Assert.AreEqual(0, model.PendingChanges.Count);
            Assert.IsFalse(model.IsDirty);
            Assert.AreEqual(110.99m, model.SelectedAccount.Balance);
            Assert.AreEqual(106L, model.CaptureDocument().OperationCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class UnavailableLedger : ILedgerSource
    {
        public LedgerPage ReadPage(Account account, int offset, int pageSize,
            IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null) =>
            throw new IOException("Lecture synthétique indisponible.");
    }
}
