using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string TransfersSql = """
        CREATE TABLE Transfers (
            transfer_id TEXT NOT NULL PRIMARY KEY CHECK(length(transfer_id)=36 AND transfer_id=lower(transfer_id)),
            date TEXT NOT NULL CHECK(length(date)=10 AND date BETWEEN '0001-01-01' AND '9999-12-31' AND date(date,'+0 days') IS NOT NULL AND date(date,'+0 days')=date),
            reference TEXT NOT NULL CHECK(length(reference)<=120),
            notes TEXT NOT NULL CHECK(length(notes)<=2000),
            exchange_date TEXT CHECK(exchange_date IS NULL OR (length(exchange_date)=10 AND exchange_date BETWEEN '0001-01-01' AND '9999-12-31' AND date(exchange_date,'+0 days') IS NOT NULL AND date(exchange_date,'+0 days')=exchange_date)),
            rate_source TEXT,
            created_utc TEXT NOT NULL,
            modified_utc TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK(revision>=1),
            is_deleted INTEGER NOT NULL CHECK(is_deleted IN (0,1))
        ) STRICT
        """;
    private const string TransferLegsSql = """
        CREATE TABLE TransferLegs (
            transfer_id TEXT NOT NULL REFERENCES Transfers(transfer_id),
            role INTEGER NOT NULL CHECK(role IN (0,1,2)),
            transaction_id TEXT NOT NULL UNIQUE REFERENCES Transactions(transaction_id),
            PRIMARY KEY(transfer_id,role)
        ) STRICT
        """;
    // The UNIQUE transaction_id and composite primary key supply both lookup indexes.
    private static readonly IReadOnlyDictionary<string, string> TransferTables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Transfers"] = TransfersSql, ["TransferLegs"] = TransferLegsSql
    };
    private static string TransferSchemaSql => TransfersSql + ";" + TransferLegsSql + ";";

    internal static void UpgradeV4(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 4) throw Error(DocumentError.UnsupportedVersion, "Seul un dossier v4 peut recevoir les virements v5.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, TransferSchemaSql + "UPDATE Document SET schema_version=5,min_reader=5,min_writer=5; PRAGMA user_version=5;", transaction);
        InsertHistory(connection, transaction, 5, 4, "v4-to-v5-transfers", "linked-transfer-legs-v5");
        var migrated = ReadWithinTransaction(connection, false, transaction);
        var legacy = ReadLedgerMetadata(connection, migrated with { SchemaVersion = 4, TransferCount = 0 }, transaction);
        if (!SameSnapshot(original, legacy)) throw Error(DocumentError.VerificationFailed, "La migration a modifié le registre ou son historique de rapprochement.");
        transaction.Commit();
    }

    internal static FinancialDocument ReadLegacyV4Representation(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var current = ReadWithinTransaction(connection, true, transaction);
        if (current.SchemaVersion < 4) throw Error(DocumentError.UnsupportedVersion, "Un registre v4 ou v5 est requis.");
        var result = current.SchemaVersion == 4 ? current : ReadLedgerMetadata(connection, current with { SchemaVersion = 4, TransferCount = 0 }, transaction);
        transaction.Commit();
        return Freeze(result);
    }
}
