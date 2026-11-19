using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed record BackupResult(string FilePath, string Sha256, Guid DocumentId);
public sealed record BackupInspection(BackupResult Backup, int ManifestVersion, long Length, FinancialDocument Snapshot);
public enum BackupPhase { Capture, Verification, Publication, Rotation }

/// <summary>
/// Certified SQLite snapshots. The manifest detects accidental modification; it is not
/// an authenticity signature and neither the database nor its manifest is encrypted.
/// </summary>
public static class MonetaBackup
{
    private const int ManifestVersion = 1;
    private static readonly string[] DatabaseCompanions = ["-wal", "-shm", "-journal", ".lock"];
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static BackupResult Create(MonetaFile source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Execute(() => CreateCore(source.Connection, source.Snapshot, destination, default, null),
            "Création de la sauvegarde impossible.");
    }

    internal static BackupResult CreateFromCommitted(string sourcePath, FinancialDocument expected, string destination,
        CancellationToken cancellation, Action<BackupPhase>? phase) => Execute(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        using var source = MonetaFile.OpenConnection(ExistingPath(sourcePath), readOnly: true);
        return CreateCore(source, expected, destination, cancellation, phase);
    }, "Création de la sauvegarde impossible.");

    private static BackupResult CreateCore(SqliteConnection source, FinancialDocument snapshot, string destination,
        CancellationToken cancellation, Action<BackupPhase>? phase, bool allowLegacy = false)
    {
        var target = NewDestination(destination, requireManifest: true);
        string? temporary = null;
        string? temporaryManifest = null;
        try
        {
            phase?.Invoke(BackupPhase.Capture);
            cancellation.ThrowIfCancellationRequested();
            var expected = Digest(snapshot);
            var live = MonetaFile.ReadValidated(source, allowLegacy);
            RequireEqual(expected, Digest(live), "Le dossier actif a changé depuis son ouverture ou sa dernière sauvegarde.");
            cancellation.ThrowIfCancellationRequested();
            temporary = ReserveTemporary(target);
            var copied = CopyDatabase(source, temporary, allowLegacy, beforeVerification: () =>
            {
                phase?.Invoke(BackupPhase.Verification);
                cancellation.ThrowIfCancellationRequested();
            });
            RequireEqual(expected, Digest(copied), "La copie SQLite ne correspond pas au dossier actif.");
            cancellation.ThrowIfCancellationRequested();
            using (var bytes = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                bytes.Flush(flushToDisk: true);
            var certification = Certify(temporary, copied);
            temporaryManifest = ReserveTemporary(target + ".manifest.json");
            using (var stream = new FileStream(temporaryManifest, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, certification, ManifestJson);
                stream.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
            temporary = null;
            // Publication is deliberately last: an interrupted publication is not certified.
            File.Move(temporaryManifest, target + ".manifest.json", overwrite: false);
            temporaryManifest = null;
            return new BackupResult(target, certification.Sha256, copied.Id);
        }
        finally
        {
            DeleteTemporary(temporaryManifest);
            DeleteTemporary(temporary);
        }
    }

    public static BackupInspection Inspect(string backup) => Execute(() =>
    {
        var sourcePath = ExistingPath(backup);
        using var bytes = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = ReadManifest(sourcePath + ".manifest.json");
        VerifyFile(bytes, manifest);
        using var source = MonetaFile.OpenConnection(sourcePath, readOnly: true);
        var snapshot = MonetaFile.ReadValidated(source, allowVersion1: true);
        VerifySnapshot(snapshot, manifest);
        return new BackupInspection(new(sourcePath, manifest.Sha256, manifest.DocumentId), manifest.ManifestVersion, manifest.Length, snapshot);
    }, "Vérification de la sauvegarde impossible.");

    internal static void VerifyCertifiedBytes(string archive, BackupVersion version) => Execute(() =>
    {
        using var bytes = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = ReadManifest(archive + ".manifest.json");
        if (manifest.DocumentId != version.DocumentId || manifest.SchemaVersion != version.SchemaVersion ||
            manifest.Length != version.Length)
            throw new DocumentException(DocumentError.VerificationFailed, "Le manifeste ne correspond pas à la version publiée.");
        RequireEqual(version.Sha256, manifest.Sha256, "L’empreinte ne correspond pas à la version publiée.");
        VerifyFile(bytes, manifest);
        return true;
    }, "Vérification de la dernière version impossible.");

    public static MonetaFile Restore(string backup, string destination) => Execute(() =>
    {
        var target = NewDestination(destination, requireManifest: false);
        var sourcePath = ExistingPath(backup);
        string expected;
        string? temporary = null;
        try
        {
            // Keep the certified file read-locked through the copy and the second hash.
            using var sourceBytes = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = ReadManifest(sourcePath + ".manifest.json");
            using (var source = MonetaFile.OpenConnection(sourcePath, readOnly: true))
            {
                VerifyFile(sourceBytes, manifest);
                var snapshot = MonetaFile.ReadValidated(source, allowVersion1: true);
                VerifySnapshot(snapshot, manifest);
                temporary = ReserveTemporary(target);
                var copied = CopyDatabase(source, temporary, allowLegacy: true);
                VerifySnapshot(copied, manifest);
                VerifyFile(sourceBytes, manifest);
                expected = Digest(UpgradeCopy(temporary, copied));
            }
            return PublishAndOpen(ref temporary, target, expected);
        }
        finally { DeleteTemporary(temporary); }
    }, "Restauration de la sauvegarde impossible.");

    public static MonetaFile Migrate(string source, string destination, string? preMigrationBackup = null) => Execute(() =>
    {
        var target = NewDestination(destination, requireManifest: false);
        var sourcePath = ExistingPath(source);
        string? temporary = null;
        string expected;
        try
        {
            using (var sourceBytes = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var connection = MonetaFile.OpenConnection(sourcePath, readOnly: true))
            {
                var originalHash = Hash(sourceBytes);
                var original = MonetaFile.ReadValidated(connection, allowVersion1: true);
                if (original.SchemaVersion is < 1 or >= MonetaFile.CurrentSchemaVersion)
                    throw new DocumentException(DocumentError.InvalidData, "Le dossier ne nécessite pas de migration par copie.");
                if (preMigrationBackup is not null)
                    _ = CreateCore(connection, original, preMigrationBackup, default, null, allowLegacy: true);
                temporary = ReserveTemporary(target);
                var copied = CopyDatabase(connection, temporary, allowLegacy: true);
                RequireEqual(Digest(original), Digest(copied), "La copie avant migration diffère du dossier source.");
                expected = Digest(UpgradeCopy(temporary, copied));
                RequireEqual(originalHash, Hash(sourceBytes), "Le dossier source a changé pendant la migration.");
            }
            return PublishAndOpen(ref temporary, target, expected);
        }
        finally { DeleteTemporary(temporary); }
    }, "Migration par copie impossible.");

    public static MonetaFile SaveAs(MonetaFile source, string destination, FinancialDocument metadata,
        IReadOnlyCollection<TransactionChange> changes, ReconciliationBatch? reconciliation = null,
        IReadOnlyCollection<TransferChange>? transfers = null) => Execute(() =>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(changes);
        var target = NewDestination(destination, requireManifest: false);
        string? temporary = null;
        try
        {
            var baseline = Digest(source.Snapshot);
            RequireEqual(baseline, Digest(MonetaFile.ReadValidated(source.Connection)),
                "Le dossier actif a changé depuis son ouverture ou sa dernière sauvegarde.");
            temporary = ReserveTemporary(target);
            var copied = CopyDatabase(source.Connection, temporary);
            RequireEqual(baseline, Digest(copied), "La copie SQLite ne correspond pas au dossier actif.");
            string expected;
            // Only the owned work copy is writable; failure leaves the source and drafts untouched.
            using (var candidate = MonetaFile.Open(temporary))
            {
                candidate.Save(metadata, changes, reconciliation, transfers);
                expected = Digest(candidate.Snapshot);
            }
            File.Delete(temporary + ".lock");
            using (var connection = MonetaFile.OpenConnection(temporary, readOnly: false))
                MakeStandalone(connection);
            return PublishAndOpen(ref temporary, target, expected);
        }
        finally { DeleteTemporary(temporary); }
    }, "Enregistrement sous un nouveau nom impossible.");

    private static FinancialDocument UpgradeCopy(string path, FinancialDocument original)
    {
        if (original.SchemaVersion == MonetaFile.CurrentSchemaVersion) return original;
        using var connection = MonetaFile.OpenConnection(path, readOnly: false);
        if (original.SchemaVersion == 1) MonetaFile.UpgradeV1(connection);
        if (original.SchemaVersion <= 2) MonetaFile.UpgradeV2(connection);
        if (original.SchemaVersion <= 3) MonetaFile.UpgradeV3(connection);
        if (original.SchemaVersion <= 4) MonetaFile.UpgradeV4(connection);
        if (original.SchemaVersion <= 5) MonetaFile.UpgradeV5(connection);
        MonetaFile.UpgradeV6(connection);
        var upgraded = MonetaFile.ReadValidated(connection);
        // Compare historical ledgers through their original canonical projection;
        // newer digests additionally certify reconciliation history and transfer links.
        var originalRepresentation = original.SchemaVersion switch
        {
            3 => MonetaFile.ReadLegacyV3Representation(connection),
            4 => MonetaFile.ReadLegacyV4Representation(connection),
            _ => upgraded with { SchemaVersion = original.SchemaVersion }
        };
        RequireEqual(Digest(original), Digest(originalRepresentation),
            "La migration a modifié les données du dossier.");
        if (original.SchemaVersion <= 2 && (upgraded.OperationCount != 0 || upgraded.Categories.Count != 0 ||
            upgraded.Payees.Count != 0 || upgraded.PaymentMethods.Count != 0))
            throw new DocumentException(DocumentError.VerificationFailed, "La migration a ajouté des données financières.");
        MakeStandalone(connection);
        return upgraded;
    }

    private static FinancialDocument CopyDatabase(SqliteConnection source, string temporary, bool allowLegacy = false,
        Action? beforeVerification = null)
    {
        using var copy = MonetaFile.OpenConnection(temporary, readOnly: false);
        source.BackupDatabase(copy);
        beforeVerification?.Invoke();
        var snapshot = MonetaFile.ReadValidated(copy, allowVersion1: allowLegacy);
        MakeStandalone(copy);
        return snapshot;
    }

    internal static void MakeStandalone(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetInt64(0) != 0)
                throw new DocumentException(DocumentError.Locked, "La copie SQLite ne peut pas être consolidée.");
        }
        command.CommandText = "PRAGMA journal_mode=DELETE;";
        if (!string.Equals(command.ExecuteScalar()?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
            throw new DocumentException(DocumentError.VerificationFailed, "La sauvegarde dépend encore d'un journal SQLite.");
    }

    private static MonetaFile PublishAndOpen(ref string? temporary, string destination, string expected)
    {
        // Reopen the standalone copy without creating a temporary ownership-lock file.
        using (var check = MonetaFile.OpenConnection(temporary!, readOnly: true))
            RequireEqual(expected, Digest(MonetaFile.ReadValidated(check)), "La réouverture de la copie a changé ses données.");
        File.Move(temporary!, destination, overwrite: false);
        temporary = null;
        var result = MonetaFile.Open(destination);
        try
        {
            RequireEqual(expected, Digest(result.Snapshot), "Le dossier publié ne correspond pas à la copie vérifiée.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static Certification Certify(string path, FinancialDocument snapshot)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new Certification(ManifestVersion, Hash(stream), stream.Length, snapshot.SchemaVersion,
            snapshot.Id, Digest(snapshot), snapshot.Currencies.Count, snapshot.Accounts.Count);
    }

    private static Certification ReadManifest(string path)
    {
        if (!File.Exists(path))
            throw new DocumentException(DocumentError.VerificationFailed, "Manifeste de sauvegarde absent : cette copie n'est pas certifiée.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 65536)
            throw new DocumentException(DocumentError.VerificationFailed, "Taille du manifeste de sauvegarde invalide.");
        Certification? manifest;
        try { manifest = JsonSerializer.Deserialize<Certification>(stream, ManifestJson); }
        catch (JsonException ex) { throw new DocumentException(DocumentError.VerificationFailed, "Manifeste de sauvegarde illisible.", ex); }
        if (manifest is null || manifest.ManifestVersion != ManifestVersion || manifest.SchemaVersion is < 1 or > MonetaFile.CurrentSchemaVersion ||
            manifest.DocumentId == Guid.Empty || manifest.Length <= 0 || manifest.CurrencyCount < 0 || manifest.AccountCount < 0 ||
            !ValidHash(manifest.Sha256) || !ValidHash(manifest.SnapshotDigest))
            throw new DocumentException(DocumentError.VerificationFailed, "Manifeste de sauvegarde invalide ou non pris en charge.");
        return manifest;
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void VerifyFile(FileStream stream, Certification manifest)
    {
        if (stream.Length != manifest.Length)
            throw new DocumentException(DocumentError.VerificationFailed, "La taille de la sauvegarde ne correspond pas au manifeste.");
        RequireEqual(manifest.Sha256, Hash(stream), "L'empreinte SHA-256 de la sauvegarde ne correspond pas au manifeste.");
    }

    private static void VerifySnapshot(FinancialDocument snapshot, Certification manifest)
    {
        if (snapshot.Id != manifest.DocumentId || snapshot.SchemaVersion != manifest.SchemaVersion ||
            snapshot.Currencies.Count != manifest.CurrencyCount || snapshot.Accounts.Count != manifest.AccountCount)
            throw new DocumentException(DocumentError.VerificationFailed, "L'identité ou le contenu de la sauvegarde ne correspond pas au manifeste.");
        RequireEqual(manifest.SnapshotDigest, Digest(snapshot), "Les données complètes de la sauvegarde ne correspondent pas au manifeste.");
    }

    // Canonical, culture-independent full snapshot, retaining the document's list order.
    internal static string Digest(FinancialDocument snapshot)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", snapshot.SchemaVersion);
            writer.WriteString("id", snapshot.Id);
            writer.WriteString("name", snapshot.Name);
            writer.WriteString("created", snapshot.CreatedUtc.ToUniversalTime());
            writer.WriteString("modified", snapshot.ModifiedUtc.ToUniversalTime());
            if (snapshot.SelectedAccountId is { } selected) writer.WriteString("selected", selected);
            else writer.WriteNull("selected");
            writer.WriteStartArray("currencies");
            foreach (var currency in snapshot.Currencies)
            {
                writer.WriteStartObject();
                writer.WriteString("code", currency.Code);
                writer.WriteString("name", currency.Name);
                writer.WriteNumber("decimals", currency.DecimalPlaces);
                writer.WriteString("symbol", currency.Symbol);
                writer.WriteString("culture", currency.DisplayRules.CultureName);
                writer.WriteBoolean("useSymbol", currency.DisplayRules.UseSymbol);
                writer.WriteBoolean("symbolBefore", currency.DisplayRules.SymbolBeforeAmount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("accounts");
            foreach (var account in snapshot.Accounts)
            {
                writer.WriteStartObject();
                writer.WriteString("id", account.Id);
                writer.WriteString("name", account.Name);
                writer.WriteString("currency", account.Currency.Code);
                writer.WriteNumber("openingMinor", ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency));
                writer.WriteString("type", account.Type);
                writer.WriteString("notes", account.Notes);
                if (snapshot.SchemaVersion >= 6)
                {
                    writer.WriteString("bankName", account.BankName);
                    writer.WriteString("bankReference", account.BankReference);
                    writer.WriteString("holder", account.Holder);
                }
                writer.WriteBoolean("archived", account.IsArchived);
                writer.WriteString("created", account.CreatedUtc.ToUniversalTime());
                writer.WriteString("modified", account.ModifiedUtc.ToUniversalTime());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (snapshot.SchemaVersion >= 3)
            {
                writer.WriteStartArray("categories");
                foreach (var category in snapshot.Categories.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", category.Id);
                    writer.WriteString("name", category.Name);
                    if (category.ParentId is { } parent) writer.WriteString("parent", parent);
                    else writer.WriteNull("parent");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("payees");
                foreach (var payee in snapshot.Payees.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", payee.Id);
                    writer.WriteString("name", payee.Name);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("paymentMethods");
                foreach (var method in snapshot.PaymentMethods.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", method.Id);
                    writer.WriteString("name", method.Name);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (snapshot.SchemaVersion == 3) writer.WriteString("ledgerDigest", snapshot.LedgerDigest);
                writer.WriteNumber("operationCount", snapshot.OperationCount);
            }
            if (snapshot.SchemaVersion >= 4)
            {
                // Each version uses the storage-validated stream appropriate to
                // its schema; historical v4 bytes remain unchanged.
                writer.WriteString(snapshot.SchemaVersion == 4 ? "v4LedgerDigest" : "v5LedgerDigest", snapshot.LedgerDigest);
                writer.WriteNumber("reconciliationCount", snapshot.ReconciliationCount);
                writer.WriteNumber("reconciliationEventCount", snapshot.ReconciliationEventCount);
            }
            if (snapshot.SchemaVersion >= 5)
                writer.WriteNumber("transferCount", snapshot.TransferCount);
            if (snapshot.BankPerfectProvenanceHash is { } provenance)
                writer.WriteString("bankPerfectProvenance", provenance);
            if (snapshot.SchemaVersion >= 7)
            {
                writer.WritePropertyName("schedules");
                JsonSerializer.Serialize(writer, snapshot.Schedules.OrderBy(value => value.Id));
                writer.WritePropertyName("scheduleHistory");
                JsonSerializer.Serialize(writer, snapshot.ScheduleHistory.OrderBy(value => (value.PlanId, value.Number, value.Step)));
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static string Hash(FileStream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RequireEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new DocumentException(DocumentError.VerificationFailed, message);
    }

    private static string ExistingPath(string path)
    {
        MonetaFile.ValidatePath(path, mustExist: true);
        return Path.GetFullPath(path);
    }

    private static string NewDestination(string path, bool requireManifest)
    {
        MonetaFile.ValidatePath(path, mustExist: false);
        var full = Path.GetFullPath(path);
        foreach (var suffix in DatabaseCompanions)
            if (File.Exists(full + suffix) || Directory.Exists(full + suffix))
                throw new DocumentException(DocumentError.DestinationExists, "Un fichier compagnon de destination existe déjà ; il ne sera pas remplacé.");
        if (requireManifest && (File.Exists(full + ".manifest.json") || Directory.Exists(full + ".manifest.json")))
            throw new DocumentException(DocumentError.DestinationExists, "Le manifeste de destination existe déjà ; il ne sera pas remplacé.");
        return full;
    }

    private static string ReserveTemporary(string destination)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".moneta-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return temporary;
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null) return;
        // Only our reserved principal and its ownership lock are disposable.
        // SQLite alone manages recovery journals, including those of a work copy.
        for (var index = 0; index < 2; index++)
        {
            try { File.Delete(index == 0 ? path : path + ".lock"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static T Execute<T>(Func<T> action, string message)
    {
        try { return action(); }
        catch (DocumentException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new DocumentException(DocumentError.AccessDenied, message, ex); }
        catch (FileNotFoundException ex) { throw new DocumentException(DocumentError.MissingFile, message, ex); }
        catch (DirectoryNotFoundException ex) { throw new DocumentException(DocumentError.MissingFile, message, ex); }
        catch (SqliteException ex)
        {
            var error = ex.SqliteErrorCode switch
            {
                5 or 6 => DocumentError.Locked,
                11 or 26 => DocumentError.Corrupt,
                8 or 23 => DocumentError.AccessDenied,
                _ => DocumentError.WriteFailed
            };
            throw new DocumentException(error, message, ex);
        }
        catch (IOException ex)
        {
            var error = (ex.HResult & 0xffff) switch
            {
                32 or 33 => DocumentError.Locked,
                80 or 183 => DocumentError.DestinationExists,
                _ => DocumentError.WriteFailed
            };
            throw new DocumentException(error, error == DocumentError.Locked
                ? message + " Le fichier est verrouillé par une autre application ; libérez son accès puis réessayez."
                : message, ex);
        }
        catch (ArgumentException ex) { throw new DocumentException(DocumentError.InvalidData, message, ex); }
    }

    private sealed record Certification(
        [property: JsonRequired] int ManifestVersion,
        [property: JsonRequired] string Sha256,
        [property: JsonRequired] long Length,
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] Guid DocumentId,
        [property: JsonRequired] string SnapshotDigest,
        [property: JsonRequired] int CurrencyCount,
        [property: JsonRequired] int AccountCount);
}
