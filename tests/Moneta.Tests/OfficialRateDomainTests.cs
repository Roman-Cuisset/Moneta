using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;

namespace Moneta.Tests;

[TestClass]
public sealed class OfficialRateDomainTests
{
    private static readonly DateOnly Day = new(2026, 4, 3);
    private static readonly DateTimeOffset Retrieved = new(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

    private static Currency Currency(string code, int places = 2) => new(code, code, places);

    private static OfficialRateSet Set(RateProvider provider = RateProvider.Nbrb, params OfficialRateQuote[] quotes) =>
        new(Guid.NewGuid(), provider, provider == RateProvider.Nbrb ? "BYN" : "RUB", Day, Day, Retrieved,
            provider == RateProvider.Nbrb
                ? "https://api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0"
                : "https://www.cbr.ru/scripts/XML_daily.asp?date_req=03%2F04%2F2026",
            new string('A', 64), quotes.Length == 0 ? [new("EUR", 3m, 1), new("RUB", 5m, 100)] : quotes);

    private static void Invalid(OfficialRateSet rates) => Assert.AreEqual(RateFailure.InvalidResponse,
        Assert.ThrowsException<RateException>(() => RateRules.Validate(rates)).Failure);

    private static OfficialRateSet HistoricalReferenceSet(RateProvider provider, DateOnly date) =>
        Set(provider, new OfficialRateQuote("USD", 2m, 1)) with
        {
            RequestedDate = date,
            EffectiveDate = date,
            Endpoint = provider == RateProvider.Nbrb
                ? $"https://api.nbrb.by/exrates/rates?ondate={date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}&periodicity=0"
                : $"https://www.cbr.ru/scripts/XML_daily.asp?date_req={date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}"
        };

    [DataTestMethod]
    [DataRow(RateProvider.Nbrb, 2016, 6, 30)]
    [DataRow(RateProvider.Cbr, 1997, 12, 31)]
    public void LegacyReferenceUnitsCannotBeValuedAsModernCurrency(RateProvider provider, int year, int month, int day)
    {
        var rates = HistoricalReferenceSet(provider, new(year, month, day));
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertAmount(3m, "USD", Currency(rates.ReferenceCurrency), rates)).Failure);
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertTotal([new(3m, "USD")], Currency(rates.ReferenceCurrency), rates)).Failure);
    }

    [DataTestMethod]
    [DataRow(RateProvider.Nbrb, 2016, 7, 1)]
    [DataRow(RateProvider.Cbr, 1998, 1, 1)]
    public void ModernReferenceBoundaryAllowsExactSyntheticValuation(RateProvider provider, int year, int month, int day)
    {
        var rates = HistoricalReferenceSet(provider, new(year, month, day));
        Assert.AreEqual(6m, RateConverter.ConvertAmount(3m, "USD", Currency(rates.ReferenceCurrency), rates));
    }

    [DataTestMethod]
    [DataRow(RateProvider.Nbrb, "EUR", "BYN", "30")]
    [DataRow(RateProvider.Nbrb, "EUR", "RUB", "600")]
    [DataRow(RateProvider.Nbrb, "BYN", "EUR", "3.33")]
    [DataRow(RateProvider.Nbrb, "BYN", "RUB", "200")]
    [DataRow(RateProvider.Nbrb, "RUB", "EUR", "0.17")]
    [DataRow(RateProvider.Nbrb, "RUB", "BYN", "0.50")]
    [DataRow(RateProvider.Cbr, "EUR", "BYN", "30")]
    [DataRow(RateProvider.Cbr, "EUR", "RUB", "900")]
    [DataRow(RateProvider.Cbr, "BYN", "EUR", "3.33")]
    [DataRow(RateProvider.Cbr, "BYN", "RUB", "300")]
    [DataRow(RateProvider.Cbr, "RUB", "EUR", "0.11")]
    [DataRow(RateProvider.Cbr, "RUB", "BYN", "0.33")]
    public void BothProvidersSupportAllSixDirections(RateProvider provider, string source, string target, string expected)
    {
        var rates = provider == RateProvider.Nbrb ? Set() : Set(provider, new("EUR", 90m, 1), new("BYN", 30m, 1));
        var value = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(value, RateConverter.ConvertAmount(10m, source, Currency(target), rates));
        Assert.AreEqual(-value, RateConverter.ConvertAmount(-10m, source, Currency(target), rates));
        Assert.AreEqual(0m, RateConverter.ConvertAmount(0m, source, Currency(target), rates));
    }

    [DataTestMethod]
    [DataRow(1L)]
    [DataRow(10L)]
    [DataRow(100L)]
    [DataRow(1000L)]
    [DataRow(1000000L)]
    [DataRow(1000000000L)]
    public void OfficialNominalScalesAreNotUnitRates(long nominal)
    {
        var rates = Set(RateProvider.Nbrb, new("EUR", 2m, nominal), new("RUB", 4m, nominal));
        Assert.AreEqual(2m, RateConverter.ConvertAmount(nominal, "EUR", Currency("BYN"), rates));
        Assert.AreEqual(nominal / 2m, RateConverter.ConvertAmount(1m, "BYN", Currency("EUR"), rates));
        Assert.AreEqual(0.5m, RateConverter.ConvertAmount(1m, "EUR", Currency("RUB"), rates));
    }

    [TestMethod]
    public void PinnedOfficialCoefficientsKeepAllPublishedDigits()
    {
        var nbrb = Set(RateProvider.Nbrb, new("EUR", 3.3851m, 1), new("RUB", 3.7003m, 100));
        Assert.AreEqual(3.3851m, RateConverter.ConvertAmount(1m, "EUR", Currency("BYN", 4), nbrb));
        Assert.AreEqual(3.7003m, RateConverter.ConvertAmount(100m, "RUB", Currency("BYN", 4), nbrb));
        var cbr = Set(RateProvider.Cbr, new("EUR", 92.7326m, 1), new("BYN", 27.3531m, 1));
        Assert.AreEqual(92.7326m, RateConverter.ConvertAmount(1m, "EUR", Currency("RUB", 4), cbr));
        Assert.AreEqual(27.3531m, RateConverter.ConvertAmount(1m, "BYN", Currency("RUB", 4), cbr));
    }

    [TestMethod]
    public void EveryPrecisionUsesOneSymmetricTiesToEvenRounding()
    {
        var rates = Set(RateProvider.Nbrb, new OfficialRateQuote("EUR", 1m, 2));
        var unit = 1m;
        for (var places = 0; places <= 28; places++)
        {
            var target = Currency("BYN", places);
            Assert.AreEqual(0m, RateConverter.ConvertAmount(unit, "EUR", target, rates));
            Assert.AreEqual(2m * unit, RateConverter.ConvertAmount(3m * unit, "EUR", target, rates));
            Assert.AreEqual(0m, RateConverter.ConvertAmount(-unit, "EUR", target, rates));
            Assert.AreEqual(-2m * unit, RateConverter.ConvertAmount(-3m * unit, "EUR", target, rates));
            if (places < 28) unit /= 10m;
        }
        var thirds = Set(RateProvider.Nbrb, new OfficialRateQuote("EUR", 1m, 3));
        Assert.AreEqual(0.3333333333333333333333333333m,
            RateConverter.ConvertAmount(1m, "EUR", Currency("BYN", 28), thirds));
        Assert.AreEqual(0.6666666666666666666666666667m,
            RateConverter.ConvertAmount(2m, "EUR", Currency("BYN", 28), thirds));
    }

    [TestMethod]
    public void CrossConversionDoesNotRoundAnIntermediateUnitRate()
    {
        var rates = Set(RateProvider.Nbrb, new("EUR", 1m, 3), new("RUB", 1m, 9));
        Assert.AreEqual(3m, RateConverter.ConvertAmount(1m, "EUR", Currency("RUB", 28), rates));
        Assert.AreEqual(1m, RateConverter.ConvertAmount(3m, "RUB", Currency("EUR", 28), rates));
        Assert.AreEqual(0.01m, RateConverter.ConvertAmount(0.03m, "EUR", Currency("BYN"), rates));
    }

    [TestMethod]
    public void ReferenceAndQuotedIdentityNeedNoInventedOfficialReferenceQuote()
    {
        var rates = Set();
        foreach (var code in new[] { "BYN", "EUR", "RUB" })
        {
            Assert.AreEqual(1.24m, RateConverter.ConvertAmount(1.245m, code, Currency(code), rates));
            Assert.AreEqual(-1.26m, RateConverter.ConvertAmount(-1.255m, code, Currency(code), rates));
            Assert.AreEqual(decimal.MaxValue, RateConverter.ConvertAmount(decimal.MaxValue, code, Currency(code, 28), rates));
        }
        Assert.IsFalse(rates.Quotes.Any(quote => quote.CurrencyCode == rates.ReferenceCurrency));
    }

    [TestMethod]
    public void TotalAggregatesExactSignedNativeAmountsAndRoundsOnce()
    {
        var rates = Set(RateProvider.Nbrb, new("EUR", 1m, 3), new("RUB", 1m, 9));
        Assert.AreEqual(1m, RateConverter.ConvertTotal([new(1m, "EUR"), new(1m, "EUR"), new(1m, "EUR")], Currency("BYN"), rates));
        Assert.AreEqual(0.01m, RateConverter.ConvertTotal([new(0.005m, "BYN"), new(0.005m, "BYN")], Currency("BYN"), rates));
        Assert.AreEqual(0m, RateConverter.ConvertTotal([new(1m, "EUR"), new(-3m, "RUB")], Currency("BYN", 28), rates));
        Assert.AreEqual(9m, RateConverter.ConvertTotal([new(1m, "EUR"), new(2m / 3m, "BYN")], Currency("RUB"), rates));
        Assert.AreEqual(0m, RateConverter.ConvertTotal([], Currency("BYN"), rates));
    }

    [TestMethod]
    public void LargeIntermediatesAndCancellationDoNotOverflowWhenFinalResultFits()
    {
        var rates = Set(RateProvider.Nbrb, new("EUR", decimal.MaxValue, 1), new("RUB", decimal.MaxValue, 1));
        Assert.AreEqual(decimal.MaxValue, RateConverter.ConvertAmount(decimal.MaxValue, "EUR", Currency("RUB", 28), rates));
        Assert.AreEqual(-decimal.MaxValue, RateConverter.ConvertAmount(-decimal.MaxValue, "EUR", Currency("RUB", 28), rates));
        Assert.AreEqual(decimal.MaxValue, RateConverter.ConvertTotal(
            [new(decimal.MaxValue, "EUR"), new(decimal.MaxValue, "EUR"), new(-decimal.MaxValue, "EUR")], Currency("RUB", 28), rates));
        Assert.AreEqual(0m, RateConverter.ConvertTotal([new(decimal.MaxValue, "EUR"), new(-decimal.MaxValue, "EUR")], Currency("BYN"), rates));
        Assert.ThrowsException<OverflowException>(() => RateConverter.ConvertAmount(2m, "EUR", Currency("BYN"), rates));
        Assert.ThrowsException<OverflowException>(() => RateConverter.ConvertTotal([new(1m, "EUR"), new(1m, "EUR")], Currency("BYN"), rates));
        var half = Set(RateProvider.Nbrb, new OfficialRateQuote("EUR", 1m, 2));
        Assert.ThrowsException<OverflowException>(() => RateConverter.ConvertAmount(decimal.MaxValue, "EUR", Currency("BYN", 28), half));
        Assert.AreEqual(39614081257132168796771975168m,
            RateConverter.ConvertAmount(decimal.MaxValue, "EUR", Currency("BYN", 0), half));
    }

    [TestMethod]
    public void MissingQuotesRejectZeroIdentityAndPartialTotals()
    {
        var rates = Set();
        foreach (var amount in new[] { 0m, 1m, -1m })
        {
            Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() =>
                RateConverter.ConvertAmount(amount, "USD", Currency("BYN"), rates)).Failure);
            Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() =>
                RateConverter.ConvertAmount(amount, "BYN", Currency("USD"), rates)).Failure);
            Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() =>
                RateConverter.ConvertAmount(amount, "USD", Currency("USD"), rates)).Failure);
        }
        Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertTotal([new(10m, "EUR"), new(0m, "USD")], Currency("BYN"), rates)).Failure);
        Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertTotal([], Currency("USD"), rates)).Failure);
    }

    [DataTestMethod]
    [DataRow("1", "1")]
    [DataRow("1.2300000000000000000000000000000000", "1.23")]
    [DataRow("123e-2", "1.23")]
    [DataRow("1.23E+2", "123")]
    [DataRow("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [DataRow("10e-29", "0.0000000000000000000000000001")]
    [DataRow("1e28", "10000000000000000000000000000")]
    [DataRow("79228162514264337593543950335", "79228162514264337593543950335")]
    [DataRow("792281625142643375935439503350e-1", "79228162514264337593543950335")]
    public void OfficialDecimalParsingIsExactIncludingExponentAndReducibleZeros(string input, string expected) =>
        Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), RateRules.ParseOfficialDecimal(input));

    [DataTestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("0e20")]
    [DataRow("-1")]
    [DataRow("+1")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("01")]
    [DataRow("1,23")]
    [DataRow(".1")]
    [DataRow("1.")]
    [DataRow("1e")]
    [DataRow("1e+")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("1e-29")]
    [DataRow("1e29")]
    [DataRow("79228162514264337593543950336")]
    [DataRow("0.12345678901234567890123456789")]
    [DataRow("1.00000000000000000000000000001")]
    [DataRow("1e999999999999999999999999999999999")]
    [DataRow("1e-99999999999999999999999999999999")]
    [DataRow("1\0")]
    public void InvalidOrInexactOfficialDecimalsNeverSilentlyRound(string input) =>
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => RateRules.ParseOfficialDecimal(input)).Failure);

    [TestMethod]
    public void DecimalInputIsBoundedAndNullIsRejected()
    {
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => RateRules.ParseOfficialDecimal(new string('1', 257))).Failure);
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => RateRules.ParseOfficialDecimal(null!)).Failure);
    }

    [TestMethod]
    public void InvalidPublicationMetadataAndProviderMismatchAreRejected()
    {
        var good = Set();
        foreach (var bad in new[]
        {
            good with { Id = Guid.Empty }, good with { Provider = (RateProvider)99 },
            good with { Provider = RateProvider.Cbr }, good with { ReferenceCurrency = "RUB" },
            good with { RequestedDate = default }, good with { EffectiveDate = default },
            good with { RetrievedUtc = default }, good with { RetrievedUtc = Retrieved.ToOffset(TimeSpan.FromHours(1)) },
            good with { PayloadSha256 = new string('G', 64) }, good with { PayloadSha256 = "AB" },
            good with { PayloadSha256 = null! }, good with { Endpoint = null! },
            good with { Endpoint = "http://api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0" },
            good with { Endpoint = "https://api.nbrb.by.evil.test/exrates/rates?ondate=2026-04-03&periodicity=0" },
            good with { Endpoint = "https://user@api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0" },
            good with { Endpoint = "https://api.nbrb.by:444/exrates/rates?ondate=2026-04-03&periodicity=0" },
            good with { Endpoint = good.Endpoint + "#fragment" }, good with { Endpoint = good.Endpoint + "&extra=1" },
            good with { Endpoint = good.Endpoint.Replace("2026-04-03", "2026-04-04") },
            good with { Endpoint = "https://api.nbrb.by/exrates/rates" },
            good with { Endpoint = new string('x', 2049) }, good with { Endpoint = good.Endpoint + "\0" },
            good with { Quotes = [] }, good with { Quotes = null! }
        }) Invalid(bad);
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => RateRules.ReferenceCurrency((RateProvider)99)).Failure);
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => RateRules.ProviderName((RateProvider)99)).Failure);
        Assert.AreEqual("BYN", RateRules.ReferenceCurrency(RateProvider.Nbrb));
        Assert.AreEqual("RUB", RateRules.ReferenceCurrency(RateProvider.Cbr));
        Assert.AreEqual("NBRB", RateRules.ProviderName(RateProvider.Nbrb));
        Assert.AreEqual("CBR", RateRules.ProviderName(RateProvider.Cbr));
        Assert.AreEqual(31, RateRules.MaximumAgeDays);
    }

    [TestMethod]
    public void EntireBatchIsValidatedIncludingUnusedAndDuplicateQuotes()
    {
        var good = new OfficialRateQuote("EUR", 1m, 1);
        foreach (var quote in new[]
        {
            good with { CurrencyCode = "BYN" }, good with { CurrencyCode = "eur" }, good with { CurrencyCode = "EU" },
            good with { CurrencyCode = "ÉUR" }, good with { CurrencyCode = null! },
            good with { OfficialValue = 0m }, good with { OfficialValue = -1m },
            good with { Nominal = 0 }, good with { Nominal = -1 }, good with { Nominal = 1_000_000_001 },
            good with { SourceId = new string('s', 129) }, good with { SourceId = "a\0b" }, good with { SourceId = null! },
            good with { Name = new string('n', 513) }, good with { Name = "a\0b" }, good with { Name = null! },
            good with { NumericCode = "12" }, good with { NumericCode = "1234" }, good with { NumericCode = "1A3" },
            good with { NumericCode = "١٢٣" }, good with { NumericCode = null! }
        }) Invalid(Set(RateProvider.Nbrb, quote));
        Invalid(Set(RateProvider.Nbrb, good, good));
        Invalid(Set(RateProvider.Nbrb, new OfficialRateQuote[] { null! }));
        Invalid(Set() with { Quotes = Enumerable.Repeat(good, 1001).ToArray() });
        var badUnused = Set(RateProvider.Nbrb, good, new("USD", -1m, 1));
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertAmount(0m, "BYN", Currency("BYN"), badUnused)).Failure);
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() =>
            RateConverter.ConvertTotal([], Currency("BYN"), badUnused)).Failure);
        RateRules.Validate(Set(RateProvider.Nbrb, good with { SourceId = new string('s', 128), Name = new string('n', 512), NumericCode = "978" }));
    }

    [TestMethod]
    public void AdvancePublicationRetainsActualDatesAndDoesNotRewriteRequestedEndpoint()
    {
        var future = Set() with { EffectiveDate = Day.AddDays(1) };
        RateRules.Validate(future);
        Assert.AreEqual(Day, future.RequestedDate);
        Assert.AreEqual(Day.AddDays(1), future.EffectiveDate);
        Invalid(future with { Endpoint = future.Endpoint.Replace("2026-04-03", "2026-04-04") });
        var older = Set() with { EffectiveDate = Day.AddDays(-1) };
        RateRules.Validate(older);
        var cbr = Set(RateProvider.Cbr, new OfficialRateQuote("EUR", 90m, 1)) with { EffectiveDate = Day.AddDays(1) };
        RateRules.Validate(cbr);
    }

    [TestMethod]
    public void ConverterRejectsInvalidCurrencyPrecisionAndCodes()
    {
        foreach (var precision in new[] { -1, 29 })
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => RateConverter.ConvertAmount(1m, "EUR", Currency("BYN", precision), Set()));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => RateConverter.ConvertTotal([], Currency("BYN", precision), Set()));
        }
        foreach (var code in new[] { "eur", "EU", "EURO", "ÉUR", "E\0R", null! })
        {
            Assert.ThrowsException<ArgumentException>(() => RateConverter.ConvertAmount(1m, code, Currency("BYN"), Set()));
            Assert.ThrowsException<ArgumentException>(() => RateConverter.ConvertAmount(1m, "EUR", Currency(code), Set()));
            Assert.ThrowsException<ArgumentException>(() => RateConverter.ConvertTotal([new(0m, code)], Currency("BYN"), Set()));
        }
        Assert.ThrowsException<ArgumentNullException>(() => RateConverter.ConvertAmount(1m, "EUR", null!, Set()));
        Assert.ThrowsException<ArgumentNullException>(() => RateConverter.ConvertTotal(null!, Currency("BYN"), Set()));
        Assert.ThrowsException<ArgumentException>(() => RateConverter.ConvertTotal(new CurrencyAmount[] { null! }, Currency("BYN"), Set()));
    }

    [TestMethod]
    public void ValuationLeavesOriginalFinancialRecordsAndOfficialSetUntouched()
    {
        var euro = Currency("EUR");
        var account = new Account(Guid.NewGuid(), "Compte synthétique", euro, -12.345m);
        var transaction = new Transaction(Guid.NewGuid(), account.Id, Day, "Écriture synthétique", new("Tiers"), new("Catégorie"),
            1.234m, euro, TransactionDirection.Debit, ReconciliationStatus.Unmarked, "Détail");
        var rates = Set();
        var quotes = rates.Quotes.ToArray();
        var originalAccount = account with { };
        var originalTransaction = transaction with { };
        RateConverter.ConvertAmount(account.OpeningBalance, account.Currency.Code, Currency("BYN"), rates);
        RateConverter.ConvertTotal([new(account.OpeningBalance, account.Currency.Code), new(-transaction.Amount, transaction.Currency.Code)], Currency("RUB"), rates);
        Assert.AreEqual(originalAccount, account);
        Assert.AreEqual(originalTransaction, transaction);
        Assert.AreSame(euro, account.Currency);
        Assert.IsNull(transaction.Valuation);
        CollectionAssert.AreEqual(quotes, rates.Quotes.ToArray());
    }
}
