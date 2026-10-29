using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;

namespace Moneta.Tests;

[TestClass]
public sealed class DocumentPresentationTests
{
    private static readonly DateTimeOffset Created = new(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);
    private static readonly DateTimeOffset Modified = Created.AddDays(1);
    private static readonly Currency Kwd = new("KWD", "Dinar koweïtien", 3)
    {
        Symbol = "KD",
        DisplayRules = new CurrencyDisplayRules("fr-FR", true, true)
    };

    private static FinancialDocument Document(params Account[] accounts) =>
        new(Guid.NewGuid(), "Dossier personnel", Created, Modified, [Kwd], accounts);

    [TestMethod]
    public void DemoCannotCaptureOrAcceptARealDocumentAndStillSupportsDemoAccounts()
    {
        var model = new MainViewModel(DemoData.Create());
        Assert.IsTrue(model.IsDemo);
        Assert.IsFalse(model.IsDirty);
        Assert.ThrowsException<InvalidOperationException>(() => model.CaptureDocument());
        Assert.ThrowsException<InvalidOperationException>(() => model.AcceptSaved(FinancialDocument.New("Autre")));
        Assert.ThrowsException<InvalidOperationException>(() => model.AddAccount("Réel", model.CurrencyRegistry.Get("EUR"), null, 1m, null));
        model.AddDemoAccount("Fictif", model.CurrencyRegistry.Get("EUR"), null, 1m, null);
        Assert.IsFalse(model.IsDirty);
    }

    [TestMethod]
    public void NewDocumentStartsCleanWithoutDemoAccountsOrTransactions()
    {
        var document = FinancialDocument.New("Nouveau dossier");
        var model = new MainViewModel(document);
        Assert.IsFalse(model.IsDemo);
        Assert.IsFalse(model.IsDirty);
        Assert.AreEqual(document.Id, model.DocumentId);
        Assert.AreEqual(document.Name, model.DocumentName);
        Assert.AreEqual(0, model.Accounts.Count);
        Assert.AreEqual(0, model.Rows.Count);
        Assert.IsNull(model.SelectedAccountId);
        Assert.IsNull(model.SelectedRow);
        Assert.IsFalse(model.StatusText.Contains("fictives", StringComparison.Ordinal));
        var captured = model.CaptureDocument();
        Assert.AreEqual(document.CreatedUtc, captured.CreatedUtc);
        Assert.AreEqual(document.ModifiedUtc, captured.ModifiedUtc);
        Assert.AreEqual(0, captured.Accounts.Count);
        CollectionAssert.AreEqual(document.Currencies.ToArray(), captured.Currencies.ToArray());
    }

    [TestMethod]
    public void RealDocumentUsesOnlyItsCurrencyDefinitionsIncludingNonDefaultEuro()
    {
        var customEuro = new Currency("EUR", "Euro du dossier", 2)
        {
            Symbol = "EUR dossier",
            DisplayRules = new CurrencyDisplayRules("en-US", true, true)
        };
        var document = Document() with { Currencies = [Kwd, customEuro] };
        var model = new MainViewModel(document);
        CollectionAssert.AreEqual(new[] { "KWD", "EUR" }, model.DisplayCurrencies.Select(currency => currency.Code).ToArray());
        Assert.AreEqual(customEuro, model.CurrencyRegistry.Get("EUR"));
        Assert.IsFalse(model.CurrencyRegistry.TryGet("BYN", out _));
        Assert.IsFalse(model.CurrencyRegistry.TryGet("RUB", out _));
        Assert.IsFalse(model.CurrencyRegistry.TryGet("USD", out _));
        CollectionAssert.AreEqual(document.Currencies.ToArray(), model.CaptureDocument().Currencies.ToArray());
    }

