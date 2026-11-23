using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed record BackupPolicy
{
    public bool Enabled { get; init; }
    public bool AfterSave { get; init; } = true;
    public bool Daily { get; init; } = true;
    public int IntervalMinutes { get; init; } = 30;
    public int VersionsToKeep { get; init; } = 10;
    public string? DirectoryPath { get; init; }

    [JsonIgnore]
    public string Destination => DirectoryPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moneta", "Sauvegardes");

    public void Validate()
    {
        if (IntervalMinutes is < 1 or > 1440 || VersionsToKeep is < 1 or > 100)
            throw new ArgumentException(L.Get("BackupLibrary.ChooseAnIntervalOf1To1440"));
        if (Enabled && !AfterSave && !Daily)
            throw new ArgumentException(L.Get("BackupLibrary.ChooseAtLeastOneAutomaticTrigger"));
        if (DirectoryPath is not null && (!Path.IsPathFullyQualified(DirectoryPath) || DirectoryPath.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException(L.Get("BackupLibrary.ChooseALocalDirectoryWithAnAbsolutePath"));
    }
}

public enum BackupKind { AfterSave, Daily, Manual }

public sealed record BackupVersion(
    [property: JsonRequired] int FormatVersion, [property: JsonRequired] Guid SnapshotId,
    [property: JsonRequired] Guid DocumentId, [property: JsonRequired] DateTimeOffset CreatedUtc,
    [property: JsonRequired] BackupKind Kind, [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] long Length, [property: JsonRequired] string Sha256);

public sealed record BackupHistoryEntry(Guid SnapshotId, BackupVersion? Version, string DirectoryPath, string Status);
public sealed record LibraryBackupResult(BackupVersion? Published, BackupVersion? Latest, string? RotationWarning);

/// <summary>Local generations of the certified SQLite backup format.</summary>
public sealed class BackupLibrary(TimeProvider? clock = null)
{
    private const int Format = 1;
    private const string LibraryName = "versions-moneta";
    private const string ArchiveName = "dossier.moneta";
    private const string ManifestName = ArchiveName + ".manifest.json";
    private const string VersionName = "version.json";
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly string[] BundleFiles = [ArchiveName, ManifestName, VersionName];
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public LibraryBackupResult Run(BackupPolicy policy, BackupKind kind, string source, FinancialDocument committed,
        CancellationToken cancellation = default, Action<BackupPhase>? phase = null)
    {
        policy.Validate();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != BackupKind.Manual && (!policy.Enabled || (kind == BackupKind.AfterSave ? !policy.AfterSave : !policy.Daily)))
            return new(null, null, null);
        cancellation.ThrowIfCancellationRequested();
        var folder = Folder(policy, committed.Id);
        GuardOutsideLibrary(policy, source);
        Directory.CreateDirectory(folder);
        using var gate = Enter(folder);
        var history = Enumerate(folder, committed.Id);
        var now = time.GetUtcNow();
        // Future timestamps are ignored for scheduling after a backward clock change.
        var latest = history.FirstOrDefault(entry => entry.Version?.CreatedUtc <= now);
        if (latest?.Version is { } prior && !IsDue(policy, kind, now, prior))
        {
            try
            {
                MonetaBackup.VerifyCertifiedBytes(Path.Combine(latest.DirectoryPath, ArchiveName), prior);
                return new(null, prior, null);
            }
            catch (Exception error) when (IsLibraryError(error))
            {
                // A damaged or inaccessible version must not suppress a new attempt.
            }
        }

        var id = Guid.NewGuid();
        var staging = Path.Combine(folder, ".pending-" + id.ToString("N"));
        var published = Path.Combine(folder, PublishedName(id));
        if (Directory.Exists(staging) || File.Exists(staging) || Directory.Exists(published) || File.Exists(published))
            throw new DocumentException(DocumentError.DestinationExists, L.Get("BackupLibrary.AVersionWithThisIdentifierAlreadyExists"));
        Directory.CreateDirectory(staging);
        if (Directory.EnumerateFileSystemEntries(staging).Any())
            throw new DocumentException(DocumentError.DestinationExists, L.Get("BackupLibrary.TheWorkingDirectoryAlreadyContainsFiles"));
        var ownsStaging = false;
        var ownsArchive = false;
        try
        {
            using (var reserve = new FileStream(Path.Combine(staging, VersionName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                ownsStaging = true;
            var archive = Path.Combine(staging, ArchiveName);
            var result = MonetaBackup.CreateFromCommitted(source, committed, archive, cancellation, phase);
            ownsArchive = true;
            var version = new BackupVersion(Format, id, committed.Id, time.GetUtcNow(), kind,
                committed.SchemaVersion, new FileInfo(archive).Length, result.Sha256);
            using (var marker = new FileStream(Path.Combine(staging, VersionName), FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(marker, version, Json);
                marker.Flush(flushToDisk: true);
            }
            phase?.Invoke(BackupPhase.Publication);
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(staging, published);
            ownsStaging = false;

            string? warning = null;
            if (history.Count(entry => entry.Version is not null) >= policy.VersionsToKeep)
            {
                try
                {
                    phase?.Invoke(BackupPhase.Rotation);
                    warning = Rotate(folder, committed.Id, published, version, history, policy.VersionsToKeep, cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    warning = L.Get("BackupLibrary.RotationInterruptedAfterPublication");
                }
                catch (Exception error) when (IsLibraryError(error))
                {
                    warning = L.Get("BackupLibrary.TheBackupIsPublishedRotationHasBeenDelayed") + error.Message;
                }
            }
            return new(version, version, warning);
        }
        finally
        {
            if (ownsStaging) CleanOwnedBundle(staging, ownsArchive ? BundleFiles : [VersionName]);
        }
    }

    public IReadOnlyList<BackupHistoryEntry> History(BackupPolicy policy, Guid documentId) =>
        Enumerate(Folder(policy, documentId), documentId);

    public BackupInspection Verify(BackupPolicy policy, Guid documentId, Guid snapshotId)
    {
        var folder = Folder(policy, documentId);
        using var gate = Enter(folder);
        var path = Path.Combine(folder, PublishedName(snapshotId));
        return VerifyVersion(path, ReadVersion(path, documentId, snapshotId));
    }

    public void Restore(BackupPolicy policy, Guid documentId, Guid snapshotId, string destination)
    {
        GuardOutsideLibrary(policy, destination);
        var folder = Folder(policy, documentId);
        using var gate = Enter(folder);
        var path = Path.Combine(folder, PublishedName(snapshotId));
        var version = ReadVersion(path, documentId, snapshotId);
        // Keep the archive locked between library verification and restoration.
        var archive = Path.Combine(path, ArchiveName);
        using var guard = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var certificate = File.OpenRead(Path.Combine(path, ManifestName));
        MonetaBackup.VerifyCertifiedBytes(archive, version);
        // The common restore engine performs the complete SQLite and snapshot checks.
        using var restored = MonetaBackup.Restore(archive, destination);
    }

    private bool IsDue(BackupPolicy policy, BackupKind kind, DateTimeOffset now, BackupVersion? latest)
    {
        if (kind == BackupKind.Manual || latest is null) return true;
        if (kind == BackupKind.AfterSave) return now - latest.CreatedUtc >= TimeSpan.FromMinutes(policy.IntervalMinutes);
        DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, time.LocalTimeZone).DateTime);
        return Day(now) != Day(latest.CreatedUtc);
    }

    private static List<BackupHistoryEntry> Enumerate(string folder, Guid documentId)
    {
        var history = new List<BackupHistoryEntry>();
        if (!Directory.Exists(folder)) return history;
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 34 || !name.StartsWith("v-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[2..], "N", out var id) || name != PublishedName(id)) continue;
            try
            {
                history.Add(new(id, ReadVersion(directory, documentId, id), directory,
                    L.Get("BackupLibrary.PublishedToBeChecked")));
            }
            catch (Exception error) when (IsLibraryError(error))
            {
                history.Add(new(id, null, directory, L.Get("BackupLibrary.NonVerifiable") + error.Message));
            }
        }
        return history.OrderByDescending(entry => entry.Version?.CreatedUtc).ThenByDescending(entry => entry.SnapshotId).ToList();
    }

    private static BackupVersion ReadVersion(string directory, Guid documentId, Guid snapshotId)
    {
        CheckNotLinked(directory);
        foreach (var name in BundleFiles) CheckNotLinked(Path.Combine(directory, name));
        using var stream = File.OpenRead(Path.Combine(directory, VersionName));
        if (stream.Length is <= 0 or > 8192) throw InvalidVersion();
        var version = JsonSerializer.Deserialize<BackupVersion>(stream, Json);
        if (version is null || version.FormatVersion != Format || version.SnapshotId != snapshotId ||
            version.DocumentId != documentId || version.CreatedUtc == default || version.CreatedUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(version.Kind) || version.SchemaVersion is < 1 or > MonetaFile.CurrentSchemaVersion ||
            version.Length <= 0 || version.Sha256 is not { Length: 64 } || !version.Sha256.All(Uri.IsHexDigit))
            throw InvalidVersion();
        if (!File.Exists(Path.Combine(directory, ArchiveName)) || !File.Exists(Path.Combine(directory, ManifestName)))
            throw InvalidVersion();
        return version;
    }

    private static BackupInspection VerifyVersion(string directory, BackupVersion version)
    {
        var details = MonetaBackup.Inspect(Path.Combine(directory, ArchiveName));
        if (details.Backup.DocumentId != version.DocumentId || details.Snapshot.SchemaVersion != version.SchemaVersion ||
            details.Length != version.Length || !string.Equals(details.Backup.Sha256, version.Sha256, StringComparison.OrdinalIgnoreCase))
            throw InvalidVersion();
        return details;
    }

    private static string? Rotate(string folder, Guid documentId, string published, BackupVersion version,
        IReadOnlyList<BackupHistoryEntry> history, int keep, CancellationToken cancellation)
    {
        // The newly verified generation stays pinned until retirement of older versions ends.
        var archive = Path.Combine(published, ArchiveName);
        using var guard = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var certificate = File.OpenRead(Path.Combine(published, ManifestName));
        using var marker = File.OpenRead(Path.Combine(published, VersionName));
        MonetaBackup.VerifyCertifiedBytes(archive, version);
        var retained = 1;
        var deferred = false;
        foreach (var entry in history)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (entry.Version is null || !KnownBundle(entry.DirectoryPath))
                {
                    deferred = true;
                    continue;
                }
                var existing = ReadVersion(entry.DirectoryPath, documentId, entry.SnapshotId);
                if (retained < keep)
                {
                    MonetaBackup.VerifyCertifiedBytes(Path.Combine(entry.DirectoryPath, ArchiveName), existing);
                    retained++;
                    continue;
                }
                _ = VerifyVersion(entry.DirectoryPath, existing);
                cancellation.ThrowIfCancellationRequested();
                var retired = Path.Combine(folder, ".retired-" + existing.SnapshotId.ToString("N"));
                Directory.Move(entry.DirectoryPath, retired);
                if (!CleanOwnedBundle(retired, BundleFiles))
                    deferred = true;
            }
            catch (Exception error) when (IsLibraryError(error)) { deferred = true; }
        }
        return deferred ? "Versions non vérifiables, ouvertes ou inconnues conservées. La rotation a pu être partielle." : null;
    }

    private static bool KnownBundle(string path)
    {
        CheckNotLinked(path);
        return Directory.EnumerateFileSystemEntries(path).Select(file =>
        {
            var attributes = File.GetAttributes(file);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 ? Path.GetFileName(file) : "";
        }).All(name => Array.IndexOf(BundleFiles, name) >= 0);
    }

    private static bool CleanOwnedBundle(string path, string[] ownedFiles)
    {
        try
        {
            if (!KnownBundle(path)) return false;
            if (Directory.EnumerateFileSystemEntries(path).Any(file => !ownedFiles.Contains(Path.GetFileName(file))))
                return false;
            foreach (var name in ownedFiles) File.Delete(Path.Combine(path, name));
            Directory.Delete(path);
            return true;
        }
        catch (Exception error) when (IsLibraryError(error)) { return false; }
    }

    private static FileStream Enter(string folder)
    {
        CheckNotLinked(folder);
        var path = Path.Combine(folder, ".moneta-library.lock");
        CheckNotLinked(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static string Folder(BackupPolicy policy, Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException(L.Get("BackupLibrary.IdentifierOfTheMissingDocument"));
        var folder = Path.Combine(Root(policy), id.ToString("N"));
        CheckNotLinked(folder);
        return folder;
    }

    private static string Root(BackupPolicy policy)
    {
        policy.Validate();
        var destination = Path.GetFullPath(policy.Destination);
        if (destination.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(destination)!).DriveType == DriveType.Network)
            throw new ArgumentException(L.Get("BackupLibrary.ChooseALocalLocationOrExternalDisk"));
        var root = Path.Combine(destination, LibraryName);
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            CheckNotLinked(current.FullName);
        return root;
    }

    private static void GuardOutsideLibrary(BackupPolicy policy, string path)
    {
        var relative = Path.GetRelativePath(Root(policy), Path.GetFullPath(path));
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException(L.Get("BackupLibrary.ChooseAWorkDocumentOutsideOfTheHistorical"));
    }

    private static void CheckNotLinked(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("BackupLibrary.LinkOrRedirectWindowsInTheVersionPath"));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static string PublishedName(Guid id) => "v-" + id.ToString("N", CultureInfo.InvariantCulture);
    private static DocumentException InvalidVersion() => new(DocumentError.VerificationFailed, "Version historique incomplète ou incohérente.");
    private static bool IsLibraryError(Exception error) => error is IOException or UnauthorizedAccessException or DocumentException or JsonException;
}
