using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

/// <summary>A local, exclusively opened Moneta document. Financial changes are committed only by Save.</summary>
public sealed partial class MonetaFile : IDisposable, ILedgerSource, IReconciliationSource, ITransferSource, IAccountHistorySource
{
    public const int CurrentSchemaVersion = 8;
    public const int ApplicationId = 0x4D4E5441;
    private const int TimeoutSeconds = 3;
    private readonly FileStream ownership;
    private bool closed;
    private LedgerLookup ledgerLookup;
    internal SqliteConnection Connection { get; }
    public string FilePath { get; }
    public FinancialDocument Snapshot { get; private set; }

    private MonetaFile(string path, SqliteConnection connection, FileStream ownership, FinancialDocument snapshot)
    {
        FilePath = path;
        Connection = connection;
        this.ownership = ownership;
        Snapshot = snapshot;
        ledgerLookup = new(snapshot);
    }

    /// <summary>Creates a document without reconciliation history; formal reconciliation requires an already saved source.</summary>
    public static MonetaFile Create(string path, FinancialDocument snapshot, IReadOnlyCollection<TransactionChange>? changes = null,
        IReadOnlyCollection<TransferChange>? transfers = null) => Create(path, snapshot, changes, transfers, null);

    internal static MonetaFile Create(string path, FinancialDocument snapshot, IReadOnlyCollection<TransactionChange>? changes,
        IReadOnlyCollection<TransferChange>? transfers, byte[]? provenance)
    {
        path = ValidatePath(path, false);
        ValidateSnapshot(snapshot);
        snapshot = Freeze(snapshot);
        changes = changes?.ToArray() ?? Array.Empty<TransactionChange>();
        transfers = transfers?.ToArray() ?? Array.Empty<TransferChange>();
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        FileStream? gate = null;
        try
        {
            gate = AcquireOwnership(path);
            EnsureNewDestination(path);
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) { }
            using (var connection = OpenConnection(temporary, false))
            {
                using var transaction = connection.BeginTransaction();
                Execute(connection, SchemaSql(CurrentSchemaVersion), transaction);
                Execute(connection, $"PRAGMA application_id={ApplicationId}; PRAGMA user_version={CurrentSchemaVersion};", transaction);
                ValidateReconciliationChanges(connection, snapshot, changes, ReconciliationBatch.Empty, transaction, creating: true);
                ValidateTransferChanges(connection, snapshot, changes, transfers, transaction, creating: true);
                WriteSnapshot(connection, snapshot, transaction);
                WriteCatalog(connection, snapshot, transaction);
                ApplyChanges(connection, snapshot, changes, transaction, creating: true);
                WriteTransferChanges(connection, transfers, transaction);
                WriteSchedule(connection, transaction, snapshot, null, changes, transfers);
                WriteAnalysis(connection, transaction, snapshot, null);
                if (provenance is not null) WriteBankPerfectProvenance(connection, transaction, provenance);
                InsertHistory(connection, transaction, 8, 0, "create-v8",
                    provenance is null ? "native-accounts-v8" : "native-accounts-v8+bankperfect-v1");
                snapshot = ReadWithinTransaction(connection, false, transaction);
                transaction.Commit();
                Checkpoint(connection);
                // Read-only verification must not recreate WAL companions under the unpublished name.
                if (!string.Equals(Scalar(connection, "PRAGMA journal_mode=DELETE;")?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
                    throw Error(DocumentError.VerificationFailed, "Le dossier temporaire ne peut pas être rendu autonome.");
            }
            using (var validation = OpenConnection(temporary, true)) VerifySnapshot(snapshot, ReadValidated(validation));
            EnsureNewDestination(path);
            File.Move(temporary, path, false);
            var writable = OpenConnection(path, false);
            try
            {
                var published = ReadValidated(writable);
                VerifySnapshot(snapshot, published);
                var result = new MonetaFile(path, writable, gate, published);
                gate = null;
                return result;
            }
            catch { writable.Dispose(); throw; }
        }
        catch (Exception exception) when (exception is not DocumentException)
        {
            throw Translate(exception, File.Exists(path) ? DocumentError.DestinationExists : DocumentError.WriteFailed);
        }
        finally
        {
            gate?.Dispose();
            // Only our unpublished principal is disposable; SQLite alone manages recovery journals.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static MonetaFile Open(string path)
    {
        path = ValidatePath(path, true);
        FileStream? gate = null;
        SqliteConnection? connection = null;
        try
        {
            gate = AcquireOwnership(path);
            ValidateSignature(path);
            using (var diagnostic = OpenConnection(path, true)) _ = ReadValidated(diagnostic);
            connection = OpenConnection(path, false);
            var result = new MonetaFile(path, connection, gate, ReadValidated(connection));
            connection = null;
            gate = null;
            return result;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
        finally { connection?.Dispose(); gate?.Dispose(); }
    }

    public static FinancialDocument Inspect(string path)
    {
        path = ValidatePath(path, true);
        try
        {
            ValidateSignature(path);
            using var connection = OpenConnection(path, true);
            return ReadValidated(connection);
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
    }

    public void Save(FinancialDocument snapshot, IReadOnlyCollection<TransactionChange>? changes = null, ReconciliationBatch? reconciliation = null, IReadOnlyCollection<TransferChange>? transfers = null)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        ValidateSnapshot(snapshot);
        snapshot = Freeze(snapshot);
        changes = changes?.ToArray() ?? Array.Empty<TransactionChange>();
        reconciliation = FreezeBatch(reconciliation);
        transfers = transfers?.ToArray() ?? Array.Empty<TransferChange>();
        if (transfers.Count != 0 && (reconciliation.Sessions.Count != 0 || reconciliation.Events.Count != 0))
            throw Error(DocumentError.InvalidData, "Enregistrez d’abord les changements de rapprochement avant de modifier un virement.");
        if (snapshot.Id != Snapshot.Id || snapshot.CreatedUtc != Snapshot.CreatedUtc)
            throw Error(DocumentError.InvalidData, "L’identité et la date de création du dossier sont immuables.");
        if (snapshot.ModifiedUtc < Snapshot.ModifiedUtc)
            throw Error(DocumentError.InvalidData, "La date de modification ne peut pas reculer.");
        var accounts = snapshot.Accounts.ToDictionary(account => account.Id);
        var currencies = snapshot.Currencies.ToDictionary(currency => currency.Code, StringComparer.Ordinal);
        foreach (var previous in Snapshot.Accounts)
        {
            if (!accounts.TryGetValue(previous.Id, out var current))
                continue; // Removals are checked against persisted history inside the write transaction.
            if (current.Currency.Code != previous.Currency.Code || current.CreatedUtc != previous.CreatedUtc || current.ModifiedUtc < previous.ModifiedUtc)
                throw Error(DocumentError.InvalidData, "Devise native et création du compte immuables ; modification chronologique requise.");
            if (current.OpeningBalance != previous.OpeningBalance)
                throw Error(DocumentError.InvalidData, "Le solde initial est fixé à la création. Saisissez la correction comme opération dans le registre.");
        }
        foreach (var previous in Snapshot.Currencies)
            if (!currencies.TryGetValue(previous.Code, out var current) || current.DecimalPlaces != previous.DecimalPlaces)
                throw Error(DocumentError.InvalidData, "Une devise enregistrée ne peut être supprimée ou changer d’échelle.");
        var categories = snapshot.Categories.ToDictionary(value => value.Id);
        var payees = snapshot.Payees.Select(value => value.Id).ToHashSet();
        var methods = snapshot.PaymentMethods.Select(value => value.Id).ToHashSet();
        if (Snapshot.Categories.Any(value => !categories.TryGetValue(value.Id, out var current) || current.ParentId != value.ParentId) ||
            Snapshot.Payees.Any(value => !payees.Contains(value.Id)) || Snapshot.PaymentMethods.Any(value => !methods.Contains(value.Id)))
            throw Error(DocumentError.InvalidData, "Les références enregistrées ne peuvent être supprimées ou changer de parent.");
        try
        {
            using var transaction = Connection.BeginTransaction();
            var persisted = ReadWithinTransaction(Connection, false, transaction);
            if (!SameSnapshot(Snapshot, persisted))
                throw Error(DocumentError.InvalidData, "Le dossier a été modifié par un autre client SQLite. Fermez et rouvrez-le avant toute sauvegarde.");
            RemoveObsoleteBudgets(Connection, transaction, snapshot, persisted);
            ValidateAndRemoveAccounts(persisted, accounts, changes, reconciliation, transaction);
            ValidateReconciliationChanges(Connection, snapshot, changes, reconciliation, transaction, creating: false, baseline: persisted);
            ValidateTransferChanges(Connection, snapshot, changes, transfers, transaction, creating: false, baseline: persisted);
            WriteSnapshot(Connection, snapshot, transaction);
            WriteCatalog(Connection, snapshot, transaction);
            WriteReconciliationChanges(Connection, snapshot, reconciliation, transaction);
            ApplyChanges(Connection, snapshot, changes, transaction, creating: false);
            WriteTransferChanges(Connection, transfers, transaction);
            WriteSchedule(Connection, transaction, snapshot, persisted, changes, transfers);
            WriteAnalysis(Connection, transaction, snapshot, persisted);
            var saved = ReadWithinTransaction(Connection, false, transaction);
            var savedLookup = new LedgerLookup(saved);
            transaction.Commit();
            Snapshot = saved;
            ledgerLookup = savedLookup;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.WriteFailed); }
    }

    public void Close()
    {
        if (closed) return;
        try { Checkpoint(Connection); }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.WriteFailed); }
        Connection.Dispose();
        ownership.Dispose();
        closed = true;
    }

    public void Dispose() => Close();

    private static void EnsureNewDestination(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(path + suffix) || Directory.Exists(path + suffix))
                throw Error(DocumentError.DestinationExists, "La destination ou un journal SQLite existe déjà ; aucun élément n’a été remplacé.");
    }

