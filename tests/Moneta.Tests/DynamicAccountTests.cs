using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;

namespace Moneta.Tests;

[TestClass]
public sealed class DynamicAccountTests
{
    [TestMethod]
    public void RegistryIsSeededWithDistinctIsoCurrenciesAndCanExtendWhileBound()
    {
        var registry = new CurrencyRegistry();
        var model = new MainViewModel(new DemoPortfolio([], []), registry);
        CollectionAssert.AreEqual(new[] { "EUR", "BYN", "RUB" }, model.DisplayCurrencies.Select(currency => currency.Code).ToArray());
        Assert.AreEqual("€", registry.Get("EUR").Symbol);
        Assert.AreEqual("Rouble biélorusse", registry.Get("BYN").Name);
        Assert.AreEqual(2, registry.Get("RUB").DecimalPlaces);
        var changes = 0;
        ((INotifyCollectionChanged)model.DisplayCurrencies).CollectionChanged += (_, _) => changes++;
        var kwd = registry.Register(new Currency("KWD", "Dinar koweïtien", 3) { Symbol = "د.ك" });
        Assert.AreEqual(1, changes);
        Assert.AreSame(kwd, registry.Get("kwd"));
        Assert.AreSame(kwd, model.DisplayCurrencies[3]);
        Assert.AreSame(kwd, registry.Register(kwd));
        Assert.AreEqual(1, changes);
        Assert.ThrowsException<ArgumentException>(() => registry.Register(kwd with { DecimalPlaces = 2 }));
        Assert.AreEqual(4, registry.Currencies.Count);
    }

    [TestMethod]
    public void ManySameCurrencyAccountsAreIndependentObservableAndSelectedOnAddition()
    {
        var model = new MainViewModel(new DemoPortfolio([], []));
        var euro = model.CurrencyRegistry.Get("EUR");
        var changes = 0;
        ((INotifyCollectionChanged)model.Accounts).CollectionChanged += (_, args) =>
        {
            Assert.AreEqual(NotifyCollectionChangedAction.Add, args.Action);
            changes++;
        };
        for (var index = 0; index < 200; index++)
        {
            var summary = model.AddDemoAccount("  Même nom démo  ", euro, " Courant ", index + 0.25m, " Note fictive ");
            Assert.AreSame(summary, model.SelectedAccount);
            Assert.AreSame(euro, summary.Account.Currency);
            Assert.AreEqual("Même nom démo", summary.Name);
            Assert.AreEqual("Courant", summary.Account.Type);
            Assert.AreEqual("Note fictive", summary.Account.Notes);
            Assert.AreEqual(index + 0.25m, summary.Balance);
            Assert.AreEqual(0, model.Rows.Count);
            Assert.AreEqual(summary.BalanceText, model.DisplayBalanceText);
            Assert.AreSame(euro, model.DisplayCurrency);
        }
        Assert.AreEqual(200, model.Accounts.Count);
        Assert.AreEqual(200, changes);
        Assert.AreEqual(200, model.Accounts.Select(summary => summary.Account.Id).Distinct().Count());
        Assert.IsInstanceOfType<ReadOnlyObservableCollection<AccountSummary>>(model.Accounts);
        Assert.ThrowsException<NotSupportedException>(() => ((ICollection<AccountSummary>)model.Accounts).Clear());
        model.SelectedAccount = model.Accounts[0];
        Assert.AreEqual(0.25m, model.SelectedAccount.Balance);
        model.SelectedAccount = model.Accounts[199];
        Assert.AreEqual(199.25m, model.SelectedAccount.Balance);
    }

    [TestMethod]
    public void AdditionClearsPreviousRegisterSelectionAndRestoresNativeDisplay()
    {
        var model = new MainViewModel(DemoData.Create());
        model.SelectedRow = model.Rows[0];
        model.DisplayCurrency = model.CurrencyRegistry.Get("RUB");
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var added = model.AddDemoAccount("Nouveau compte fictif", model.CurrencyRegistry.Get("BYN"), null, -12.50m, null);
        Assert.AreSame(added, model.SelectedAccount);
        Assert.AreEqual(0, model.Rows.Count);
        Assert.IsNull(model.SelectedRow);
        Assert.AreEqual(string.Empty, model.SelectedRowDetails);
        Assert.AreEqual("-12,50 BYN", model.SelectedAccountBalanceText);
        Assert.AreEqual("-12,50 BYN", model.DisplayBalanceText);
        Assert.AreEqual("BYN", model.DisplayCurrency!.Code);
        foreach (var property in new[] { "SelectedAccount", "Rows", "SelectedRow", "SelectedRowDetails", "DisplayCurrency", "DisplayBalanceText", "DisplayRateDescription" })
            CollectionAssert.Contains(notifications, property);
    }

