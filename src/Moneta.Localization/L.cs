using System.ComponentModel;
using System.Collections;
using System.Globalization;
using System.Resources;

namespace Moneta.Localization;

public sealed class L : INotifyPropertyChanged
{
    public const string French = "fr-FR";
    public const string English = "en-US";
    private static readonly ResourceManager Strings = new("Moneta.Localization.Resources.Strings", typeof(L).Assembly);
    private static readonly CultureInfo ReferenceCulture = CultureInfo.GetCultureInfo(French);
    // BankPerfect provenance and historical report details keep their canonical text.
    private static readonly Lazy<Dictionary<string, string>> DiagnosticKeys = new(() =>
        Strings.GetResourceSet(ReferenceCulture, true, true)!
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string key && IsCanonicalDiagnostic(key))
            .GroupBy(entry => (string)entry.Value!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (string)group.First().Key, StringComparer.Ordinal));
    private CultureInfo _culture = CultureInfo.GetCultureInfo(French);

    private L() { }

    public static L Current { get; } = new();
    public static CultureInfo Culture => Volatile.Read(ref Current._culture);
    public static string DatePattern => Culture.Name == English ? "MM/dd/yyyy" : "dd/MM/yyyy";
    public static string MonthPattern => "MM/yyyy";
    public string CultureName => Culture.Name;
    public string this[string key] => Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;

    public static string ResolveLanguage(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : French;

    public static void SetLanguage(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(ResolveLanguage(language));
        if (Culture.Name == culture.Name) return;
        Volatile.Write(ref Current._culture, culture);
        Current.PropertyChanged?.Invoke(Current, new(nameof(CultureName)));
        Current.PropertyChanged?.Invoke(Current, new("Item[]"));
    }

    public static string Get(string key) => Get(key, Culture);

    public static string Get(string key, CultureInfo culture) =>
        Strings.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing Moneta resource: {key}.");

    public static string Format(string key, params object?[] arguments)
    {
        var culture = Culture;
        return string.Format(culture, Get(key, culture), arguments);
    }

    public static string Diagnostic(string canonicalMessage) =>
        Culture.Name != French && DiagnosticKeys.Value.TryGetValue(canonicalMessage, out var key)
            ? Get(key) : canonicalMessage;

    public static string GetCanonical(string key) => Get(key, ReferenceCulture);

    public static string FormatCanonical(string key, params object?[] arguments) =>
        string.Format(ReferenceCulture, GetCanonical(key), arguments);

    private static bool IsCanonicalDiagnostic(string key) =>
        key.Split('.')[0] is "BankPerfectDiagnostics" or "HistoricalRateResolver" or "OfficialRateClient"
            or "OfficialRateParsers" or "SqliteRateCache" or "RateRules" or "RateConverter" or "ExchangeRateSession";

    public static bool TryReadDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), DatePattern, Culture, DateTimeStyles.None, out date);
}
