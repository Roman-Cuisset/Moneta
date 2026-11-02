using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string MarkedOnColumnSql = "marked_on TEXT CHECK(marked_on IS NULL OR (status<>0 AND length(marked_on)=10 AND marked_on BETWEEN '0001-01-01' AND '9999-12-31' AND date(marked_on,'+0 days') IS NOT NULL AND date(marked_on,'+0 days')=marked_on))";
    private const string ReconciliationIdColumnSql = "reconciliation_id TEXT REFERENCES Reconciliations(session_id) CHECK(reconciliation_id IS NULL OR (status=2 AND is_deleted=0))";
    // ALTER TABLE appends these exact column definitions. The original v3 DDL is intentionally immutable.
    private static string TransactionsSql4 => TransactionsSql.Replace("revision INTEGER NOT NULL CHECK(revision>=1)",
        "revision INTEGER NOT NULL CHECK(revision>=1), " + MarkedOnColumnSql + ", " + ReconciliationIdColumnSql, StringComparison.Ordinal);
    private const string ReconciliationsSql = """
        CREATE TABLE Reconciliations (
            session_id TEXT NOT NULL PRIMARY KEY CHECK(length(session_id)=36 AND session_id=lower(session_id)),
            account_id TEXT NOT NULL REFERENCES Accounts(account_id),
            native_code TEXT NOT NULL REFERENCES Currencies(code),
            statement_date TEXT NOT NULL CHECK(length(statement_date)=10 AND statement_date BETWEEN '0001-01-01' AND '9999-12-31' AND date(statement_date,'+0 days') IS NOT NULL AND date(statement_date,'+0 days')=statement_date),
            statement_number TEXT NOT NULL CHECK(length(statement_number)<=120),
            opening_minor INTEGER NOT NULL CHECK(opening_minor>=-9223372036854775807),
            expected_minor INTEGER NOT NULL CHECK(expected_minor>=-9223372036854775807),
            calculated_minor INTEGER NOT NULL CHECK(calculated_minor>=-9223372036854775807 AND calculated_minor=expected_minor),
            previous_session_id TEXT REFERENCES Reconciliations(session_id),
            validated_utc TEXT NOT NULL,
            state INTEGER NOT NULL CHECK(state IN (0,1)),
            revision INTEGER NOT NULL,
            reopened_utc TEXT,
            reopen_reason TEXT,
            FOREIGN KEY(account_id,native_code) REFERENCES Accounts(account_id,native_code),
            CHECK(previous_session_id IS NULL OR previous_session_id<>session_id),
            CHECK((state=0 AND revision=1 AND reopened_utc IS NULL AND reopen_reason IS NULL) OR (state=1 AND revision=2 AND reopened_utc IS NOT NULL AND length(trim(reopen_reason)) BETWEEN 1 AND 1000))
        ) STRICT
        """;
    private const string ReconciliationEntriesSql = """
        CREATE TABLE ReconciliationEntries (
            session_id TEXT NOT NULL REFERENCES Reconciliations(session_id),
            transaction_id TEXT NOT NULL REFERENCES Transactions(transaction_id),
            snapshot_version INTEGER NOT NULL CHECK(snapshot_version=1),
            snapshot_json TEXT NOT NULL CHECK(length(snapshot_json)>0 AND json_valid(snapshot_json)),
            PRIMARY KEY(session_id,transaction_id)
        ) STRICT
        """;
    private const string ReconciliationHistorySql = """
        CREATE TABLE ReconciliationHistory (
            event_id TEXT NOT NULL PRIMARY KEY CHECK(length(event_id)=36 AND event_id=lower(event_id)),
            session_id TEXT REFERENCES Reconciliations(session_id),
            transaction_id TEXT REFERENCES Transactions(transaction_id),
            kind INTEGER NOT NULL CHECK(kind IN (0,1,2)),
            occurred_utc TEXT NOT NULL,
            reason TEXT NOT NULL CHECK(length(reason)<=1000),
            snapshot_version INTEGER,
            snapshot_json TEXT CHECK(snapshot_json IS NULL OR json_valid(snapshot_json)),
            CHECK((kind IN (0,1) AND session_id IS NOT NULL AND transaction_id IS NULL AND snapshot_version IS NULL AND snapshot_json IS NULL) OR (kind=2 AND session_id IS NULL AND transaction_id IS NOT NULL AND length(trim(reason))>0 AND snapshot_version=1 AND snapshot_json IS NOT NULL))
        ) STRICT
        """;
    private static readonly IReadOnlyDictionary<string, string> ReconciliationTables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Reconciliations"] = ReconciliationsSql,
        ["ReconciliationEntries"] = ReconciliationEntriesSql,
        ["ReconciliationHistory"] = ReconciliationHistorySql
    };
    private static readonly Dictionary<string, (string Table, string Sql)> ReconciliationIndexes = new(StringComparer.Ordinal)
    {
        ["Reconciliations_AccountDate"] = ("Reconciliations", "CREATE INDEX Reconciliations_AccountDate ON Reconciliations(account_id,statement_date,validated_utc,session_id)"),
        ["Reconciliations_Previous"] = ("Reconciliations", "CREATE INDEX Reconciliations_Previous ON Reconciliations(account_id,previous_session_id,validated_utc,reopened_utc)"),
        ["Reconciliations_ActiveDate"] = ("Reconciliations", "CREATE UNIQUE INDEX Reconciliations_ActiveDate ON Reconciliations(account_id,statement_date) WHERE state=0"),
        ["ReconciliationEntries_Transaction"] = ("ReconciliationEntries", "CREATE INDEX ReconciliationEntries_Transaction ON ReconciliationEntries(transaction_id,session_id)"),
        ["ReconciliationHistory_Session"] = ("ReconciliationHistory", "CREATE INDEX ReconciliationHistory_Session ON ReconciliationHistory(session_id,kind)"),
        ["ReconciliationHistory_Transaction"] = ("ReconciliationHistory", "CREATE INDEX ReconciliationHistory_Transaction ON ReconciliationHistory(transaction_id,kind)"),
        ["Transactions_Reconciliation"] = ("Transactions", "CREATE INDEX Transactions_Reconciliation ON Transactions(reconciliation_id,transaction_id)"),
        ["Transactions_Candidates"] = ("Transactions", "CREATE INDEX Transactions_Candidates ON Transactions(account_id,is_deleted,status,date,created_utc,transaction_id)")
    };
    private static string ReconciliationIndexSql => string.Join(";\n", ReconciliationIndexes.Values.Select(value => value.Sql)) + ";";
    private static string ReconciliationSchemaSql => string.Join(";\n", ReconciliationTables.Values) + ";" + ReconciliationIndexSql;
    private static string LedgerSchemaSql4 => string.Join(";\n", LedgerTables.Select(pair => pair.Key == "Transactions" ? TransactionsSql4 : pair.Value).Concat(LedgerIndexes.Values.Select(value => value.Sql))) + ";" + ReconciliationSchemaSql;

    internal static void UpgradeV3(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 3) throw Error(DocumentError.UnsupportedVersion, "Seul un dossier v3 peut recevoir le rapprochement v4.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, string.Join(";\n", ReconciliationTables.Values) + ";ALTER TABLE Transactions ADD COLUMN " + MarkedOnColumnSql + "; ALTER TABLE Transactions ADD COLUMN " + ReconciliationIdColumnSql + ";" + ReconciliationIndexSql + "UPDATE Document SET schema_version=4,min_reader=4,min_writer=4; PRAGMA user_version=4;", transaction);
        InsertHistory(connection, transaction, 4, 3, "v3-to-v4-reconciliation", "pointing-and-statement-history-v4");
        var migrated = ReadWithinTransaction(connection, true, transaction);
        var legacy = ReadLedgerMetadata(connection, migrated with { SchemaVersion = 3, ReconciliationCount = 0, ReconciliationEventCount = 0, AccountPointedBalances = null, TransferCount = 0 }, transaction);
        if (!SameSnapshot(original, legacy)) throw Error(DocumentError.VerificationFailed, "La migration a modifié le registre historique.");
        transaction.Commit();
    }

    internal static FinancialDocument ReadLegacyV3Representation(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var current = ReadWithinTransaction(connection, true, transaction);
        if (current.SchemaVersion < 3) throw Error(DocumentError.UnsupportedVersion, "Un registre v3 ou v4 est requis.");
        var result = current.SchemaVersion == 3 ? current : ReadLedgerMetadata(connection, current with { SchemaVersion = 3, ReconciliationCount = 0, ReconciliationEventCount = 0, AccountPointedBalances = null, TransferCount = 0 }, transaction);
        transaction.Commit();
        return Freeze(result);
    }
}