    [TestMethod]
    public void ImportedAccountMetadataAndExactNativeOpeningBalanceSurviveCapture()
    {
        var account = new Account(Guid.NewGuid(), "Compte archivé", Kwd, -9223372036854775.807m)
        {
            Type = "Épargne",
            Notes = "Notes conservées\nSans changement",
            IsArchived = true,
            CreatedUtc = Created.AddMonths(-1),
            ModifiedUtc = Modified.AddHours(-1)
        };
        var document = Document(account) with { SelectedAccountId = account.Id };
        var model = new MainViewModel(document);
        Assert.AreEqual(account.Id, model.SelectedAccountId);
        Assert.AreEqual(account.OpeningBalance, model.SelectedAccount!.Balance);
        Assert.AreEqual(0, model.Rows.Count);
        Assert.IsFalse(model.IsDirty);
        Assert.IsFalse(model.StatusText.Contains("fictives", StringComparison.Ordinal));
        var captured = model.CaptureDocument();
        Assert.AreEqual(document.Id, captured.Id);
        Assert.AreEqual(document.SchemaVersion, captured.SchemaVersion);
        Assert.AreEqual(Created, captured.CreatedUtc);
        Assert.AreEqual(Modified, captured.ModifiedUtc);
        Assert.AreEqual(account, captured.Accounts.Single());
        Assert.AreEqual(-long.MaxValue, ExactMoney.ToMinorUnits(captured.Accounts.Single().OpeningBalance, Kwd));
        Assert.AreEqual(Kwd.DisplayRules, captured.Currencies.Single().DisplayRules);
    }

