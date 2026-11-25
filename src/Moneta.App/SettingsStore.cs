using System.IO;
using System.Text.Json;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;
using Moneta.Localization;

namespace Moneta.App;

/// <summary>Local presentation preferences only; never contains accounts, balances or rates.</summary>
public sealed record LocalSettings
{
    public string Theme { get; init; } = "Light";
    public string Language { get; init; } = L.French;
    public bool CompactRegister { get; init; }
    public bool CheckUpdatesAtStartup { get; init; } = true;
    public bool RefreshRatesAtStartup { get; init; }
    public string RateSource { get; init; } = "NBRB";
    public BackupPolicy Backups { get; init; } = new();
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public bool WindowMaximized { get; init; }
}

public sealed class SettingsException : Exception
{
    public SettingsException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Versioned, atomic storage independent of WPF and of financial data.</summary>
public sealed class SettingsStore
{
    private const int SchemaVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _loaded;
    private SettingsException? _loadFailure;

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Moneta", "settings.json");
    }

    public string FilePath { get; }
    public LocalSettings Current { get; private set; } = new();
    public string? LoadError => _loadFailure?.Message;

    public void Load()
    {
        _loaded = true;
        _loadFailure = null;
        Current = new LocalSettings();
        try
        {
            Current = ReadSettings(FilePath);
        }
        catch (FileNotFoundException)
        {
            // First launch: defaults remain in memory until an explicit save.
        }
        catch (DirectoryNotFoundException)
        {
            // The local preferences directory is created only when saving.
        }
        catch (Exception ex) when (ex is JsonException or SettingsException or IOException or UnauthorizedAccessException)
        {
            _loadFailure = new SettingsException(
                L.Format("SettingsStore.CouldNotLoadPreferences0File1", (ex.Message), (FilePath))
                + L.Get("SettingsStore.DefaultValuesAreUsedForThisSessionNo")
                + L.Get("SettingsStore.CorrectOrMoveTheFileThenRestartMoneta"), ex);
            throw _loadFailure;
        }
    }

    public void Save(LocalSettings settings)
    {
        if (!_loaded)
        {
            Load();
        }

        if (_loadFailure is not null)
        {
            throw new SettingsException(
                L.Get("SettingsStore.SaveBlockedToPreserveTheUnloadedPreferenceFile") + _loadFailure.Message,
                _loadFailure);
        }

        Validate(settings);
        string? temporaryPath = null;
        try
        {
            var fullPath = Path.GetFullPath(FilePath);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, new SettingsDocument
                {
                    SchemaVersion = SchemaVersion,
                    Theme = settings.Theme,
                    Language = settings.Language,
                    CompactRegister = settings.CompactRegister,
                    CheckUpdatesAtStartup = settings.CheckUpdatesAtStartup,
                    RefreshRatesAtStartup = settings.RefreshRatesAtStartup,
                    RateSource = settings.RateSource,
                    Backups = settings.Backups,
                    WindowLeft = settings.WindowLeft,
                    WindowTop = settings.WindowTop,
                    WindowWidth = settings.WindowWidth,
                    WindowHeight = settings.WindowHeight,
                    WindowMaximized = settings.WindowMaximized
                }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                // A file edited since startup must not be silently replaced if it is invalid.
                _ = ReadSettings(fullPath);
                // Same-directory atomic rename; no unnecessary ACL/metadata merge by ReplaceFile.
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }

            temporaryPath = null;
            Current = settings;
        }
        catch (Exception ex) when (ex is JsonException or SettingsException or IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            throw new SettingsException(
                L.Format("SettingsStore.PreferencesWereNotSaved0File1", (ex.Message), (FilePath)), ex);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original save error; an uncommitted temporary file is not loaded.
                }
            }
        }
    }

    private static LocalSettings ReadSettings(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<SettingsDocument>(stream, JsonOptions)
            ?? throw new JsonException(L.Get("SettingsStore.TheDocumentIsEmpty"));
        if (document.SchemaVersion is not (1 or 2 or SchemaVersion))
        {
            throw new SettingsException(
                L.Format("SettingsStore.The0VersionOfThePreferencesFileIs", (document.SchemaVersion)));
        }

        var hasWindow = TryReadWindow(document, out var left, out var top, out var width, out var height);
        var settings = new LocalSettings
        {
            Theme = document.Theme,
            Language = L.ResolveLanguage(document.Language),
            CompactRegister = document.CompactRegister,
            CheckUpdatesAtStartup = document.CheckUpdatesAtStartup,
            RefreshRatesAtStartup = document.RefreshRatesAtStartup,
            RateSource = document.SchemaVersion == 1 ? "NBRB" : document.RateSource
                ?? throw new SettingsException(L.Get("SettingsStore.TheRateProviderIsAbsentFromPreferences")),
            Backups = document.SchemaVersion < 3 ? new() : document.Backups
                ?? throw new SettingsException(L.Get("SettingsStore.TheSafeguardPolicyIsAbsentFromThePreferences")),
            WindowLeft = hasWindow ? left : null,
            WindowTop = hasWindow ? top : null,
            WindowWidth = hasWindow ? width : null,
            WindowHeight = hasWindow ? height : null,
            WindowMaximized = hasWindow && document.WindowMaximized
        };
        Validate(settings);
        return settings;
    }

    private static bool TryReadWindow(SettingsDocument document, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;
        if (document.WindowLeft is not { } candidateLeft || document.WindowTop is not { } candidateTop
            || document.WindowWidth is not { } candidateWidth || document.WindowHeight is not { } candidateHeight)
            return false;
        if (!WindowPlacement.IsUsable(candidateLeft, candidateTop, candidateWidth, candidateHeight))
            return false;
        left = candidateLeft;
        top = candidateTop;
        width = candidateWidth;
        height = candidateHeight;
        return true;
    }

    private static void Validate(LocalSettings settings)
    {
        if (settings.Theme is not ("Light" or "Dark"))
        {
            throw new SettingsException(L.Get("SettingsStore.TheThemeMustBeLightOrDark"));
        }
        if (settings.RateSource is not ("NBRB" or "CBR"))
            throw new SettingsException(L.Get("SettingsStore.TheRateProviderMustBeNbrbOrCbr"));
        if (settings.Language is not (L.French or L.English))
            throw new SettingsException(L.Get("Settings.UnsupportedLanguage"));
        try { settings.Backups.Validate(); }
        catch (ArgumentException error) { throw new SettingsException(error.Message, error); }
    }

    private sealed class SettingsDocument
    {
        public SettingsDocument()
        {
        }

        public required int SchemaVersion { get; init; }
        public required string Theme { get; init; }
        public string? Language { get; init; }
        public bool CompactRegister { get; init; }
        public required bool CheckUpdatesAtStartup { get; init; }
        public required bool RefreshRatesAtStartup { get; init; }
        public string? RateSource { get; init; }
        public BackupPolicy? Backups { get; init; }
        public double? WindowLeft { get; init; }
        public double? WindowTop { get; init; }
        public double? WindowWidth { get; init; }
        public double? WindowHeight { get; init; }
        public bool WindowMaximized { get; init; }
    }
}