    [TestMethod]
    public void DisplayChoiceNeverConvertsOrMutatesNativeAccountTransactionsOrValuations()
    {
        var portfolio = DemoData.Create();
        var account = portfolio.Accounts[0];
        var transaction = portfolio.Transactions[0] with
        {
            Valuation = new MoneyValuation(99m, portfolio.Accounts[1].Currency, new DateOnly(2026, 10, 1), "Valorisation fictive")
        };
        var model = new MainViewModel(new DemoPortfolio([account], [transaction]));
        var originalBalance = model.SelectedAccount!.Balance;
        var rows = model.Rows;
        model.SelectedRow = rows[0];
        var originalDetails = model.SelectedRowDetails;
        foreach (var code in new[] { "BYN", "RUB", "EUR", "BYN" })
        {
            model.DisplayCurrency = model.CurrencyRegistry.Get(code);
            Assert.AreSame(account.Currency, model.SelectedAccount.Account.Currency);
            Assert.AreSame(transaction, model.Rows[0].Transaction);
            Assert.AreSame(rows, model.Rows);
            Assert.AreEqual(originalBalance, model.SelectedAccount.Balance);
            Assert.AreEqual(originalDetails, model.SelectedRowDetails);
            Assert.AreEqual(code == "EUR" ? model.SelectedAccountBalanceText : "Cours indisponible", model.DisplayBalanceText);
            StringAssert.Contains(model.DisplayRateDescription, "Aucun service de change actif");
            StringAssert.Contains(model.DisplayRateDescription, "aucun cours chargé");
        }
        Assert.IsNull(typeof(Account).GetProperty(nameof(Account.Currency))!.SetMethod);
    }

    [TestMethod]
    public void AccountSwitchResetsDisplayEvenBetweenAccountsSharingTheirNativeCurrency()
    {
        var model = new MainViewModel(DemoData.Create());
        var first = model.Accounts[0];
        var savings = model.Accounts[3];
        model.DisplayCurrency = model.CurrencyRegistry.Get("BYN");
        model.SelectedAccount = savings;
        Assert.AreEqual("EUR", model.DisplayCurrency!.Code);
        Assert.AreEqual(savings.BalanceText, model.DisplayBalanceText);
        model.DisplayCurrency = model.CurrencyRegistry.Get("RUB");
        model.SelectedAccount = first;
        Assert.AreEqual("EUR", model.DisplayCurrency!.Code);
        model.SelectedAccount = null;
        Assert.IsNull(model.DisplayCurrency);
        Assert.AreEqual(string.Empty, model.DisplayBalanceText);
        StringAssert.Contains(model.DisplayRateDescription, "Aucun compte sélectionné");
    }

    [TestMethod]
    public void FourthCurrencyUsesItsOwnPrecisionAndDisplayRulesWithoutAnFxProvider()
    {
        var model = new MainViewModel(new DemoPortfolio([], []));
        var kwd = model.CurrencyRegistry.Register(new Currency("KWD", "Dinar koweïtien", 3)
        {
            Symbol = "KD",
            DisplayRules = new CurrencyDisplayRules("fr-FR", UseSymbol: true, SymbolBeforeAmount: true)
        });
        var added = model.AddDemoAccount("Dinar démo", kwd, null, 1.234m, null);
        Assert.AreSame(kwd, added.Account.Currency);
        Assert.AreEqual("KD 1,234", model.DisplayBalanceText);
        Assert.AreEqual(1.234m, added.Balance);
        model.DisplayCurrency = model.CurrencyRegistry.Get("EUR");
        Assert.AreEqual("Cours indisponible", model.DisplayBalanceText);
        model.DisplayCurrency = kwd;
        Assert.AreEqual("KD 1,234", model.DisplayBalanceText);
        Assert.ThrowsException<ArgumentException>(() => model.AddDemoAccount("Fraction démo", kwd, null, 1.2345m, null));
    }

