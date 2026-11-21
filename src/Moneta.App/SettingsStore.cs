using System.IO;
using System.Text.Json;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

/// <summary>Local presentation preferences only; never contains accounts, balances or rates.</summary>
public sealed record LocalSettings
{
    public string Theme { get; init; } = "Light";
    public bool CheckUpdatesAtStartup { get; init; } = true;
    public bool RefreshRatesAtStartup { get; init; }
    public string RateSource { get; init; } = "NBRB";
    public BackupPolicy Backups { get; init; } = new();
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
                $"Impossible de charger les préférences : {ex.Message}\nFichier : {FilePath}\n"
                + "Les valeurs par défaut sont utilisées pour cette session. Aucun fichier existant n'a été remplacé. "
                + "Corrigez ou déplacez le fichier, puis relancez Moneta avant d'enregistrer des préférences.", ex);
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
                "Enregistrement bloqué pour préserver le fichier de préférences non chargé.\n" + _loadFailure.Message,
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
                    CheckUpdatesAtStartup = settings.CheckUpdatesAtStartup,
                    RefreshRatesAtStartup = settings.RefreshRatesAtStartup,
                    RateSource = settings.RateSource,
                    Backups = settings.Backups
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
                $"Les préférences n'ont pas été enregistrées : {ex.Message}\nFichier : {FilePath}", ex);
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
            ?? throw new JsonException("Le document est vide.");
        if (document.SchemaVersion is not (1 or 2 or SchemaVersion))
        {
            throw new SettingsException(
                $"La version {document.SchemaVersion} du fichier de préférences n'est pas prise en charge.");
        }

        var settings = new LocalSettings
        {
            Theme = document.Theme,
            CheckUpdatesAtStartup = document.CheckUpdatesAtStartup,
            RefreshRatesAtStartup = document.RefreshRatesAtStartup,
            RateSource = document.SchemaVersion == 1 ? "NBRB" : document.RateSource
                ?? throw new SettingsException("Le fournisseur des cours est absent des préférences."),
            Backups = document.SchemaVersion < 3 ? new() : document.Backups
                ?? throw new SettingsException("La politique de sauvegarde est absente des préférences.")
        };
        Validate(settings);
        return settings;
    }

    private static void Validate(LocalSettings settings)
    {
        if (settings.Theme is not ("Light" or "Dark"))
        {
            throw new SettingsException("Le thème doit être Light ou Dark.");
        }
        if (settings.RateSource is not ("NBRB" or "CBR"))
            throw new SettingsException("Le fournisseur des cours doit être NBRB ou CBR.");
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
        public required bool CheckUpdatesAtStartup { get; init; }
        public required bool RefreshRatesAtStartup { get; init; }
        public string? RateSource { get; init; }
        public BackupPolicy? Backups { get; init; }
    }
}
