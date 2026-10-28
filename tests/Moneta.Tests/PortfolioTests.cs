using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.Tests;

[TestClass]
public sealed class PortfolioTests
{
    private static readonly Currency Eur = new("EUR", "Euro", 2);
    private static readonly Currency Byn = new("BYN", "Rouble biélorusse", 2);
    private static readonly Account A = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Compte A fictif", Eur, 10m);
    private static readonly Account B = new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Compte B fictif", Byn, 20m);

    private static Transaction Entry(Account account, decimal amount, TransactionDirection direction = TransactionDirection.Debit,
        string label = "Achat fictif", int day = 1) => new(Guid.NewGuid(), account.Id, new DateOnly(2026, 1, day),
            label, new Payee("Tiers fictif"), new Category("Catégorie fictive"), amount, account.Currency,
            direction, ReconciliationStatus.Marked, "Détail fictif uniquement");

    private static MainViewModel Portfolio() => new(new DemoPortfolio([A, B], [Entry(A, 1.25m), Entry(B, 1.25m)]));

    [TestMethod]
    public void AccountSelectionSeparatesIdenticalDatesAndAmountsInDifferentCurrencies()
    {
        var model = Portfolio();
        Assert.AreEqual(A.Id, model.Rows.Single().Transaction.AccountId);
        Assert.AreEqual("8,75 EUR", model.SelectedAccountBalanceText);
        model.SelectedAccount = model.Accounts[1];
        Assert.AreEqual(B.Id, model.Rows.Single().Transaction.AccountId);
        Assert.AreEqual(Byn, model.Rows.Single().Transaction.Currency);
        Assert.AreEqual("18,75 BYN", model.SelectedAccountBalanceText);
        model.SelectedAccount = model.Accounts[0];
        Assert.AreEqual(A.Id, model.Rows.Single().Transaction.AccountId);
        Assert.AreEqual(8.75m, model.Rows.Single().Balance);
    }

    [TestMethod]
    public void AccountSwitchClearsSelectedTransactionAndItsDetails()
    {
        var model = Portfolio();
        model.SelectedRow = model.Rows[0];
        StringAssert.Contains(model.SelectedRowDetails, "1,25 EUR");
        model.SelectedAccount = model.Accounts[1];
        Assert.IsNull(model.SelectedRow);
        Assert.AreEqual(string.Empty, model.SelectedRowDetails);
        model.SelectedRow = model.Rows[0];
        StringAssert.Contains(model.SelectedRowDetails, "1,25 BYN");
        Assert.IsFalse(model.SelectedRowDetails.Contains("EUR", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ForeignRowCannotReplaceCurrentSelection()
    {
        var model = Portfolio();
        var foreign = model.Rows[0];
        model.SelectedAccount = model.Accounts[1];
        var current = model.Rows[0];
        model.SelectedRow = current;
        var details = model.SelectedRowDetails;
        Assert.ThrowsException<ArgumentException>(() => model.SelectedRow = foreign);
        Assert.AreSame(current, model.SelectedRow);
        Assert.AreEqual(details, model.SelectedRowDetails);
    }

    [TestMethod]
    public void RunningBalancesAreExactChronologicalAndStableForEqualDates()
    {
        var account = A with { OpeningBalance = -1m };
        var model = new MainViewModel(new DemoPortfolio([account], [
            Entry(account, 100m, TransactionDirection.Credit, "Plus tard", 2),
            Entry(account, 0.10m, label: "Débit même date"),
            Entry(account, 0.20m, TransactionDirection.Credit, "Crédit même date")
        ]));
        CollectionAssert.AreEqual(new[] { "Débit même date", "Crédit même date", "Plus tard" }, model.Rows.Select(r => r.Label).ToArray());
        CollectionAssert.AreEqual(new[] { -1.10m, -0.90m, 99.10m }, model.Rows.Select(r => r.Balance).ToArray());
        Assert.AreEqual(0.10m, model.Rows[0].Debit);
        Assert.IsNull(model.Rows[0].Credit);
        Assert.AreEqual(string.Empty, model.Rows[0].CreditText);
        Assert.AreEqual("0,20 EUR", model.Rows[1].CreditText);
        Assert.AreEqual("99,10 EUR", model.SelectedAccountBalanceText);
    }

    [TestMethod]
    public void EmptyAccountRetainsItsOpeningBalanceWithoutInventedRows()
    {
        var model = new MainViewModel(new DemoPortfolio([A], []));
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual("10,00 EUR", model.SelectedAccountBalanceText);
        Assert.IsNull(model.SelectedRow);
    }

    [TestMethod]
    public void ClearingAccountClearsAllVisibleFinancialState()
    {
        var model = Portfolio();
        model.SelectedRow = model.Rows[0];
        model.SelectedAccount = null;
        Assert.AreEqual(0, model.Rows.Count);
        Assert.AreEqual(string.Empty, model.SelectedAccountBalanceText);
        Assert.AreEqual(string.Empty, model.SelectedRowDetails);
        Assert.IsNull(model.SelectedRow);
    }

    [TestMethod]
    public void MismatchedTransactionCurrencyIsNotReinterpreted()
    {
        var transaction = Entry(A, 1m) with { Currency = Byn };
        Assert.ThrowsException<ArgumentException>(() => new MainViewModel(new DemoPortfolio([A], [transaction])));
    }

    [TestMethod]
    public void OrphanTransactionIsNotSilentlyDiscarded()
    {
        Assert.ThrowsException<ArgumentException>(() => new MainViewModel(new DemoPortfolio([A], [Entry(B, 1m)])));
    }

    [TestMethod]
    public void NegativeMagnitudeCannotReverseTheTransactionDirection()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new MainViewModel(new DemoPortfolio([A], [Entry(A, -1m)])));
    }

    [TestMethod]
    public void OverflowCannotProduceAFictitiousBalance()
    {
        var account = A with { OpeningBalance = decimal.MaxValue };
        Assert.ThrowsException<OverflowException>(() => new MainViewModel(new DemoPortfolio([account], [Entry(account, 1m, TransactionDirection.Credit)])));
    }

    [TestMethod]
    public void OriginalAmountAndOptionalValuationRemainSeparate()
    {
        var transaction = Entry(A, 1.25m) with { Valuation = new MoneyValuation(4.00m, Byn, new DateOnly(2026, 1, 1), "Exemple fictif") };
        var model = new MainViewModel(new DemoPortfolio([A], [transaction]));
        model.SelectedRow = model.Rows[0];
        Assert.AreEqual("8,75 EUR", model.SelectedAccountBalanceText);
        StringAssert.Contains(model.SelectedRowDetails, "1,25 EUR");
        StringAssert.Contains(model.SelectedRowDetails, "4,00 BYN");
        StringAssert.Contains(model.SelectedRowDetails, "Exemple fictif");
    }

    [TestMethod]
    public void FrenchFormattingDoesNotDependOnMachineCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.AreEqual("1\u202f234,56 EUR", MoneyFormatter.Format(1234.56m, Eur));
            Assert.AreEqual("-12,50 BYN", MoneyFormatter.Format(-12.5m, Byn));
            Assert.AreEqual("0,00 RUB", MoneyFormatter.Format(0m, new Currency("RUB", "Rouble russe", 2)));
            Assert.AreEqual("1,234 KWD", MoneyFormatter.Format(1.234m, new Currency("KWD", "Dinar", 3)));
            Assert.AreEqual("42 JPY", MoneyFormatter.Format(42m, new Currency("JPY", "Yen", 0)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
