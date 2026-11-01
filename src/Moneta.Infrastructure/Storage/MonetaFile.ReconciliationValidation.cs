using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private static (long Sessions, long Events) ValidateAndHashReconciliations(SqliteConnection connection,
        FinancialDocument metadata, LedgerLookup lookup, IncrementalHash digest, SqliteTransaction transaction)
    {
        long sessionCount = 0, eventCount = 0;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = $"SELECT {SessionColumns} FROM Reconciliations ORDER BY session_id COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var session = ReadSession(reader, lookup); var account = lookup.Accounts[session.AccountId];
                var previous = session.PreviousSessionId is { } previousId ? FindSession(connection, previousId, lookup, transaction) : null;
                // Validate headers with an empty, balanced projection; the actual exact sum is streamed below.
                ReconciliationRules.ValidateSession(session with { ExpectedClosingBalance = session.OpeningBalance, CalculatedBalance = session.OpeningBalance }, account, previous, Array.Empty<ReconciliationEntry>());
                if (previous is not null && (previous.ValidatedUtc > session.ValidatedUtc ||
                    (previous.ReopenedUtc is { } previousEnd && (previousEnd < session.ValidatedUtc ||
                    session.ReopenedUtc is null || session.ReopenedUtc > previousEnd))))
                    throw new ArgumentException("Chronologie ou ordre de réouverture des relevés invalide.");
                var calculated = session.OpeningBalance;
                HashReader(digest, "session", reader);
                foreach (var entry in StreamEntries(connection, session.Id, metadata, lookup, transaction, digest))
                {
                    var original = entry.Original;
                    if (original.AccountId != session.AccountId || original.Date > session.StatementDate || original.IsDeleted ||
                        original.Status is not (ReconciliationStatus.Unmarked or ReconciliationStatus.Marked) || original.ReconciliationId is not null ||
                        original.ModifiedUtc > session.ValidatedUtc)
                        throw new ArgumentException("Membre du relevé inéligible.");
                    calculated = checked(calculated + FinancialRules.SignedAmount(original));
                    var current = FindTransaction(connection, original.Id, lookup, transaction) ?? throw new ArgumentException("Membre du relevé absent.");
                    if (current.AccountId != session.AccountId || current.CreatedUtc != original.CreatedUtc ||
                        current.Revision <= original.Revision || current.ModifiedUtc < original.ModifiedUtc)
                        throw new ArgumentException("Identité ou révision historique incohérente.");
                    if (session.State == ReconciliationSessionState.Validated &&
                        (!ReconciliationRules.FinancialPayloadEquals(current, original) || current.Status != ReconciliationStatus.Reconciled ||
                        current.ReconciliationId != session.Id || current.Revision != checked(original.Revision + 1) || current.MarkedOn is null ||
                        (original.MarkedOn is not null && current.MarkedOn != original.MarkedOn)))
                        throw new ArgumentException("Le contenu actif ne correspond pas à son instantané de rapprochement.");
                    if (session.State == ReconciliationSessionState.Reopened && current.Revision < checked(original.Revision + 2))
                        throw new ArgumentException("Révision de réouverture absente.");
                }
                if (calculated != session.CalculatedBalance || calculated != session.ExpectedClosingBalance)
                    throw new ArgumentException("Écart historique non nul.");
                using var audits = connection.CreateCommand(); audits.Transaction = transaction;
                audits.CommandText = "SELECT kind,COUNT(*) FROM ReconciliationHistory WHERE session_id=$id GROUP BY kind;";
                audits.Parameters.AddWithValue("$id", session.Id.ToString("D"));
                long validated = 0, reopened = 0;
                using (var auditReader = audits.ExecuteReader())
                {
                    while (auditReader.Read())
                    {
                        if (auditReader.GetInt64(0) == 0) validated = auditReader.GetInt64(1);
                        else if (auditReader.GetInt64(0) == 1) reopened = auditReader.GetInt64(1);
                        else throw new ArgumentException("Audit de relevé incompatible.");
                    }
                }
                if (validated != 1 || reopened != (session.State == ReconciliationSessionState.Reopened ? 1 : 0))
                    throw new ArgumentException("Historique de validation ou de réouverture incomplet.");
                sessionCount++;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = $"SELECT {EventColumns} FROM ReconciliationHistory ORDER BY event_id COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var audit = ReadEvent(reader, metadata, lookup); ValidateAuditShape(audit, metadata, lookup);
                if (audit.SessionId is { } id)
                {
                    var session = FindSession(connection, id, lookup, transaction) ?? throw new ArgumentException("Relevé de l’audit absent.");
                    if (audit.Kind == ReconciliationEventKind.Validated ? audit.OccurredUtc != session.ValidatedUtc :
                        session.State != ReconciliationSessionState.Reopened || audit.OccurredUtc != session.ReopenedUtc || audit.Reason != session.ReopenReason)
                        throw new ArgumentException("Audit de relevé incohérent.");
                }
                else
                {
                    var before = audit.OperationBefore!;
                    var current = FindTransaction(connection, before.Id, lookup, transaction) ?? throw new ArgumentException("Opération libérée absente.");
                    if (current.AccountId != before.AccountId || current.CreatedUtc != before.CreatedUtc ||
                        current.Revision <= before.Revision || current.ModifiedUtc < audit.OccurredUtc)
                        throw new ArgumentException("Audit de libération incohérent.");
                    using var duplicate = connection.CreateCommand(); duplicate.Transaction = transaction;
                    duplicate.CommandText = "SELECT COUNT(*) FROM ReconciliationHistory WHERE transaction_id=$id AND kind=2;";
                    duplicate.Parameters.AddWithValue("$id", before.Id.ToString("D"));
                    if ((long)duplicate.ExecuteScalar()! != 1) throw new ArgumentException("Libération héritée dupliquée.");
                }
                HashReader(digest, "event", reader); eventCount++;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM Transactions t LEFT JOIN Reconciliations r ON r.session_id=t.reconciliation_id LEFT JOIN ReconciliationEntries e ON e.session_id=t.reconciliation_id AND e.transaction_id=t.transaction_id WHERE t.reconciliation_id IS NOT NULL AND (t.status<>2 OR t.is_deleted<>0 OR r.state<>0 OR r.account_id<>t.account_id OR e.transaction_id IS NULL) LIMIT 1;";
            if (command.ExecuteScalar() is not null) throw new ArgumentException("Référence de rapprochement sans appartenance active valide.");
            command.CommandText = "SELECT 1 FROM Transactions t WHERE t.status=2 AND t.is_deleted=0 AND t.reconciliation_id IS NULL AND EXISTS(SELECT 1 FROM Reconciliations r WHERE r.account_id=t.account_id) LIMIT 1;";
            if (command.ExecuteScalar() is not null) throw new ArgumentException("Rapprochement hérité actif incompatible avec un historique de relevés.");
        }
        // Stream sibling lifetimes in order; parent lifetime/date containment above then
        // proves a single active chain. Memory is constant even for repeated historical roots.
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT account_id,previous_session_id,validated_utc,reopened_utc FROM Reconciliations ORDER BY account_id COLLATE BINARY,previous_session_id COLLATE BINARY,validated_utc,CASE WHEN reopened_utc=validated_utc THEN 0 ELSE 1 END,session_id COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            string? lastAccount = null, lastPrevious = null;
            DateTimeOffset? lastEnd = null;
            while (reader.Read())
            {
                var account = reader.GetString(0); var previous = reader.IsDBNull(1) ? null : reader.GetString(1);
                var start = ReadTimestamp(reader.GetString(2));
                if (account == lastAccount && previous == lastPrevious && (lastEnd is null || start < lastEnd))
                    throw new ArgumentException("Fourche dans la chaîne historique des relevés.");
                lastAccount = account; lastPrevious = previous;
                lastEnd = reader.IsDBNull(3) ? null : ReadTimestamp(reader.GetString(3));
            }
        }
        return (sessionCount, eventCount);
    }

    private static void HashReader(IncrementalHash digest, string kind, SqliteDataReader reader)
    {
        HashValue(digest, kind);
        for (var index = 0; index < reader.FieldCount; index++)
            HashValue(digest, reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
    }
}
