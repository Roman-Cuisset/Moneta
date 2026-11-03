using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;

namespace Moneta.Tests;

[TestClass]
public sealed class OfficialRateProviderTests
{
    private static readonly DateOnly Date = new(2026, 4, 3);
    private static readonly DateTimeOffset Retrieved = new(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
    private const string NbrbEndpoint = "https://api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0";
    private const string CbrEndpoint = "https://www.cbr.ru/scripts/XML_daily.asp?date_req=03/04/2026";

    [TestMethod]
    public void HistoricalNbrbByrPublicationIsNotRelabeledByn()
    {
        // Public entry extracted from the 2016-06-30 response, not a byte-identical full response.
        var payload = Encoding.UTF8.GetBytes("""
            [{"Cur_ID":19,"Cur_Code":"978","Date":"2016-06-30T00:00:00","Cur_Abbreviation":"EUR","Cur_Scale":1,"Cur_Name":"Евро","Cur_OfficialRate":22210}]
            """);
        var error = Assert.ThrowsException<RateException>(() => OfficialRateParsers.Parse(RateProvider.Nbrb,
            new(2016, 6, 30), Retrieved,
            "https://api.nbrb.by/exrates/rates?ondate=2016-06-30&periodicity=0", payload));
        Assert.AreEqual(RateFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    public void HistoricalCbrOldRublePublicationIsNotRelabeledModernRub()
    {
        // Public USD entry: request 31/12/1997 actually returns effect 30/12/1997 and 5960 old rubles.
        var payload = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="utf-8"?><ValCurs Date="30.12.1997" name="Foreign Currency Market"><Valute ID="R01235"><NumCode>840</NumCode><CharCode>USD</CharCode><Nominal>1</Nominal><Name>Доллар США</Name><Value>5960,0000</Value><VunitRate>5960</VunitRate></Valute></ValCurs>
            """);
        var error = Assert.ThrowsException<RateException>(() => OfficialRateParsers.Parse(RateProvider.Cbr,
            new(1997, 12, 31), Retrieved,
            "https://www.cbr.ru/scripts/XML_daily.asp?date_req=31/12/1997", payload));
        Assert.AreEqual(RateFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    public void AuthenticNbrbBytesPreserveExactRatesNominalsAndProvenance()
    {
        var bytes = OfficialRateFixtures.Nbrb();
        var download = Parse(RateProvider.Nbrb, bytes);
        var rates = download.Rates;
        Assert.AreEqual(OfficialRateFixtures.NbrbSha256, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.AreEqual(OfficialRateFixtures.NbrbSha256, rates.PayloadSha256);
        CollectionAssert.AreEqual(bytes, download.Payload.ToArray());
        Assert.AreEqual(30, rates.Quotes.Count);
        Assert.AreEqual("BYN", rates.ReferenceCurrency);
        Assert.AreEqual(Date, rates.RequestedDate);
        Assert.AreEqual(Date, rates.EffectiveDate);
        Assert.AreEqual(Retrieved, rates.RetrievedUtc);
        Assert.AreEqual(NbrbEndpoint, rates.Endpoint);
        Assert.AreNotEqual(Guid.Empty, rates.Id);
        var eur = rates.Quotes.Single(x => x.CurrencyCode == "EUR");
        Assert.AreEqual(3.3851m, eur.OfficialValue);
        Assert.AreEqual(1L, eur.Nominal);
        Assert.AreEqual("Евро", eur.Name);
        Assert.AreEqual("978", eur.NumericCode);
        var rub = rates.Quotes.Single(x => x.CurrencyCode == "RUB");
        Assert.AreEqual(3.7003m, rub.OfficialValue);
        Assert.AreEqual(100L, rub.Nominal);
        CollectionAssert.AreEquivalent(new long[] { 1, 10, 100, 1000, 100000 }, rates.Quotes.Select(x => x.Nominal).Distinct().ToArray());
        Assert.IsFalse(rates.Quotes.Any(x => x.CurrencyCode == "BYN"));
    }

    [TestMethod]
    public void AuthenticCbrOriginalWindows1251BytesUseValueAndNominalNotVunitRate()
    {
        var bytes = OfficialRateFixtures.Cbr();
        var download = Parse(RateProvider.Cbr, bytes);
        Assert.AreEqual(9511, bytes.Length);
        Assert.AreEqual(OfficialRateFixtures.CbrSha256, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.AreEqual(OfficialRateFixtures.CbrSha256, download.Rates.PayloadSha256);
        CollectionAssert.AreEqual(bytes, download.Payload.ToArray());
        Assert.AreEqual(54, download.Rates.Quotes.Count);
        Assert.AreEqual("RUB", download.Rates.ReferenceCurrency);
        Assert.AreEqual(Date, download.Rates.EffectiveDate);
        var eur = download.Rates.Quotes.Single(x => x.CurrencyCode == "EUR");
        Assert.AreEqual(92.7326m, eur.OfficialValue);
        Assert.AreEqual(1L, eur.Nominal);
        Assert.AreEqual("Евро", eur.Name);
        var byn = download.Rates.Quotes.Single(x => x.CurrencyCode == "BYN");
        Assert.AreEqual(27.3531m, byn.OfficialValue);
        Assert.AreEqual("Белорусский рубль", byn.Name);
        var irr = download.Rates.Quotes.Single(x => x.CurrencyCode == "IRR");
        Assert.AreEqual(58.8139m, irr.OfficialValue);
        Assert.AreEqual(1000000L, irr.Nominal);
        CollectionAssert.AreEquivalent(new long[] { 1, 10, 100, 1000, 10000, 1000000 }, download.Rates.Quotes.Select(x => x.Nominal).Distinct().ToArray());
        Assert.IsFalse(download.Rates.Quotes.Any(x => x.CurrencyCode == "RUB"));
    }

    [TestMethod]
    public void ParsedBatchOwnsPayloadAndReadOnlyQuotes()
    {
        var bytes = OfficialRateFixtures.Nbrb();
        var download = Parse(RateProvider.Nbrb, bytes);
        bytes[0] = 0;
        Assert.AreEqual((byte)'[', download.Payload.Span[0]);
        var list = (IList<OfficialRateQuote>)download.Rates.Quotes;
        Assert.IsTrue(list.IsReadOnly);
        Assert.ThrowsException<NotSupportedException>(() => list[0] = list[1]);
        Assert.AreNotEqual(download.Rates.Id, Parse(RateProvider.Nbrb, OfficialRateFixtures.Nbrb()).Rates.Id);
    }

    [DataTestMethod]
    [DataRow("Cur_Scale\":1,", "Cur_Scale\":0,")]
    [DataRow("Cur_Scale\":1,", "Cur_Scale\":-1,")]
    [DataRow("Cur_Scale\":1,", "Cur_Scale\":1.5,")]
    [DataRow("Cur_Scale\":1,", "Cur_Scale\":1000000001,")]
    [DataRow("Cur_ID\":440", "Cur_ID\":0")]
    [DataRow("Cur_ID\":510", "Cur_ID\":440")]
    [DataRow("Cur_Abbreviation\":\"AMD\"", "Cur_Abbreviation\":\"AUD\"")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":-2.0192")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":0")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":null")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":\"2.0192\"")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":1.00000000000000000000000000001")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":1e-29")]
    [DataRow("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":79228162514264337593543950336")]
    [DataRow("Cur_Code\":\"036\"", "Cur_Code\":\"36\"")]
    [DataRow("Cur_Abbreviation\":\"AUD\"", "Cur_Abbreviation\":\"aud\"")]
    [DataRow("Cur_Abbreviation\":\"AUD\"", "Cur_Abbreviation\":\"BYN\"")]
    [DataRow("Cur_ID\":440", "Cur_ID\":440,\"Cur_ID\":441")]
    public void MutatedNbrbRejectsInvalidNominalsDuplicatesAndInexactDecimals(string before, string after) =>
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes(NbrbText().Replace(before, after, StringComparison.Ordinal)));

    [TestMethod]
    public void NbrbRowDateMismatchAndNonMidnightDateAreRejected()
    {
        var text = NbrbText();
        var index = text.IndexOf("2026-04-03T00:00:00", StringComparison.Ordinal);
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes(text.Remove(index, 19).Insert(index, "2026-04-04T00:00:00")));
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes(text.Replace("T00:00:00", "T00:00:01", StringComparison.Ordinal)));
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes(text.Replace("2026-04-03", "2026-02-30", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExactJsonExponentIsAcceptedWithoutFloatingPoint()
    {
        var bytes = Encoding.UTF8.GetBytes(NbrbText().Replace("Cur_OfficialRate\":2.0192", "Cur_OfficialRate\":20192e-4", StringComparison.Ordinal));
        Assert.AreEqual(2.0192m, Parse(RateProvider.Nbrb, bytes).Rates.Quotes.Single(x => x.CurrencyCode == "AUD").OfficialValue);
    }

    [TestMethod]
    public void OptionalNbrbNumericCodeMayBeAbsent()
    {
        var text = NbrbText().Replace("\"Cur_Code\":\"036\",", "", StringComparison.Ordinal);
        Assert.AreEqual("", Parse(RateProvider.Nbrb, Encoding.UTF8.GetBytes(text)).Rates.Quotes.Single(x => x.CurrencyCode == "AUD").NumericCode);
    }

    [DataTestMethod]
    [DataRow("<Nominal>1</Nominal>", "<Nominal>0</Nominal>")]
    [DataRow("<Nominal>1</Nominal>", "<Nominal>-1</Nominal>")]
    [DataRow("<Nominal>1</Nominal>", "<Nominal>1.5</Nominal>")]
    [DataRow("<Nominal>1</Nominal>", "<Nominal>1000000001</Nominal>")]
    [DataRow("<Value>55,2612</Value>", "<Value>55.2612</Value>")]
    [DataRow("<Value>55,2612</Value>", "<Value>5 5,2612</Value>")]
    [DataRow("<Value>55,2612</Value>", "<Value>55,,2612</Value>")]
    [DataRow("<Value>55,2612</Value>", "<Value>1,00000000000000000000000000001</Value>")]
    [DataRow("<Value>55,2612</Value>", "<Value>-55,2612</Value>")]
    [DataRow("<Value>55,2612</Value>", "<Value>0,0000</Value>")]
    [DataRow("<CharCode>AZN</CharCode>", "<CharCode>AUD</CharCode>")]
    [DataRow("ID=\"R01020A\"", "ID=\"R01010\"")]
    [DataRow("<Value>55,2612</Value>", "<Value>55,2612</Value><Value>55,2612</Value>")]
    [DataRow("Date=\"03.04.2026\"", "Date=\"30.02.2026\"")]
    [DataRow("<Nominal>1</Nominal>", "<Nominal><Nested>1</Nested></Nominal>")]
    public void MutatedCbrRejectsMalformedDecimalsNominalsDatesAndDuplicates(string before, string after) =>
        Invalid(RateProvider.Cbr, CbrBytes(CbrText().Replace(before, after, StringComparison.Ordinal)));

    [TestMethod]
    public void CbrApproximateUnitRateIsNeverUsed()
    {
        var bytes = CbrBytes(CbrText().Replace("<VunitRate>55,2612</VunitRate>", "<VunitRate>0,0000001</VunitRate>", StringComparison.Ordinal));
        Assert.AreEqual(55.2612m, Parse(RateProvider.Cbr, bytes).Rates.Quotes.Single(x => x.CurrencyCode == "AUD").OfficialValue);
    }

    [TestMethod]
    public void CbrActualPublicationDateIsNotRewrittenToRequestedDate()
    {
        var requested = new DateOnly(2026, 4, 5);
        var rates = OfficialRateParsers.Parse(RateProvider.Cbr, requested, Retrieved,
            "https://www.cbr.ru/scripts/XML_daily.asp?date_req=05/04/2026", OfficialRateFixtures.Cbr()).Rates;
        Assert.AreEqual(requested, rates.RequestedDate);
        Assert.AreEqual(Date, rates.EffectiveDate);
    }

    [TestMethod]
    public void ValidAdvancePublicationIsRetainedWithActualDate()
    {
        var nbrb = Parse(RateProvider.Nbrb, Encoding.UTF8.GetBytes(NbrbText().Replace("2026-04-03", "2026-04-04", StringComparison.Ordinal)));
        Assert.AreEqual(new DateOnly(2026, 4, 4), nbrb.Rates.EffectiveDate);
        Assert.AreEqual(Date, nbrb.Rates.RequestedDate);
        var cbr = Parse(RateProvider.Cbr, CbrBytes(CbrText().Replace("Date=\"03.04.2026\"", "Date=\"04.04.2026\"", StringComparison.Ordinal)));
        Assert.AreEqual(new DateOnly(2026, 4, 4), cbr.Rates.EffectiveDate);
    }

    [TestMethod]
    public void MalformedDocumentsEmptyBodiesAndExcessDepthAreRejected()
    {
        Invalid(RateProvider.Nbrb, Array.Empty<byte>());
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes("[{"));
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes("{}"));
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes("[" + new string('[', 10) + "0" + new string(']', 10) + "]"));
        Invalid(RateProvider.Cbr, CbrBytes("<ValCurs Date=\"03.04.2026\"><Valute>"));
        Invalid(RateProvider.Cbr, CbrBytes(CbrText() + "<extra/>"));
        Invalid(RateProvider.Cbr, CbrBytes("<Wrong/>"));
    }

    [TestMethod]
    public void EmptyOfficialTablesAreUnavailableNotValidRates()
    {
        Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() => Parse(RateProvider.Nbrb, Encoding.UTF8.GetBytes("[]"))).Failure);
        Assert.AreEqual(RateFailure.Unavailable, Assert.ThrowsException<RateException>(() => Parse(RateProvider.Cbr, CbrBytes("<ValCurs Date=\"03.04.2026\"/>"))).Failure);
    }

    [DataTestMethod]
    [DataRow("<!DOCTYPE ValCurs [<!ENTITY stolen SYSTEM 'file:///C:/Windows/win.ini'>]>")]
    [DataRow("<!DOCTYPE ValCurs SYSTEM 'https://example.invalid/evil.dtd'>")]
    [DataRow("<!DOCTYPE ValCurs [<!ENTITY a '123'><!ENTITY b '&a;&a;'>]>")]
    public void CbrRejectsAllDtdsWithoutResolvingExternalResources(string dtd)
    {
        var xml = CbrText();
        var declarationEnd = xml.IndexOf("?>", StringComparison.Ordinal) + 2;
        Invalid(RateProvider.Cbr, CbrBytes(xml.Insert(declarationEnd, dtd)));
    }

    [TestMethod]
    public void ParserLimitsPayloadSizeAndEntryCounts()
    {
        Invalid(RateProvider.Nbrb, new byte[OfficialRateParsers.MaximumPayloadBytes + 1]);
        Invalid(RateProvider.Cbr, new byte[OfficialRateParsers.MaximumPayloadBytes + 1]);
        using var document = JsonDocument.Parse(OfficialRateFixtures.Nbrb());
        var row = document.RootElement[0].GetRawText();
        Invalid(RateProvider.Nbrb, Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(row, 1001)) + "]"));
        var xml = CbrText();
        var start = xml.IndexOf("<Valute ", StringComparison.Ordinal);
        var end = xml.IndexOf("</Valute>", StringComparison.Ordinal) + "</Valute>".Length;
        var cbrRow = xml[start..end];
        var rows = Enumerable.Range(0, 1001).Select(i => cbrRow.Replace("R01010", "R" + i, StringComparison.Ordinal));
        Invalid(RateProvider.Cbr, CbrBytes("<ValCurs Date=\"03.04.2026\">" + string.Concat(rows) + "</ValCurs>"));
    }

    [DataTestMethod]
    [DataRow(RateProvider.Nbrb)]
    [DataRow(RateProvider.Cbr)]
    public async Task ClientFetchesOnlyFixedOfficialHttpsGetWithDateParameters(RateProvider provider)
    {
        var handler = new FixtureHandler((request, _) =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            var uri = request.RequestUri ?? throw new AssertFailedException("Missing official request URI.");
            Assert.AreEqual(provider == RateProvider.Nbrb ? NbrbEndpoint : CbrEndpoint, uri.AbsoluteUri);
            Assert.AreEqual("https", uri.Scheme);
            Assert.IsNull(request.Content);
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            return Task.FromResult(Ok(provider == RateProvider.Nbrb ? OfficialRateFixtures.Nbrb() : OfficialRateFixtures.Cbr()));
        });
        using var client = new OfficialRateClient(provider, handler);
        var download = await client.FetchAsync(Date);
        Assert.AreEqual(provider, download.Rates.Provider);
        Assert.AreEqual(Date, download.Rates.EffectiveDate);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(TimeSpan.Zero, download.Rates.RetrievedUtc.Offset);
    }

    [DataTestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    [DataRow(404)]
    [DataRow(429)]
    [DataRow(500)]
    public async Task RedirectsAndErrorStatusesDoNotRetryOrFollowLocation(int status)
    {
        var handler = new FixtureHandler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Location = new Uri("https://example.invalid/never-contact");
            return Task.FromResult(response);
        });
        using var client = new OfficialRateClient(RateProvider.Nbrb, handler);
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.Http, error.Failure);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task HttpSuccessStillUsesRealParserAndRejectsMalformedBody()
    {
        using var client = new OfficialRateClient(RateProvider.Nbrb,
            new FixtureHandler((_, _) => Task.FromResult(Ok(Encoding.UTF8.GetBytes("{malformed")))));
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.InvalidResponse, error.Failure);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task HttpBodyIsBoundedEvenWithoutContentLength(bool declaredLength)
    {
        var payload = new byte[OfficialRateParsers.MaximumPayloadBytes + 1];
        var handler = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = declaredLength ? new ByteArrayContent(payload) : new StreamContent(new NonSeekableStream(payload))
        }));
        using var client = new OfficialRateClient(RateProvider.Nbrb, handler);
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    public async Task OwnDeadlineIsTypedTimeout()
    {
        var handler = new FixtureHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable after cancellation.");
        });
        using var client = new OfficialRateClient(RateProvider.Nbrb, handler, TimeSpan.FromMilliseconds(30));
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.Timeout, error.Failure);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task CallerCancellationRemainsOperationCanceledWithCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FixtureHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable after cancellation.");
        });
        using var client = new OfficialRateClient(RateProvider.Nbrb, handler);
        var error = await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.FetchAsync(Date, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
    }

    [TestMethod]
    public async Task DeadlineAlsoBoundsBodyReadsAfterSuccessfulHeaders()
    {
        var handler = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream())
        }));
        using var client = new OfficialRateClient(RateProvider.Cbr, handler, TimeSpan.FromMilliseconds(30));
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.Timeout, error.Failure);
    }

    [TestMethod]
    public async Task CallerCanCancelBodyReadAfterSuccessfulHeaders()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream(cancellation.Cancel))
        }));
        using var client = new OfficialRateClient(RateProvider.Cbr, handler);
        var error = await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.FetchAsync(Date, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
    }

    [TestMethod]
    public async Task PreCanceledRequestDoesNotContactHandler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("Must not send."));
        using var client = new OfficialRateClient(RateProvider.Cbr, handler);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.FetchAsync(Date, cancellation.Token));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task TransportFailureIsTypedNetworkErrorWithoutFallback()
    {
        var handler = new FixtureHandler((_, _) => throw new HttpRequestException("Simulated disconnected transport."));
        using var client = new OfficialRateClient(RateProvider.Cbr, handler);
        var error = await Assert.ThrowsExceptionAsync<RateException>(() => client.FetchAsync(Date));
        Assert.AreEqual(RateFailure.Network, error.Failure);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public void ClientRejectsInvalidTimeoutAndProvider()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new OfficialRateClient(RateProvider.Nbrb, timeout: TimeSpan.Zero));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new OfficialRateClient(RateProvider.Nbrb, timeout: Timeout.InfiniteTimeSpan));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new OfficialRateClient((RateProvider)99));
    }

    private static RateDownload Parse(RateProvider provider, byte[] payload) => OfficialRateParsers.Parse(provider,
        Date, Retrieved, provider == RateProvider.Nbrb ? NbrbEndpoint : CbrEndpoint, payload);

    private static void Invalid(RateProvider provider, byte[] payload) =>
        Assert.AreEqual(RateFailure.InvalidResponse, Assert.ThrowsException<RateException>(() => Parse(provider, payload)).Failure);

    private static string NbrbText() => Encoding.UTF8.GetString(OfficialRateFixtures.Nbrb());

    private static Encoding CbrEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }

    private static string CbrText() => CbrEncoding().GetString(OfficialRateFixtures.Cbr());
    private static byte[] CbrBytes(string text) => CbrEncoding().GetBytes(text);
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class BlockingReadStream(Action? onRead = null) : MemoryStream
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
    }
}
