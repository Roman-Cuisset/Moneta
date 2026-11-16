using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    internal const int MaxBankPerfectProvenanceBytes = 64 * 1024 * 1024;
    private const string BankPerfectProvenanceSql = """
        CREATE TABLE BankPerfectProvenance (
            singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1),
            format_version INTEGER NOT NULL CHECK(format_version=1),
            sha256 TEXT NOT NULL CHECK(length(sha256)=64),
            payload BLOB NOT NULL CHECK(length(payload) BETWEEN 1 AND 67108864)
        ) STRICT
        """;

    private static void WriteBankPerfectProvenance(SqliteConnection connection, SqliteTransaction transaction, byte[] payload)
    {
        if (payload.Length is 0 or > MaxBankPerfectProvenanceBytes)
            throw Error(DocumentError.InvalidData, "Taille du rapport de provenance hors limites.");
        Execute(connection, BankPerfectProvenanceSql, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO BankPerfectProvenance VALUES(1,1,$hash,$payload);";
        command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(payload)));
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    private static string VerifyBankPerfectProvenance(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT singleton,format_version,sha256,length(payload),payload FROM BankPerfectProvenance;";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != 1 || reader.GetInt64(1) != 1 ||
            reader.GetInt64(3) is <= 0 or > MaxBankPerfectProvenanceBytes)
            throw Error(DocumentError.Corrupt, "Rapport de provenance BankPerfect absent ou de format invalide.");
        var expected = reader.GetString(2);
        using var payload = reader.GetStream(4);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        if (hash != expected || reader.Read())
            throw Error(DocumentError.Corrupt, "Empreinte du rapport de provenance BankPerfect invalide.");
        return hash;
    }

    /// <summary>Returns the private migration report, after checking it against the open snapshot.</summary>
    public byte[]? ReadBankPerfectProvenance()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (Snapshot.BankPerfectProvenanceHash is not { } expected) return null;
        using var transaction = Connection.BeginTransaction(deferred: true);
        if (VerifyBankPerfectProvenance(Connection, transaction) != expected)
            throw Error(DocumentError.VerificationFailed, "La provenance a changé depuis l’ouverture du dossier.");
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload FROM BankPerfectProvenance WHERE singleton=1;";
        return (byte[])command.ExecuteScalar()!;
    }

    // The candidate has already been reopened and checked with the normal Moneta APIs.
    // Its final, standalone database is the single unit of publication.
    internal static void PublishVerifiedImport(string candidate, string path, FinancialDocument expected, CancellationToken cancellationToken)
    {
        path = ValidatePath(path, false);
        // The exclusive stream must be created by this attempt, so it is safe to remove on failure.
        var lockPath = path + ".lock";
        FileStream gate;
        try { gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) when (File.Exists(lockPath) || Directory.Exists(lockPath))
        { throw new DocumentException(DocumentError.DestinationExists, "Un verrou de destination existe déjà.", exception); }
        try
        {
            EnsureNewDestination(path);
            using (var connection = OpenConnection(candidate, readOnly: false))
            {
                VerifySnapshot(expected, ReadValidated(connection));
                MonetaBackup.MakeStandalone(connection);
            }
            EnsureNewDestination(path);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(candidate, path, overwrite: false);
        }
        finally
        {
            gate.Dispose();
            // A successful Moneta destination uses the ordinary persistent lock file.
            // On failure leave the lock if another process has already acquired it.
            if (!File.Exists(path))
                try { File.Delete(lockPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }
}
