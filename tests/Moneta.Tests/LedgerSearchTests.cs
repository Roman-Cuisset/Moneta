using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class LedgerSearchTests
{
    private static readonly DateOnly Date = new(2025, 1, 15);
    private string _directory = null!;
    private string Pathname => Path.Combine(_directory, "recherche.moneta");
    private Guid _document;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("MonetaSearch-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    private LedgerSearchPage Search(LedgerSearchCriteria query, int index = 0, CancellationToken token = default) =>
        MonetaFile.SearchOperations(Pathname, _document, query, index, token);

    [TestMethod]
    public void UnavailableFileDuringConnectionOpenReportsDocumentError()
    {
        var document = FinancialDocument.New("Ouverture bloquée");
        _document = document.Id;
        using (MonetaFile.Create(Pathname, document)) { }
        using var exclusive = File.Open(Pathname, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsException<DocumentException>(() => Search(new()));
    }

    [TestMethod]
    public void InvalidPersistedDateDuringSearchReportsDocumentError()
    {
        using (var workspace = NewWorkspace(out _, out _, out _))
        {
            workspace.Model!.AddTransaction(new(Date, 1m, TransactionDirection.Credit, "Date fictive"));
            workspace.Save(Pathname);
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Pathname, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints=ON; UPDATE Transactions SET date='2025-02-30';";
            command.ExecuteNonQuery();
        }
        var error = Assert.ThrowsException<DocumentException>(() => Search(new()));
        Assert.AreEqual(DocumentError.Corrupt, error.Error);
    }

    private DocumentWorkspace NewWorkspace(out Guid eur, out Guid byn, out Guid rub)
    {
        var workspace = new DocumentWorkspace();
        workspace.New("Recherche fictive");
        var model = workspace.Model!;
        eur = model.AddAccount("Courant", model.CurrencyRegistry.Get("EUR"), null, 0m, null).Account.Id;
        byn = model.AddAccount("Courant", model.CurrencyRegistry.Get("BYN"), null, 0m, null).Account.Id;
        rub = model.AddAccount("Frais", model.CurrencyRegistry.Get("RUB"), null, 0m, null).Account.Id;
        _document = model.CaptureDocument().Id;
        return workspace;
    }

    [TestMethod]
    public void AccentCompositionLiteralPunctuationTokensAndStructuredFilters()
    {
        using var workspace = NewWorkspace(out var eur, out _, out _);
        var model = workspace.Model!;
        model.SelectedAccount = model.Accounts[0];
        var match = model.AddTransaction(new(Date, 51.23m, TransactionDirection.Debit, "Épicerie 日本語",
            "Chez Irène", "Alimentation", "Marché", "Carte bancaire", "Note %_ ' | /"));
        model.AddTransaction(new(Date.AddDays(1), 50m, TransactionDirection.Debit, "Épicerie", "Chez Irène", "Alimentation", "Marché", "Carte bancaire"));
        workspace.Save(Pathname);
        var criteria = new LedgerSearchCriteria
        {
            AccountId = eur, Text = "  e\u0301PIC 日本 %_ ' | / ", Payee = "IRÈ", Category = "ali", Subcategory = "MARCHÉ",
            PaymentMethod = "banc", From = Date, Through = Date, Maximum = -51m,
            Kind = LedgerSearchKind.Expense, Status = LedgerSearchStatus.Unmarked
        };
        var row = Search(criteria).Rows.Single();
        Assert.AreEqual(match.Id, row.Id);
        Assert.AreEqual(-51.23m, row.SignedAmount);
        Assert.AreEqual("Marché", row.Subcategory);
        Assert.AreEqual(0L, Search(criteria with { Text = "epicerie" }).Total);
        Assert.AreEqual(0L, Search(criteria with { Text = "' OR 1=1 --" }).Total);
        Assert.AreEqual(0L, Search(criteria with { Through = Date.AddDays(-1), From = null }).Total);
        Assert.AreEqual(2L, Search(new() { AccountId = eur, Text = " \t " }).Total);
        Assert.IsFalse(workspace.HasUnsavedChanges);
    }

    [TestMethod]
    public void SignedAmountsMatchWithinNativeCurrencyIncludingZeroAndHighPrecision()
    {
        using var workspace = NewWorkspace(out var eur, out _, out _);
        var model = workspace.Model!;
        foreach (var account in model.Accounts.Take(2))
        {
            model.SelectedAccount = account;
            model.AddTransaction(new(Date, 100, TransactionDirection.Credit, "Entrée"));
            model.AddTransaction(new(Date, 100, TransactionDirection.Debit, "Sortie"));
            model.AddTransaction(new(Date, 0, TransactionDirection.Debit, "Zéro"));
        }
        workspace.Save(Pathname);
        Assert.AreEqual(2L, Search(new() { Exact = 100 }).Total);
        Assert.AreEqual("BYN", Search(new() { Exact = 100, CurrencyCode = "BYN" }).Rows.Single().CurrencyCode);
        Assert.AreEqual(eur, Search(new() { Minimum = -100, Maximum = -99.999m, CurrencyCode = "EUR" }).Rows.Single().AccountId);
        Assert.AreEqual(0L, Search(new() { Exact = -99.999m }).Total);
        Assert.AreEqual(2L, Search(new() { Exact = 0, Kind = LedgerSearchKind.Expense }).Total);
        Assert.AreEqual(4L, Search(new() { Maximum = 0 }).Total);
        Assert.AreEqual(-51.24m, LedgerSearchCriteria.ParseAmount("  -51,24  "));
        Assert.ThrowsException<ArgumentException>(() => LedgerSearchCriteria.ParseAmount("0,00000000000000000000000000001"));
    }

    [TestMethod]
    public void PointingArchiveTransferAndTombstoneAreReadWithoutMutatingLedger()
    {
        using var workspace = NewWorkspace(out var eur, out var byn, out var rub);
        var model = workspace.Model!;
        model.SelectedAccount = model.Accounts[0];
        var reconciled = model.AddTransaction(new(Date, 20, TransactionDirection.Credit, "Déjà rapproché"));
        var tombstone = model.AddTransaction(new(Date, 2, TransactionDirection.Credit, "Tombstone"));
        var marked = model.AddTransaction(new(Date.AddDays(1), 1, TransactionDirection.Credit, "A pointer"));
        var link = model.CreateTransfer(new(eur, byn, Date.AddDays(2), 10m, 31m, "Référence-α", "Règlement",
            FeeAccountId: rub, FeeAmount: 1m));
        workspace.Save(Pathname);
        model.StageReconciliation(new(Date, 20), [reconciled.Id]);
        workspace.Save();
        model.DeleteTransaction(tombstone.Id, tombstone.Revision);
        model.SetPointing([new(marked.Id, marked.Revision)], true, Date.AddDays(1));
        model.SetAccountArchived(byn, true);
        model.SetAccountArchived(rub, true);
        workspace.Save();
        workspace.Dispose();
        var before = SHA256.HashData(File.ReadAllBytes(Pathname));
        using var reopened = new DocumentWorkspace();
        reopened.Open(Pathname);
        model = reopened.Model!;
        Assert.AreEqual(0L, Search(new() { Text = "Tombstone", IncludeArchived = true }).Total);
        Assert.AreEqual(1L, Search(new() { Status = LedgerSearchStatus.Marked }).Total);
        Assert.AreEqual(reconciled.Id, Search(new() { Status = LedgerSearchStatus.Reconciled }).Rows.Single().Id);
        Assert.AreEqual(2L, Search(new() { Status = LedgerSearchStatus.Marked | LedgerSearchStatus.Reconciled }).Total);
        Assert.AreEqual(2L, Search(new() { Status = LedgerSearchStatus.Unmarked | LedgerSearchStatus.Marked }).Total);
        Assert.AreEqual(1L, Search(new() { Kind = LedgerSearchKind.Transfers }).Total);
        var transfers = Search(new() { Text = "référence-α règlement", IncludeArchived = true });
        Assert.AreEqual(3L, transfers.Total);
        Assert.IsTrue(transfers.Rows.All(row => row.TransferId == link.Value.Id));
        var fee = Search(new() { IncludeArchived = true, Kind = LedgerSearchKind.Fees }).Rows.Single();
        Assert.AreEqual(rub, fee.AccountId);
        Assert.AreEqual(LedgerTransferLeg.Fee, fee.TransferRole);
        Assert.AreEqual(1L, Search(new() { AccountId = byn }).Total);
        var page = model.LocateOperation(fee.AccountId, fee.Id)!;
        Assert.IsTrue(model.ShowLocatedOperation(fee.AccountId, fee.Id, page));
        Assert.IsTrue(model.SelectedAccount!.Account.IsArchived);
        Assert.AreEqual(link.Value.Id, model.SelectedRow!.TransferId);
        Assert.IsFalse(reopened.HasUnsavedChanges);
        reopened.Dispose();
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(Pathname)));
    }

    [TestMethod]
    public async Task PagesAndStableTiesUseTheWholeLedgerAndConcurrentQueriesRemainIndependent()
    {
        var document = FinancialDocument.New("Nombreuses écritures fictives");
        _document = document.Id;
        var currency = document.Currencies.First(c => c.Code == "EUR");
        var account = new Account(Guid.NewGuid(), "Test", currency, 0m);
        var rows = Enumerable.Range(1, 1350).Select(number => new Transaction(
            Guid.Parse($"00000000-0000-0000-0000-{number:D12}"), account.Id, Date.AddDays(number / 300),
            number == 1275 ? "Repère au-delà de la première page" : "Même libellé",
            new Payee(""), new Category(""), 2m, currency, TransactionDirection.Credit, ReconciliationStatus.Unmarked, "")
        {
            CreatedUtc = document.CreatedUtc,
            ModifiedUtc = document.CreatedUtc
        }).ToArray();
        using var file = MonetaFile.Create(Pathname, document with { Accounts = [account], SelectedAccountId = account.Id },
            rows.Select(row => new TransactionChange(row, 0)).ToArray());
        var sorting = new LedgerSearchCriteria { Sort = LedgerSearchSort.Label, Descending = false };
        var first = Search(sorting);
        var second = Search(sorting, 1);
        Assert.AreEqual(1350L, first.Total);
        Assert.AreEqual(100, first.Rows.Count);
        CollectionAssert.AreEqual(rows.Skip(100).Take(100).Select(row => row.Id).ToArray(), second.Rows.Select(row => row.Id).ToArray());
        Assert.AreEqual(13, Search(sorting, int.MaxValue).Index);
        var outgoing = Enumerable.Range(0, 6).Select(index => Task.Run(() =>
            Search(sorting with { Text = index % 2 == 0 ? "repère" : "introuvable" }))).ToArray();
        var captures = await Task.WhenAll(outgoing);
        Assert.AreEqual(rows[1274].Id, captures[0].Rows.Single().Id);
        Assert.AreEqual(0L, captures[1].Total);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => Search(sorting, token: cts.Token));
        var model = new Moneta.Core.Presentation.MainViewModel(file.Snapshot, file);
        var page = model.LocateOperation(account.Id, rows[1274].Id)!;
        Assert.IsTrue(page.Offset >= 1200);
        Assert.IsTrue(model.ShowLocatedOperation(account.Id, rows[1274].Id, page));
        Assert.AreEqual(rows[1274].Id, model.SelectedRow!.Transaction.Id);
        Assert.IsFalse(model.IsDirty);
        Assert.IsNull(model.LocateOperation(account.Id, Guid.NewGuid()));
    }

    [TestMethod]
    public void AmountSortKeepsNativeGroupsAndRetainsTheSmallestUnitAcrossScales()
    {
        var document = FinancialDocument.New("Précisions fictives");
        _document = document.Id;
        var eur = document.Currencies.Single(currency => currency.Code == "EUR");
        var fine = new Currency("XXX", "Unité fictive", 28);
        var standard = new Account(Guid.NewGuid(), "A", eur, 0);
        var precise = new Account(Guid.NewGuid(), "B", fine, 0);
        TransactionChange Row(Account account, decimal amount, TransactionDirection direction) =>
            new(new(Guid.NewGuid(), account.Id, Date.AddYears(100), "Futur", new Payee(""), new Category(""),
                amount, account.Currency, direction, ReconciliationStatus.Unmarked, ""), 0);
        var small = ExactMoney.FromMinorUnits(1, fine);
        Assert.AreEqual(small, LedgerSearchCriteria.ParseAmount("0," + new string('0', 27) + "1"));
        var max = ExactMoney.FromMinorUnits(long.MaxValue, eur);
        using var file = MonetaFile.Create(Pathname,
            document with { Accounts = [standard, precise], Currencies = [..document.Currencies, fine] },
            [Row(standard, max, TransactionDirection.Debit), Row(standard, 1m, TransactionDirection.Credit),
             Row(precise, small, TransactionDirection.Credit), Row(precise, small * 2, TransactionDirection.Credit)]);
        var exact = new LedgerSearchCriteria { Exact = small, From = Date.AddYears(99) };
        Assert.AreEqual("XXX", Search(exact).Rows.Single().CurrencyCode);
        Assert.AreEqual(-max, Search(exact with { Exact = -max }).Rows.Single().SignedAmount);
        var descending = new LedgerSearchCriteria { Sort = LedgerSearchSort.Amount };
        CollectionAssert.AreEqual(new[] { 1m, -max, small * 2, small }, Search(descending).Rows.Select(row => row.SignedAmount).ToArray());
        CollectionAssert.AreEqual(new[] { -max, 1m, small, small * 2 }, Search(descending with { Descending = false }).Rows.Select(row => row.SignedAmount).ToArray());
        Assert.ThrowsException<DocumentException>(() => MonetaFile.SearchOperations(Pathname, Guid.NewGuid(), new(), 0));
    }
}