    [TestMethod]
    public void ValidationRejectsInvalidInputAtomicallyWithoutRoundingOrChangingSelection()
    {
        var model = new MainViewModel(DemoData.Create());
        var selected = model.SelectedAccount;
        var rows = model.Rows;
        var euro = model.CurrencyRegistry.Get("EUR");
        Action[] invalid =
        [
            () => model.AddDemoAccount(" ", euro, null, 0m, null),
            () => model.AddDemoAccount(new string('a', 121), euro, null, 0m, null),
            () => model.AddDemoAccount("Démo", euro, new string('a', 81), 0m, null),
            () => model.AddDemoAccount("Démo", euro, null, 0m, new string('a', 2001)),
            () => model.AddDemoAccount("Démo", euro, null, 0.001m, null),
            () => model.AddDemoAccount("Démo", new Currency("USD", "Dollar", 2), null, 1m, null),
            () => model.AddDemoAccount("Démo", euro with { DecimalPlaces = 3 }, null, 1m, null),
            () => model.AddDemoAccount("Démo", euro, null, 92233720368547758.08m, null),
            () => model.AddDemoAccount("Démo", euro, null, -92233720368547758.08m, null),
            () => model.AddDemoAccount("Démo", euro, null, decimal.MaxValue, null),
            () => model.AddDemoAccount("Démo", euro, null, decimal.MinValue, null)
        ];
        foreach (var add in invalid)
        {
            try
            {
                add();
                Assert.Fail("Une entrée invalide a été acceptée.");
            }
            catch (ArgumentException)
            {
                Assert.AreEqual(4, model.Accounts.Count);
                Assert.AreSame(selected, model.SelectedAccount);
                Assert.AreSame(rows, model.Rows);
            }
        }
        Assert.ThrowsException<ArgumentNullException>(() => model.AddDemoAccount("Démo", null!, null, 0m, null));
        Assert.ThrowsException<ArgumentException>(() => model.DisplayCurrency = null);
        Assert.ThrowsException<ArgumentException>(() => model.DisplayCurrency = new Currency("USD", "Dollar", 2));
        Assert.AreSame(selected, model.SelectedAccount);
    }

    [TestMethod]
    public void OpeningBalanceBoundsAreInclusiveAndPrecisionDoesNotImposeTwoDecimals()
    {
        var model = new MainViewModel(new DemoPortfolio([], []));
        var euro = model.CurrencyRegistry.Get("EUR");
        Assert.AreEqual(92233720368547758.07m, model.AddDemoAccount("Maximum démo", euro, null, 92233720368547758.07m, null).Balance);
        Assert.AreEqual(-92233720368547758.07m, model.AddDemoAccount("Minimum démo", euro, null, -92233720368547758.07m, null).Balance);
        Assert.AreEqual(1.23m, model.AddDemoAccount("Zéros de précision démo", euro, null, 1.2300m, null).Balance);
        var yen = model.CurrencyRegistry.Register(new Currency("JPY", "Yen", 0) { Symbol = "¥" });
        Assert.AreEqual("42 JPY", model.AddDemoAccount("Yen démo", yen, " ", 42m, " ").BalanceText);
        Assert.IsNull(model.SelectedAccount!.Account.Type);
        Assert.IsNull(model.SelectedAccount.Account.Notes);
        Assert.ThrowsException<ArgumentException>(() => model.AddDemoAccount("Fraction yen démo", yen, null, 42.1m, null));
    }

    [TestMethod]
    public void ExistingFourLedgersAndFixtureRemainUnchangedAndAddedAccountsAreNotPersisted()
    {
        var portfolio = DemoData.Create();
        Assert.AreEqual(4, portfolio.Accounts.Count);
        Assert.AreEqual(3212, portfolio.Transactions.Count);
        var model = new MainViewModel(portfolio);
        var originalAccounts = model.Accounts.ToArray();
        var originalRows = new List<IReadOnlyList<LedgerRow>>();
        foreach (var summary in originalAccounts)
        {
            model.SelectedAccount = summary;
            originalRows.Add(model.Rows);
        }
        CollectionAssert.AreEqual(new[] { 3200, 4, 4, 4 }, originalRows.Select(rows => rows.Count).ToArray());
        model.AddDemoAccount("Seulement en mémoire", model.CurrencyRegistry.Get("EUR"), null, 100m, null);
        for (var index = 0; index < originalAccounts.Length; index++)
        {
            model.SelectedAccount = originalAccounts[index];
            Assert.AreSame(originalRows[index], model.Rows);
            var expected = originalAccounts[index].Account.OpeningBalance;
            foreach (var row in model.Rows)
            {
                expected = row.Transaction.Direction == TransactionDirection.Credit
                    ? checked(expected + row.Transaction.Amount)
                    : checked(expected - row.Transaction.Amount);
                Assert.AreEqual(expected, row.Balance);
            }
            Assert.AreEqual(expected, originalAccounts[index].Balance);
        }
        Assert.AreEqual(4, portfolio.Accounts.Count);
        Assert.AreEqual(3212, portfolio.Transactions.Count);
        Assert.AreEqual(4, new MainViewModel(DemoData.Create()).Accounts.Count);
    }

    [TestMethod]
    public void InvalidCurrencyMetadataCannotEnterTheRegistry()
    {
        var registry = new CurrencyRegistry();
        Assert.ThrowsException<ArgumentException>(() => registry.Register(new Currency("usd", "Dollar", 2)));
        Assert.ThrowsException<ArgumentException>(() => registry.Register(new Currency("US", "Dollar", 2)));
        Assert.ThrowsException<ArgumentException>(() => registry.Register(new Currency("USD", " ", 2)));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => registry.Register(new Currency("USD", "Dollar", -1)));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => registry.Register(new Currency("USD", "Dollar", 29)));
        Assert.AreEqual(3, registry.Currencies.Count);
    }
}