    [TestMethod]
    public void RealAccountAdditionIsStagedUntilAcceptedSaveAndNotifiesDirtyChanges()
    {
        var document = Document();
        var model = new MainViewModel(document);
        var dirtyStates = new List<bool>();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsDirty))
                dirtyStates.Add(model.IsDirty);
        };
        var before = DateTimeOffset.UtcNow;
        var added = model.AddAccount("  Compte réel  ", Kwd, " Courant ", 1.234m, " Note ");
        var after = DateTimeOffset.UtcNow;
        Assert.IsTrue(model.IsDirty);
        Assert.AreEqual(0, document.Accounts.Count);
        Assert.AreSame(added, model.SelectedAccount);
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual("Compte réel", added.Name);
        Assert.AreEqual("Courant", added.Account.Type);
        Assert.AreEqual("Note", added.Account.Notes);
        Assert.IsFalse(added.Account.IsArchived);
        Assert.AreEqual(TimeSpan.Zero, added.Account.CreatedUtc.Offset);
        Assert.IsTrue(added.Account.CreatedUtc >= before && added.Account.CreatedUtc <= after);
        Assert.AreEqual(added.Account.CreatedUtc, added.Account.ModifiedUtc);
        var snapshot = model.CaptureDocument();
        Assert.IsTrue(model.IsDirty);
        Assert.AreEqual(Created, snapshot.CreatedUtc);
        Assert.IsTrue(snapshot.ModifiedUtc >= before);
        Assert.AreEqual(added.Account, snapshot.Accounts.Single());
        Assert.AreEqual(added.Account.Id, snapshot.SelectedAccountId);
        Assert.AreEqual(1234L, ExactMoney.ToMinorUnits(snapshot.Accounts.Single().OpeningBalance, Kwd));
        model.AcceptSaved(snapshot);
        Assert.IsFalse(model.IsDirty);
        CollectionAssert.AreEqual(new[] { true, false }, dirtyStates);
        Assert.AreEqual(snapshot.ModifiedUtc, model.CaptureDocument().ModifiedUtc);
        Assert.AreEqual(added.Account.CreatedUtc, model.CaptureDocument().Accounts.Single().CreatedUtc);
    }

    [TestMethod]
    public void RealDocumentRejectsDemoCreationAndInvalidAmountsWithoutPartialEdits()
    {
        var model = new MainViewModel(Document());
        Assert.ThrowsException<InvalidOperationException>(() => model.AddDemoAccount("Démo", Kwd, null, 1m, null));
        Assert.ThrowsException<ArgumentException>(() => model.AddAccount("Fraction", Kwd, null, 1.2345m, null));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => model.AddAccount("Trop grand", Kwd, null, 9223372036854775.808m, null));
        Assert.ThrowsException<ArgumentException>(() => model.AddAccount("Devise absente", new Currency("USD", "Dollar", 2), null, 1m, null));
        Assert.AreEqual(0, model.Accounts.Count);
        Assert.IsNull(model.SelectedAccount);
        Assert.IsFalse(model.IsDirty);
    }

    [TestMethod]
    public void SelectionIsCapturedWithoutDirtyingOrChangingDocumentTimestamps()
    {
        var first = new Account(Guid.NewGuid(), "Premier", Kwd, 1.001m);
        var second = new Account(Guid.NewGuid(), "Second", Kwd, 2.002m);
        var model = new MainViewModel(Document(first, second));
        Assert.IsNull(model.SelectedAccount);
        model.SelectedAccount = model.Accounts[1];
        Assert.AreEqual(second.Id, model.SelectedAccountId);
        Assert.AreEqual(second.Id, model.CaptureDocument().SelectedAccountId);
        Assert.IsFalse(model.IsDirty);
        Assert.AreEqual(Modified, model.CaptureDocument().ModifiedUtc);
        model.SelectedAccount = null;
        Assert.IsNull(model.CaptureDocument().SelectedAccountId);
        Assert.IsFalse(model.IsDirty);
        Assert.IsFalse(model.StatusText.Contains("fictives", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisplayPreferenceNeverChangesTheCapturedNativeCurrencyOrMoney()
    {
        var account = new Account(Guid.NewGuid(), "Natif", Kwd, 12.345m);
        var euro = new Currency("EUR", "Euro", 2);
        var document = Document(account) with { Currencies = [Kwd, euro], SelectedAccountId = account.Id };
        var model = new MainViewModel(document) { DisplayCurrency = euro };
        Assert.AreEqual("Cours indisponible", model.DisplayBalanceText);
        Assert.AreEqual(account, model.CaptureDocument().Accounts.Single());
        Assert.AreEqual(Kwd, model.CaptureDocument().Accounts.Single().Currency);
        Assert.IsFalse(model.IsDirty);
    }

    [TestMethod]
    public void SeparateDocumentsNeverShareAccountsCurrencyRegistriesOrSelections()
    {
        var first = new MainViewModel(Document());
        var second = new MainViewModel(Document());
        first.AddAccount("Premier uniquement", Kwd, null, 1.234m, null);
        first.CurrencyRegistry.Register(new Currency("USD", "Dollar", 2));
        Assert.AreNotEqual(first.DocumentId, second.DocumentId);
        Assert.AreEqual(0, second.Accounts.Count);
        Assert.IsNull(second.SelectedAccountId);
        Assert.IsFalse(second.IsDirty);
        Assert.IsFalse(second.CurrencyRegistry.TryGet("USD", out _));
        Assert.ThrowsException<ArgumentException>(() => second.SelectedAccount = first.SelectedAccount);
        Assert.ThrowsException<ArgumentException>(() => first.AcceptSaved(second.CaptureDocument()));
        Assert.IsTrue(first.IsDirty);
    }

    [TestMethod]
    public void NewAccountMetadataDefaultsAreUtcAndNativeCurrencyRemainsGetOnly()
    {
        var before = DateTimeOffset.UtcNow;
        var account = new Account(Guid.NewGuid(), "Nouveau", Kwd, 0m);
        var after = DateTimeOffset.UtcNow;
        Assert.IsFalse(account.IsArchived);
        Assert.AreEqual(TimeSpan.Zero, account.CreatedUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, account.ModifiedUtc.Offset);
        Assert.IsTrue(account.CreatedUtc >= before && account.CreatedUtc <= after);
        Assert.IsTrue(account.ModifiedUtc >= before && account.ModifiedUtc <= after);
        Assert.IsNull(typeof(Account).GetProperty(nameof(Account.Currency))!.SetMethod);
    }
}
