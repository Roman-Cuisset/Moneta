using System.Globalization;
using System.Numerics;
using Moneta.Core;

namespace Moneta.BankPerfect;

internal static class V5Values
{
    private static readonly DateOnly Epoch = new(1899, 12, 30);
    private static readonly BigInteger DecimalMaxCoefficient = (BigInteger.One << 96) - 1;
    private static readonly CurrencyRegistry Currencies = new();

    internal static Currency? Currency(string? raw) =>
        Currencies.TryGet(raw == "€" ? "EUR" : raw ?? "", out var currency) ? currency : null;

    internal static V5Date Date(string? raw, V5DateKind kind, V5Location source, V5Diagnostics diagnostics)
    {
        if (string.IsNullOrEmpty(raw))
        {
            diagnostics.Add(kind == V5DateKind.Operation ? "missing-operation-date" : "missing-additional-date",
                kind == V5DateKind.Operation ? V5Finding.Unreadable : V5Finding.Uncertain, "Date source absente ou vide.", source);
            return new(kind, raw, null, null, source);
        }
        if (raw.Length <= V5Limits.NumberLength && int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var days))
        {
            var dayNumber = (long)Epoch.DayNumber + days;
            if (dayNumber >= DateOnly.MinValue.DayNumber && dayNumber <= DateOnly.MaxValue.DayNumber)
                return new(kind, raw, days, DateOnly.FromDayNumber((int)dayNumber), source);
            diagnostics.Add("date-range", V5Finding.Unreadable, "Jour Delphi hors du calendrier pris en charge.", source);
            return new(kind, raw, days, null, source);
        }
        diagnostics.Add("invalid-date", V5Finding.Unreadable, "Jour Delphi invalide ou hors plage entière.", source);
        return new(kind, raw, null, null, source);
    }

    internal static V5Amount Amount(string? raw, Currency? currency, V5Location source, V5Diagnostics diagnostics)
    {
        if (!TryExactDecimal(raw, out var amount, out var scale))
        {
            diagnostics.Add("invalid-amount", V5Finding.Unreadable,
                "Montant absent, malformé ou hors de la représentation décimale exacte.", source);
            return new(raw, null, scale, V5AmountCompatibility.Unknown, source);
        }
        if (!IsHistoricalAmount(amount))
            diagnostics.Add("amount-outside-v5-domain", V5Finding.Uncertain,
                "Montant exact hors de la plage historique signée à quatre décimales.", source);

        var compatibility = V5AmountCompatibility.Unknown;
        if (currency is not null)
        {
            try
            {
                _ = ExactMoney.ToMinorUnits(amount, currency);
                compatibility = V5AmountCompatibility.Exact;
            }
            catch (ArgumentOutOfRangeException)
            {
                compatibility = V5AmountCompatibility.OutOfRange;
                diagnostics.Add("amount-target-range", V5Finding.Unrepresentable, "Montant hors des limites des unités mineures Moneta.", source);
            }
            catch (ArgumentException)
            {
                compatibility = V5AmountCompatibility.ExcessPrecision;
                diagnostics.Add("amount-target-precision", V5Finding.Unrepresentable,
                    "Montant non représentable exactement dans les unités mineures de la devise Moneta.", source);
            }
        }
        return new(raw, amount, scale, compatibility, source);
    }

    internal static bool IsHistoricalAmount(decimal amount) =>
        amount >= -922337203685477.5808m && amount <= 922337203685477.5807m &&
        decimal.Round(amount, 4) == amount;

    internal static bool TryExactDecimal(string? raw, out decimal value, out int? scale)
    {
        value = 0;
        scale = null;
        if (raw is null || raw.Length > V5Limits.NumberLength)
            return false;
        var text = raw.AsSpan().Trim();
        var negative = !text.IsEmpty && text[0] == '-';
        if (!text.IsEmpty && text[0] is '-' or '+')
            text = text[1..];
        if (text.IsEmpty || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[^1]))
            return false;

        BigInteger coefficient = 0;
        var separator = -1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '.' or ',' && separator < 0)
                separator = index;
            else if (char.IsAsciiDigit(character))
                coefficient = coefficient * 10 + character - '0';
            else
                return false;
        }
        var effectiveScale = separator < 0 ? 0 : text.Length - separator - 1;
        scale = effectiveScale;
        while (effectiveScale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            effectiveScale--;
        }
        if (effectiveScale > 28 || coefficient > DecimalMaxCoefficient)
            return false;

        var lo = (uint)(coefficient & uint.MaxValue);
        var mid = (uint)((coefficient >> 32) & uint.MaxValue);
        var hi = (uint)(coefficient >> 64);
        value = new decimal((int)lo, (int)mid, (int)hi, negative, (byte)effectiveScale);
        return true;
    }

    internal static bool TryUInt(string? raw, out uint value)
    {
        value = 0;
        return raw is { Length: <= V5Limits.NumberLength } &&
            uint.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryUid(string? raw, out ulong value)
    {
        value = 0;
        return raw is { Length: <= V5Limits.IdentifierLength } &&
            ulong.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