    private static bool SameSnapshot(FinancialDocument expected, FinancialDocument actual) =>
        expected.Id == actual.Id && expected.Name == actual.Name &&
        expected.CreatedUtc == actual.CreatedUtc && expected.ModifiedUtc == actual.ModifiedUtc &&
        expected.SchemaVersion == actual.SchemaVersion && expected.SelectedAccountId == actual.SelectedAccountId &&
        expected.Currencies.SequenceEqual(actual.Currencies) && expected.Accounts.SequenceEqual(actual.Accounts) &&
        expected.Categories.OrderBy(value => value.Id).SequenceEqual(actual.Categories.OrderBy(value => value.Id)) &&
        expected.Payees.OrderBy(value => value.Id).SequenceEqual(actual.Payees.OrderBy(value => value.Id)) &&
        expected.PaymentMethods.OrderBy(value => value.Id).SequenceEqual(actual.PaymentMethods.OrderBy(value => value.Id)) &&
        expected.LedgerDigest == actual.LedgerDigest && expected.OperationCount == actual.OperationCount &&
        expected.ReconciliationCount == actual.ReconciliationCount && expected.ReconciliationEventCount == actual.ReconciliationEventCount &&
        expected.TransferCount == actual.TransferCount &&
        expected.BankPerfectProvenanceHash == actual.BankPerfectProvenanceHash &&
        expected.Schedules.OrderBy(value => value.Id).SequenceEqual(actual.Schedules.OrderBy(value => value.Id)) &&
        expected.ScheduleHistory.OrderBy(value => (value.PlanId, value.Number, value.Step))
            .SequenceEqual(actual.ScheduleHistory.OrderBy(value => (value.PlanId, value.Number, value.Step))) && SameAnalysis(expected, actual);

