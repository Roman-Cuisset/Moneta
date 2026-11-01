using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;

namespace Moneta.Tests;

[TestClass]
public sealed class MoneyInputTests
{
    private static readonly Currency Euro = new("EUR", "Euro", 2);

    [TestMethod]
    public void FrenchGroupingAndSignedNativePrecisionAreExact()
    {
        Assert.AreEqual(1234.56m, MoneyInput.ParseFrench(" 1 234,5600000000000000000000000000000000 ", Euro));
        Assert.AreEqual(-1234.56m, MoneyInput.ParseFrench("-1\u202f234,56", Euro, true));
        Assert.AreEqual(1234.56m, MoneyInput.ParseFrench("+1\u00a0234,56", Euro, true));
        Assert.AreEqual(0m, MoneyInput.ParseFrench("-0,0000", Euro, true));
        Assert.AreEqual(1.001m, MoneyInput.ParseFrench("1,001000", new Currency("KWD", "Dinar", 3)));
        Assert.AreEqual(42m, MoneyInput.ParseFrench("42,0000", new Currency("JPY", "Yen", 0)));
    }

    [TestMethod]
    public void BothSignedMinorBoundsAreAcceptedWithoutRounding()
    {
        Assert.AreEqual(92233720368547758.07m, MoneyInput.ParseFrench("92233720368547758,07", Euro, true));
        Assert.AreEqual(-92233720368547758.07m, MoneyInput.ParseFrench("-92233720368547758,07", Euro, true));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("92233720368547758,08", Euro, true));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("-92233720368547758,08", Euro, true));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("-92233720368547758,09", Euro, true));
    }

    [TestMethod]
    public void MalformedAndInexactAmountsAreNeverSilentlyRounded()
    {
        foreach (var text in new[] { "", " ", ",1", "1,", "1.23", "1,2,3", "1e2", "1 23", "1234 567", "1 2345", "1  234", "- 1", "++1", "١", "0,0000000000000000000000000000000000000001" })
            Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench(text, Euro, true), text);
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("-1", Euro));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("+1", Euro));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseFrench("42,1", new Currency("JPY", "Yen", 0)));
    }
}
