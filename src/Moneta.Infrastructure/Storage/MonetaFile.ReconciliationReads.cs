using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string SessionColumns = "session_id,account_id,native_code,statement_date,statement_number,opening_minor,expected_minor,calculated_minor,previous_session_id,validated_utc,state,revision,reopened_utc,reopen_reason";
    private const string EventColumns = "event_id,session_id,transaction_id,kind,occurred_utc,reason,snapshot_version,snapshot_json";

    private static ReconciliationSession ReadSession(SqliteDataReader reader, LedgerLookup lookup)
    {
        var account = lookup.Accounts[ReadGuid(reader.GetString(1))];
        if (reader.GetString(2) != account.Currency.Code) throw Error(DocumentError.Corrupt, "Devise du relevé incohérente.");
        return new(ReadGuid(reader.GetString(0)), account.Id, ReadDate(reader.GetString(3)), reader.GetString(4),
            ExactMoney.FromMinorUnits(reader.GetInt64(5), account.Currency), ExactMoney.FromMinorUnits(reader.GetInt64(6), account.Currency),
            ExactMoney.FromMinorUnits(reader.GetInt64(7), account.Currency), reader.IsDBNull(8) ? null : ReadGuid(reader.GetString(8)),
            ReadTimestamp(reader.GetString(9)), (ReconciliationSessionState)checked((int)reader.GetInt64(10)), reader.GetInt64(11),
            reader.IsDBNull(12) ? null : ReadTimestamp(reader.GetString(12)), reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static ReconciliationSession? FindSession(SqliteConnection connection, Guid id, LedgerLookup lookup, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT {SessionColumns} FROM Reconciliations WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSession(reader, lookup) : null;
    }

    private static ReconciliationSession? LatestActiveSession(SqliteConnection connection, Guid accountId, LedgerLookup lookup, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT {SessionColumns} FROM Reconciliations WHERE account_id=$account AND state=0 ORDER BY statement_date DESC LIMIT 1;";
        command.Parameters.AddWithValue("$account", accountId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSession(reader, lookup) : null;
    }

    private static IEnumerable<ReconciliationEntry> StreamEntries(SqliteConnection connection, Guid sessionId, FinancialDocument metadata, LedgerLookup lookup, SqliteTransaction transaction, IncrementalHash? digest = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT transaction_id,snapshot_version,snapshot_json FROM ReconciliationEntries WHERE session_id=$id ORDER BY transaction_id COLLATE BINARY;";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt64(1) != 1) throw Error(DocumentError.Corrupt, "Version d’instantané inconnue.");
            var json = reader.GetString(2);
            var original = DecodeOperationSnapshot(json, metadata, lookup);
            if (original.Id != ReadGuid(reader.GetString(0))) throw Error(DocumentError.Corrupt, "Identité de l’instantané incohérente.");
            if (digest is not null)
            {
                HashValue(digest, "entry"); HashValue(digest, sessionId.ToString("D")); HashValue(digest, reader.GetString(0));
                HashValue(digest, "1"); HashValue(digest, json);
            }
            yield return new(sessionId, original);
        }
    }

    private static ReconciliationEvent ReadEvent(SqliteDataReader reader, FinancialDocument metadata, LedgerLookup lookup)
    {
        var value = new ReconciliationEvent(ReadGuid(reader.GetString(0)), reader.IsDBNull(1) ? null : ReadGuid(reader.GetString(1)),
            reader.IsDBNull(2) ? null : ReadGuid(reader.GetString(2)), (ReconciliationEventKind)checked((int)reader.GetInt64(3)),
            ReadTimestamp(reader.GetString(4)), reader.GetString(5));
        if (!reader.IsDBNull(6))
        {
            if (reader.GetInt64(6) != 1 || reader.IsDBNull(7)) throw Error(DocumentError.Corrupt, "Version d’audit inconnue.");
            value = value with { OperationBefore = DecodeOperationSnapshot(reader.GetString(7), metadata, lookup) };
        }
        else if (!reader.IsDBNull(7)) throw Error(DocumentError.Corrupt, "Version d’audit absente.");
        return value;
    }

    public IReadOnlyList<Transaction> ReadTransactions(IReadOnlyCollection<Guid> ids)
    {
        ObjectDisposedException.ThrowIf(closed, this); ArgumentNullException.ThrowIfNull(ids);
        var frozen = ids.ToArray();
        if (frozen.Any(id => id == Guid.Empty) || frozen.Distinct().Count() != frozen.Length) throw new ArgumentException("Identifiants absents ou dupliqués.", nameof(ids));
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var values = new List<Transaction>(frozen.Length);
            foreach (var chunk in frozen.Chunk(128))
            {
                using var command = Connection.CreateCommand(); command.Transaction = transaction;
                var names = chunk.Select((_, index) => "$id" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                command.CommandText = $"SELECT {TransactionColumns} FROM Transactions WHERE transaction_id IN ({string.Join(',', names)}) ORDER BY transaction_id COLLATE BINARY;";
                for (var index = 0; index < chunk.Length; index++) command.Parameters.AddWithValue(names[index], chunk[index].ToString("D"));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var value = ReadTransaction(reader, ledgerLookup); FinancialRules.ValidateTransaction(value, Snapshot); values.Add(value);
                }
            }
            if (values.Count != frozen.Length) throw Error(DocumentError.InvalidData, "Une opération sélectionnée n’existe plus.");
            transaction.Commit(); return values.AsReadOnly();
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    public ReconciliationCandidatePage ReadReconciliationCandidates(Account account, DateOnly statementDate, IReadOnlySet<Guid> selectedIds, int offset, int pageSize)
    {
        ObjectDisposedException.ThrowIf(closed, this); ArgumentNullException.ThrowIfNull(account); ArgumentNullException.ThrowIfNull(selectedIds);
        ValidatePage(offset, pageSize);
        if (!ledgerLookup.Accounts.TryGetValue(account.Id, out var stored) || stored != account || statementDate == default)
            throw new ArgumentException("Compte ou date de relevé invalide.");
        var selected = selectedIds.ToHashSet();
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var previous = LatestActiveSession(Connection, account.Id, ledgerLookup, transaction);
            long total = 0, selectedCount = 0, legacy = 0; decimal selectedNet = 0;
            var pageIds = new List<Guid>(pageSize);
            using (var command = Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT transaction_id,date,amount_minor,direction,native_code,status,reconciliation_id FROM Transactions WHERE account_id=$account AND is_deleted=0 ORDER BY date,created_utc,transaction_id COLLATE BINARY;";
                command.Parameters.AddWithValue("$account", account.Id.ToString("D"));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = ReadGuid(reader.GetString(0)); var date = ReadDate(reader.GetString(1));
                    var status = reader.GetInt64(5); var minor = reader.GetInt64(2); var direction = reader.GetInt64(3);
                    if (status is not (0 or 1 or 2) || minor < 0 || direction is not (0 or 1) || reader.GetString(4) != account.Currency.Code || (!reader.IsDBNull(6) && status != 2))
                        throw Error(DocumentError.Corrupt, "Candidat de rapprochement invalide.");
                    if (status == 2) { if (reader.IsDBNull(6)) legacy++; continue; }
                    if (date > statementDate) continue;
                    if (selected.Contains(id))
                    {
                        var amount = ExactMoney.FromMinorUnits(minor, account.Currency);
                        selectedNet = checked(selectedNet + (direction == 0 ? -amount : amount)); selectedCount++;
                    }
                    if (total >= offset && pageIds.Count < pageSize) pageIds.Add(id);
                    total++;
                }
            }
            if (selectedCount != selected.Count) throw Error(DocumentError.InvalidData, "La sélection contient une opération inconnue, supprimée, future, déjà rapprochée ou d’un autre compte.");
            var rows = pageIds.Select(id => FindTransaction(Connection, id, ledgerLookup, transaction) ?? throw Error(DocumentError.Corrupt, "Candidat introuvable.")).ToArray();
            foreach (var value in rows) FinancialRules.ValidateTransaction(value, Snapshot);
            transaction.Commit();
            return new(Array.AsReadOnly(rows), total, offset, previous?.ExpectedClosingBalance ?? account.OpeningBalance, selectedNet, selectedCount, previous?.Id, legacy);
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    public ReconciliationHistoryPage ReadReconciliationHistory(Guid accountId, int offset, int pageSize)
    {
        ObjectDisposedException.ThrowIf(closed, this); ValidatePage(offset, pageSize);
        if (!ledgerLookup.Accounts.ContainsKey(accountId)) throw new ArgumentException("Compte absent.", nameof(accountId));
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            using var command = Connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM Reconciliations WHERE account_id=$account;";
            command.Parameters.AddWithValue("$account", accountId.ToString("D"));
            var count = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            command.CommandText = $"SELECT {SessionColumns} FROM Reconciliations WHERE account_id=$account ORDER BY statement_date DESC,validated_utc DESC,session_id COLLATE BINARY LIMIT $size OFFSET $offset;";
            command.Parameters.AddWithValue("$size", pageSize); command.Parameters.AddWithValue("$offset", offset);
            var values = new List<ReconciliationSession>(pageSize);
            using (var reader = command.ExecuteReader()) while (reader.Read()) values.Add(ReadSession(reader, ledgerLookup));
            transaction.Commit(); return new(values.AsReadOnly(), count, offset);
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    public ReconciliationDetails ReadReconciliation(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var session = FindSession(Connection, sessionId, ledgerLookup, transaction) ?? throw Error(DocumentError.InvalidData, "Rapprochement absent.");
            var entries = StreamEntries(Connection, sessionId, Snapshot, ledgerLookup, transaction).ToArray();
            var events = new List<ReconciliationEvent>();
            using (var command = Connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandText = $"SELECT {EventColumns} FROM ReconciliationHistory WHERE session_id=$id ORDER BY occurred_utc,event_id COLLATE BINARY;";
                command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
                using var reader = command.ExecuteReader(); while (reader.Read()) events.Add(ReadEvent(reader, Snapshot, ledgerLookup));
            }
            transaction.Commit(); return new(session, Array.AsReadOnly(entries), events.AsReadOnly());
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    private static void ValidatePage(int offset, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
    }
}
