using System.Globalization;
using System.Numerics;

namespace Moneta.Core;

public static class RateRules
{
    public const int MaximumAgeDays = 31;
    private static readonly BigInteger MaximumCoefficient = (BigInteger.One << 96) - 1;
    private static readonly DateOnly BynSince = new(2016, 7, 1);
    private static readonly DateOnly RubSince = new(1998, 1, 1);

    public static string ReferenceCurrency(RateProvider provider) => provider switch
    {
        RateProvider.Nbrb => "BYN",
        RateProvider.Cbr => "RUB",
        _ => throw Invalid("La source de cours est inconnue.")
    };

    public static string ProviderName(RateProvider provider) => provider switch
    {
        RateProvider.Nbrb => "NBRB",
        RateProvider.Cbr => "CBR",
        _ => throw Invalid("La source de cours est inconnue.")
    };

    public static void Validate(OfficialRateSet rates)
    {
        if (rates is null) throw Invalid("La publication de cours est absente.");
        var reference = ReferenceCurrency(rates.Provider);
        if (rates.Id == Guid.Empty || rates.ReferenceCurrency != reference ||
            rates.RequestedDate == default || rates.EffectiveDate == default ||
            rates.RetrievedUtc == default || rates.RetrievedUtc.Offset != TimeSpan.Zero)
            throw Invalid("Les métadonnées de la publication sont invalides.");
        var referenceSince = rates.Provider == RateProvider.Nbrb ? BynSince : RubSince;
        if (rates.EffectiveDate < referenceSince)
            throw Invalid($"La publication précède la monnaie de référence {reference} actuelle " +
                $"({referenceSince.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}). Aucune redénomination automatique n’est appliquée.");
        if (!ValidText(rates.PayloadSha256, 64) || rates.PayloadSha256.Length != 64)
            throw Invalid("L’empreinte de la publication est invalide.");
        foreach (var character in rates.PayloadSha256)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))
                throw Invalid("L’empreinte de la publication est invalide.");
        ValidateEndpoint(rates);
        if (rates.Quotes is null || rates.Quotes.Count is < 1 or > 1000)
            throw Invalid("La publication doit contenir entre 1 et 1000 cours.");
        var codes = new HashSet<string>(rates.Quotes.Count, StringComparer.Ordinal);
        foreach (var quote in rates.Quotes)
        {
            if (quote is null || !IsCurrencyCode(quote.CurrencyCode) || quote.CurrencyCode == reference ||
                !codes.Add(quote.CurrencyCode) || quote.OfficialValue <= 0 || quote.Nominal is < 1 or > 1_000_000_000 ||
                !ValidText(quote.SourceId, 128) || !ValidText(quote.Name, 512) ||
                !ValidNumericCode(quote.NumericCode))
                throw Invalid("Un cours officiel est invalide ou dupliqué.");
        }
    }

    // Decimal.Parse may round a long coefficient. Build the exact coefficient instead.
    public static decimal ParseOfficialDecimal(string invariant)
    {
        if (string.IsNullOrEmpty(invariant) || invariant.Length > 256)
            throw Invalid("Le cours décimal est absent ou trop long.");
        var text = invariant.AsSpan();
        var index = 0;
        var coefficient = BigInteger.Zero;
        if (text[index] == '0') index++;
        else
        {
            if (text[index] is < '1' or > '9') throw Invalid("Le cours décimal est invalide.");
            while (index < text.Length && IsDigit(text[index]))
                coefficient = coefficient * 10 + (text[index++] - '0');
        }
        var fractionalDigits = 0;
        if (index < text.Length && text[index] == '.')
        {
            index++;
            var start = index;
            while (index < text.Length && IsDigit(text[index]))
            {
                coefficient = coefficient * 10 + (text[index++] - '0');
                fractionalDigits++;
            }
            if (index == start) throw Invalid("Le cours décimal est invalide.");
        }
        var exponent = 0;
        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            var negative = index < text.Length && text[index] == '-';
            if (index < text.Length && text[index] is '+' or '-') index++;
            var start = index;
            while (index < text.Length && IsDigit(text[index]))
            {
                exponent = exponent * 10 + (text[index++] - '0');
                if (exponent > 512) throw Invalid("Le cours dépasse la précision décimale exacte.");
            }
            if (index == start) throw Invalid("Le cours décimal est invalide.");
            if (negative) exponent = -exponent;
        }
        if (index != text.Length || coefficient.IsZero) throw Invalid("Le cours décimal doit être strictement positif.");
        var scale = fractionalDigits - exponent;
        while (scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }
        if (scale < 0)
        {
            if (scale < -28) throw Invalid("Le cours dépasse la précision décimale exacte.");
            coefficient *= BigInteger.Pow(10, -scale);
            scale = 0;
        }
        if (scale > 28 || coefficient > MaximumCoefficient)
            throw Invalid("Le cours dépasse la précision décimale exacte.");
        return new decimal((int)(uint)(coefficient & uint.MaxValue),
            (int)(uint)((coefficient >> 32) & uint.MaxValue),
            (int)(uint)(coefficient >> 64), false, (byte)scale);
    }

    internal static bool IsCurrencyCode(string code) => code is { Length: 3 } &&
        code[0] is >= 'A' and <= 'Z' && code[1] is >= 'A' and <= 'Z' && code[2] is >= 'A' and <= 'Z';

    private static bool IsDigit(char value) => value is >= '0' and <= '9';
    private static bool ValidText(string value, int maximum) => value is not null && value.Length <= maximum && !value.Contains('\0');
    private static bool ValidNumericCode(string value) => value is not null &&
        (value.Length == 0 || (value.Length == 3 && IsDigit(value[0]) && IsDigit(value[1]) && IsDigit(value[2])));

    private static void ValidateEndpoint(OfficialRateSet rates)
    {
        if (!ValidText(rates.Endpoint, 2048) || !Uri.TryCreate(rates.Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Invalid("L’adresse de la publication officielle est invalide.");
        var query = Uri.UnescapeDataString(uri.Query);
        var requested = rates.RequestedDate;
        var valid = rates.Provider switch
        {
            RateProvider.Nbrb => uri.Host.Equals("api.nbrb.by", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath == "/exrates/rates" &&
                (query == $"?ondate={requested.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&periodicity=0" ||
                 query == $"?periodicity=0&ondate={requested.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"),
            RateProvider.Cbr => uri.Host.Equals("www.cbr.ru", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath == "/scripts/XML_daily.asp" &&
                query == $"?date_req={requested.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            _ => false
        };
        if (!valid) throw Invalid("L’adresse ne correspond pas à la source et à la date demandées.");
    }

    private static RateException Invalid(string message) => new(RateFailure.InvalidResponse, message);
}
