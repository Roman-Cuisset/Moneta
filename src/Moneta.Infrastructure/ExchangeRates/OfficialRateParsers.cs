using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Moneta.Core;

namespace Moneta.Infrastructure.ExchangeRates;

public static class OfficialRateParsers
{
    public const int MaximumPayloadBytes = 1024 * 1024;
    public const int MaximumEntries = 1000;

    static OfficialRateParsers() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static RateDownload Parse(RateProvider provider, DateOnly requestedDate,
        DateTimeOffset retrievedUtc, string endpoint, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaximumPayloadBytes)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheRateResponseExceedsTheAuthorizedSize"));
        if (payload.IsEmpty)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheRateResponseIsEmpty"));

        // Own the exact received bytes: callers may reuse their transport buffers.
        var bytes = payload.ToArray();
        try
        {
            var (effectiveDate, quotes) = provider switch
            {
                RateProvider.Nbrb => ParseNbrb(bytes),
                RateProvider.Cbr => ParseCbr(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
            var rates = new OfficialRateSet(Guid.NewGuid(), provider, RateRules.ReferenceCurrency(provider),
                requestedDate, effectiveDate, retrievedUtc, endpoint, Convert.ToHexString(SHA256.HashData(bytes)),
                Array.AsReadOnly(quotes.ToArray()));
            RateRules.Validate(rates);
            return new RateDownload(rates, bytes);
        }
        catch (RateException) { throw; }
        catch (Exception ex) when (ex is JsonException or XmlException or ArgumentException or FormatException or OverflowException or InvalidOperationException)
        {
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheOfficialResponseContainsInvalidRates"), ex);
        }
    }

    private static (DateOnly, List<OfficialRateQuote>) ParseNbrb(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheNbrbTableIsInvalid"));
        if (root.GetArrayLength() > MaximumEntries)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheNbrbTableContainsTooManyCurrencies"));
        var quotes = new List<OfficialRateQuote>(root.GetArrayLength());
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        DateOnly? effectiveDate = null;
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw Invalid(L.GetCanonical("OfficialRateParsers.ANbrbLineIsInvalid"));
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in row.EnumerateObject())
                if (!properties.Add(property.Name))
                    throw Invalid(L.GetCanonical("OfficialRateParsers.ANbrbPropertyIsRepeated"));
            var dateText = RequiredString(row, "Date");
            if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var timestamp) || timestamp.TimeOfDay != TimeSpan.Zero)
                throw Invalid(L.GetCanonical("OfficialRateParsers.TheNbrbDateIsInvalid"));
            var date = DateOnly.FromDateTime(timestamp);
            if (effectiveDate.HasValue && date != effectiveDate.Value)
                throw Invalid(L.GetCanonical("OfficialRateParsers.TheDatesInTableNbrbDoNotMatch"));
            effectiveDate = date;
            var id = RequiredInteger(row, "Cur_ID");
            if (id <= 0)
                throw Invalid(L.GetCanonical("OfficialRateParsers.NbrbIdIsInvalid"));
            var sourceId = id.ToString(CultureInfo.InvariantCulture);
            if (!sourceIds.Add(sourceId))
                throw Invalid(L.GetCanonical("OfficialRateParsers.ANbrbIdentifierIsRepeated"));
            var nominal = RequiredInteger(row, "Cur_Scale");
            if (!row.TryGetProperty("Cur_OfficialRate", out var value) || value.ValueKind != JsonValueKind.Number)
                throw Invalid(L.GetCanonical("OfficialRateParsers.TheNbrbRateIsInvalid"));
            var officialValue = RateRules.ParseOfficialDecimal(value.GetRawText());
            var numericCode = row.TryGetProperty("Cur_Code", out _) ? RequiredString(row, "Cur_Code") : "";
            var name = row.TryGetProperty("Cur_Name", out _) ? RequiredString(row, "Cur_Name") : "";
            quotes.Add(new OfficialRateQuote(RequiredString(row, "Cur_Abbreviation"), officialValue,
                nominal, sourceId, numericCode, name));
        }
        if (!effectiveDate.HasValue)
            throw new RateException(RateFailure.Unavailable, L.GetCanonical("OfficialRateParsers.NoNbrbRatesHaveBeenPublishedForThis"));
        return (effectiveDate.Value, quotes);
    }

    private static string RequiredString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw Invalid(L.FormatCanonical("OfficialRateParsers.TheNbrb0FieldIsInvalid", name));
        return value.GetString()!;
    }

    private static long RequiredInteger(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
            throw Invalid(L.FormatCanonical("OfficialRateParsers.TheEntireNbrb0FieldIsInvalid", name));
        return integer;
    }

    private static (DateOnly, List<OfficialRateQuote>) ParseCbr(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumPayloadBytes,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        });
        if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "ValCurs" || reader.NamespaceURI.Length != 0)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheCbrRootIsInvalid"));
        if (!DateOnly.TryParseExact(reader.GetAttribute("Date"), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheCbrDateIsInvalid"));
        var quotes = new List<OfficialRateQuote>();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            reader.ReadStartElement("ValCurs");
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.Name != "Valute" || reader.NamespaceURI.Length != 0 || reader.Depth != 1 || reader.IsEmptyElement)
                    throw Invalid(L.GetCanonical("OfficialRateParsers.ACbrLineIsInvalid"));
                if (quotes.Count == MaximumEntries)
                    throw Invalid(L.GetCanonical("OfficialRateParsers.TableCbrContainsTooManyCurrencies"));
                var sourceId = reader.GetAttribute("ID");
                if (string.IsNullOrEmpty(sourceId) || !sourceIds.Add(sourceId))
                    throw Invalid(L.GetCanonical("OfficialRateParsers.ACbrIdentifierIsMissingOrRepeated"));
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                reader.ReadStartElement("Valute");
                while (reader.MoveToContent() == XmlNodeType.Element)
                {
                    var name = reader.Name;
                    if (reader.Depth != 2 || reader.NamespaceURI.Length != 0 ||
                        name is not ("NumCode" or "CharCode" or "Nominal" or "Name" or "Value" or "VunitRate") || fields.ContainsKey(name))
                        throw Invalid(L.GetCanonical("OfficialRateParsers.ACbrFieldIsInvalidOrRepeated"));
                    // ElementContent rejects nested markup, bounding XML depth to the schema.
                    fields.Add(name, reader.ReadElementContentAsString());
                }
                reader.ReadEndElement();
                var nominalText = RequiredField(fields, "Nominal");
                if (!AsciiDigits(nominalText) || !long.TryParse(nominalText, NumberStyles.None, CultureInfo.InvariantCulture, out var nominal))
                    throw Invalid(L.GetCanonical("OfficialRateParsers.NominalCbrIsInvalid"));
                var valueText = RequiredField(fields, "Value");
                var comma = valueText.IndexOf(',');
                if (comma <= 0 || comma == valueText.Length - 1 ||
                    !AsciiDigits(valueText.AsSpan(0, comma)) || !AsciiDigits(valueText.AsSpan(comma + 1)))
                    throw Invalid(L.GetCanonical("OfficialRateParsers.TheDecimalNumberCbrIsInvalid"));
                quotes.Add(new OfficialRateQuote(RequiredField(fields, "CharCode"),
                    RateRules.ParseOfficialDecimal(valueText.Replace(',', '.')), nominal, sourceId,
                    RequiredField(fields, "NumCode"), RequiredField(fields, "Name")));
            }
            reader.ReadEndElement();
        }
        if (reader.MoveToContent() != XmlNodeType.None)
            throw Invalid(L.GetCanonical("OfficialRateParsers.TheCbrDocumentContainsAdditionalData"));
        if (quotes.Count == 0)
            throw new RateException(RateFailure.Unavailable, L.GetCanonical("OfficialRateParsers.NoCbrRatesPublishedForThisApplication"));
        return (date, quotes);
    }

    private static string RequiredField(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : throw Invalid(L.FormatCanonical("OfficialRateParsers.FieldCbr0IsMissing", name));

    private static bool AsciiDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return false;
        foreach (var c in text)
            if (c is < '0' or > '9') return false;
        return true;
    }

    private static RateException Invalid(string message, Exception? inner = null) =>
        new(RateFailure.InvalidResponse, message, inner);
}