    private static void VerifySnapshot(FinancialDocument expected, FinancialDocument actual)
    {
        if (!SameSnapshot(expected, actual))
            throw Error(DocumentError.VerificationFailed, "Le dossier publié ne correspond pas intégralement aux données demandées.");
    }

    internal static SqliteConnection OpenConnection(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = TimeoutSeconds
        }.ToString());
        try
        {
            connection.Open();
            connection.EnableExtensions(false);
            Execute(connection, "PRAGMA trusted_schema=OFF; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=3000;");
            var versionText = Convert.ToString(Scalar(connection, "SELECT sqlite_version();"), CultureInfo.InvariantCulture)!;
            if (!Version.TryParse(versionText, out var version) || version < new Version(3, 51, 3))
                throw Error(DocumentError.UnsupportedVersion, "Le moteur SQLite doit inclure le correctif WAL (3.51.3 minimum).");
            if (Integer(connection, "PRAGMA trusted_schema;") != 0 || Integer(connection, "PRAGMA foreign_keys;") != 1 || Integer(connection, "PRAGMA busy_timeout;") != 3000)
                throw Error(DocumentError.VerificationFailed, "Configuration de sécurité SQLite non appliquée.");
            if (!readOnly)
            {
                if (!string.Equals(Convert.ToString(Scalar(connection, "PRAGMA journal_mode=WAL;"), CultureInfo.InvariantCulture), "wal", StringComparison.OrdinalIgnoreCase))
                    throw Error(DocumentError.VerificationFailed, "Le mode WAL est obligatoire.");
                Execute(connection, "PRAGMA synchronous=FULL;");
                if (Integer(connection, "PRAGMA synchronous;") != 2)
                    throw Error(DocumentError.VerificationFailed, "La synchronisation FULL est obligatoire.");
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    internal static string ValidatePath(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Error(DocumentError.InvalidData, "Chemin de fichier requis.");
        // Reject BankPerfect before opening, locking, creating, or otherwise touching it.
        if (Path.GetExtension(path).StartsWith(".bp", StringComparison.OrdinalIgnoreCase))
            throw Error(DocumentError.InvalidFormat, "Un fichier BankPerfect n’est pas un dossier Moneta.");
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.StartsWith("//", StringComparison.Ordinal))
                throw Error(DocumentError.InvalidData, "Les chemins réseau ne sont pas pris en charge.");
            var root = Path.GetPathRoot(full)!;
            if (new DriveInfo(root).DriveType == DriveType.Network)
                throw Error(DocumentError.InvalidData, "Une base active doit résider sur un disque local.");
            if (mustExist && !File.Exists(full)) throw Error(DocumentError.MissingFile, "Le dossier demandé n’existe pas.");
            if (!mustExist && (File.Exists(full) || Directory.Exists(full))) throw Error(DocumentError.DestinationExists, "La destination existe déjà.");
            if (!Directory.Exists(Path.GetDirectoryName(full))) throw Error(DocumentError.MissingFile, "Le répertoire de destination n’existe pas.");
            return full;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    private static FileStream AcquireOwnership(string path)
    {
        try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new DocumentException(DocumentError.InUse, "Ce dossier est déjà ouvert ou son verrou est indisponible.", exception); }
        catch (UnauthorizedAccessException exception) { throw new DocumentException(DocumentError.AccessDenied, "Accès au verrou du dossier refusé.", exception); }
    }

    private static void ValidateSignature(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[16];
        if (stream.Read(header) != header.Length || !header.SequenceEqual("SQLite format 3\0"u8))
            throw Error(DocumentError.InvalidFormat, "Signature SQLite absente ou tronquée ; aucun fichier n’a été recréé.");
    }

    internal static void Checkpoint(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0 || reader.GetInt64(2) != 0 || reader.Read())
            throw Error(DocumentError.Locked, "Checkpoint incomplet : conserver le dossier et tous ses journaux ; fichier unique non garanti.");
    }

    private static FinancialDocument Freeze(FinancialDocument snapshot) => snapshot with
    {
        Currencies = Array.AsReadOnly(snapshot.Currencies.ToArray()),
        Accounts = Array.AsReadOnly(snapshot.Accounts.ToArray()),
        Categories = Array.AsReadOnly(snapshot.Categories.ToArray()),
        Payees = Array.AsReadOnly(snapshot.Payees.ToArray()),
        PaymentMethods = Array.AsReadOnly(snapshot.PaymentMethods.ToArray()),
        AccountBalances = snapshot.AccountBalances is null ? null :
            new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(snapshot.AccountBalances.ToDictionary(pair => pair.Key, pair => pair.Value)),
        Schedules = Array.AsReadOnly(snapshot.Schedules.ToArray()),
        ScheduleHistory = Array.AsReadOnly(snapshot.ScheduleHistory.ToArray()),
        Budgets = Array.AsReadOnly(snapshot.Budgets.Select(item => item with
        {
            Accounts = Array.AsReadOnly(item.Accounts.Order().ToArray()), Categories = Array.AsReadOnly(item.Categories.Order().ToArray())
        }).ToArray()),
        FrozenReports = Array.AsReadOnly(snapshot.FrozenReports.ToArray()),
        AccountPointedBalances = snapshot.AccountPointedBalances is null ? null :
            new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(snapshot.AccountPointedBalances.ToDictionary(pair => pair.Key, pair => pair.Value))
    };

    private static DocumentException Error(DocumentError error, string message) => new(error, message);

    internal static DocumentException Translate(Exception exception, DocumentError fallback = DocumentError.Corrupt)
    {
        if (exception is DocumentException document) return document;
        var error = exception switch
        {
            UnauthorizedAccessException => DocumentError.AccessDenied,
            FileNotFoundException or DirectoryNotFoundException => DocumentError.MissingFile,
            SqliteException sqlite when sqlite.SqliteErrorCode is 5 or 6 => DocumentError.Locked,
            SqliteException sqlite when sqlite.SqliteErrorCode is 8 or 23 => DocumentError.AccessDenied,
            SqliteException sqlite when sqlite.SqliteErrorCode is 11 or 26 => DocumentError.Corrupt,
            SqliteException sqlite when sqlite.SqliteErrorCode is 10 or 13 => DocumentError.WriteFailed,
            _ => fallback
        };
        return new DocumentException(error, error == DocumentError.Locked ? "SQLite est verrouillé ; aucune sauvegarde n’a été confirmée." : "Impossible de traiter le dossier Moneta : " + exception.Message, exception);
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static long Integer(SqliteConnection connection, string sql, SqliteTransaction? transaction = null) => Convert.ToInt64(Scalar(connection, sql, transaction), CultureInfo.InvariantCulture);
}
