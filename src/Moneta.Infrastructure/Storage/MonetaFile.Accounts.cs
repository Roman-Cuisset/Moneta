using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    internal static void UpgradeV5(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 5) throw Error(DocumentError.UnsupportedVersion, "Le dossier source doit être en version 5.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            ALTER TABLE Accounts ADD COLUMN bank_name TEXT;
            ALTER TABLE Accounts ADD COLUMN bank_reference TEXT;
            ALTER TABLE Accounts ADD COLUMN holder TEXT;
            UPDATE Document SET schema_version=6,min_reader=6,min_writer=6;
            PRAGMA user_version=6;
            """, transaction);
        InsertHistory(connection, transaction, 6, 5, "v5-to-v6-account-properties", "account-descriptions-v6");
        var migrated = ReadWithinTransaction(connection, false, transaction);
        VerifySnapshot(original with { SchemaVersion = CurrentSchemaVersion }, migrated);
        transaction.Commit();
    }

    public bool HasAccountHistory(Guid accountId)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        using var transaction = Connection.BeginTransaction(deferred: true);
        var account = Snapshot.Accounts.SingleOrDefault(account => account.Id == accountId)
            ?? throw Error(DocumentError.InvalidData, "Compte absent du dossier enregistré.");
        var result = account.OpeningBalance != 0 || HasLedgerHistory(accountId, transaction) ||
            ImportedAccountIds(Snapshot, transaction).Contains(accountId);
        transaction.Commit();
        return result;
    }

    public Guid? FindAccountOperation(Guid accountId, string query, Guid? afterId)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        using var command = Connection.CreateCommand();
        command.CommandText = """
            SELECT t.transaction_id,t.label,t.notes,COALESCE(p.name,'')
            FROM Transactions t LEFT JOIN Payees p ON p.payee_id=t.payee_id
            WHERE t.account_id=$id AND t.is_deleted=0
            ORDER BY t.date,t.created_utc,t.transaction_id;
            """;
        command.Parameters.AddWithValue("$id", accountId.ToString("D"));
        using var reader = command.ExecuteReader();
        Guid? first = null;
        var after = afterId is null;
        while (reader.Read())
        {
            var id = ReadGuid(reader.GetString(0));
            if (reader.GetString(1).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                reader.GetString(2).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                reader.GetString(3).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (after) return id;
                first ??= id;
            }
            if (id == afterId) after = true;
        }
        return first;
    }

    private void ValidateAndRemoveAccounts(FinancialDocument persisted, Dictionary<Guid, Account> target,
        IReadOnlyCollection<TransactionChange> changes, ReconciliationBatch reconciliation, SqliteTransaction transaction)
    {
        HashSet<Guid>? imported = null;
        foreach (var previous in persisted.Accounts)
        {
            if (target.ContainsKey(previous.Id)) continue;
            if (previous.OpeningBalance != 0 || HasLedgerHistory(previous.Id, transaction) ||
                (imported ??= ImportedAccountIds(persisted, transaction)).Contains(previous.Id) ||
                changes.Any(change => change.Value.AccountId == previous.Id) ||
                reconciliation.Sessions.Any(change => change.Value.AccountId == previous.Id) ||
                reconciliation.Events.Any(change => change.OperationBefore?.AccountId == previous.Id))
                throw Error(DocumentError.InvalidData, "Le compte a un solde initial ou un historique. Archivez-le pour le conserver.");

            using var command = Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Accounts WHERE account_id=$id;";
            command.Parameters.AddWithValue("$id", previous.Id.ToString("D"));
            if (command.ExecuteNonQuery() != 1)
                throw Error(DocumentError.VerificationFailed, "Le compte à supprimer a changé.");
        }
    }

    private bool HasLedgerHistory(Guid accountId, SqliteTransaction transaction)
    {
        // Deliberately includes tombstones. Transfer legs, including fees, all refer to transactions.
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM Transactions WHERE account_id=$id) OR
                   EXISTS(SELECT 1 FROM Reconciliations WHERE account_id=$id);
            """;
        command.Parameters.AddWithValue("$id", accountId.ToString("D"));
        return (long)command.ExecuteScalar()! != 0;
    }

    private HashSet<Guid> ImportedAccountIds(FinancialDocument persisted, SqliteTransaction transaction)
    {
        if (persisted.BankPerfectProvenanceHash is not { } expected) return [];
        if (VerifyBankPerfectProvenance(Connection, transaction) != expected)
            throw Error(DocumentError.VerificationFailed, "Le rapport d’import a changé.");
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload FROM BankPerfectProvenance WHERE singleton=1;";
        try
        {
            using var audit = JsonDocument.Parse((byte[])command.ExecuteScalar()!);
            var root = audit.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("documentId").GetGuid() != persisted.Id)
                throw new FormatException();
            var ids = root.GetProperty("objects").EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "account")
                .Select(item => item.GetProperty("targetId").GetGuid()).ToHashSet();
            if (ids.Count == 0 || !ids.IsSubsetOf(persisted.Accounts.Select(account => account.Id)))
                throw new FormatException();
            return ids;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw new DocumentException(DocumentError.VerificationFailed, "Les comptes protégés par la provenance ne peuvent pas être déterminés.", error);
        }
    }
}
