using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private static void ValidateReconciliationChanges(SqliteConnection connection, FinancialDocument metadata,
        IReadOnlyCollection<TransactionChange> changes, ReconciliationBatch batch, SqliteTransaction transaction, bool creating, FinancialDocument? baseline = null)
    {
        try
        {
            var lookup = new LedgerLookup(metadata);
            var baselineLookup = baseline is null ? lookup : new LedgerLookup(baseline);
            var proposed = new Dictionary<Guid, TransactionChange>();
            var originals = new Dictionary<Guid, Transaction?>();
            foreach (var change in changes)
            {
                if (change?.Value is not { } value || !proposed.TryAdd(value.Id, change) || change.ExpectedRevision < 0 ||
                    (creating && change.ExpectedRevision != 0) || value.Revision != checked(change.ExpectedRevision + 1))
                    throw new ArgumentException("Lot de révisions invalide.");
                FinancialRules.ValidateTransaction(value, metadata);
                var original = FindTransaction(connection, value.Id, baselineLookup, transaction); originals.Add(value.Id, original);
                if (change.ExpectedRevision == 0)
                {
                    if (original is not null || value.IsDeleted) throw new ArgumentException("Insertion déjà présente ou supprimée.");
                }
                else if (original is null || original.IsDeleted || original.Revision != change.ExpectedRevision ||
                    original.AccountId != value.AccountId || original.Currency.Code != value.Currency.Code ||
                    original.CreatedUtc != value.CreatedUtc || value.ModifiedUtc < original.ModifiedUtc)
                    throw new ArgumentException("Révision obsolète ou identité financière modifiée.");
                if (lookup.Accounts[value.AccountId].IsArchived) throw new ArgumentException("Compte archivé.");
            }
            foreach (var account in metadata.Accounts)
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "SELECT a.opening_minor,a.native_code FROM Accounts a WHERE a.account_id=$id AND EXISTS(SELECT 1 FROM Reconciliations r WHERE r.account_id=a.account_id);";
                command.Parameters.AddWithValue("$id", account.Id.ToString("D"));
                using var reader = command.ExecuteReader();
                if (reader.Read() && (reader.GetInt64(0) != ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency) || reader.GetString(1) != account.Currency.Code))
                    throw new ArgumentException("Le solde initial et la devise sont immuables après un premier relevé.");
            }
            var events = new Dictionary<Guid, ReconciliationEvent>();
            foreach (var audit in batch.Events)
            {
                ValidateAuditShape(audit, metadata, lookup);
                if (!events.TryAdd(audit.Id, audit)) throw new ArgumentException("Événement dupliqué.");
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "SELECT 1 FROM ReconciliationHistory WHERE event_id=$id;"; command.Parameters.AddWithValue("$id", audit.Id.ToString("D"));
                if (command.ExecuteScalar() is not null) throw new ArgumentException("Événement déjà enregistré.");
            }
            var consumedEvents = new HashSet<Guid>(); var authorized = new HashSet<Guid>(); var sessions = new Dictionary<Guid, ReconciliationSession>();
            var latest = new Dictionary<Guid, ReconciliationSession?>();
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var change in batch.Sessions)
            {
                var session = change.Value;
                if (!sessions.TryAdd(session.Id, session)) throw new ArgumentException("Relevé dupliqué.");
                var account = lookup.Accounts[session.AccountId];
                if (account.IsArchived) throw new ArgumentException("Compte archivé.");
                if (!latest.TryGetValue(account.Id, out var previous)) previous = LatestActiveSession(connection, account.Id, lookup, transaction);
                var stored = FindSession(connection, session.Id, lookup, transaction);
                ReconciliationEvent RequiredEvent(ReconciliationEventKind kind, DateTimeOffset occurred, string reason)
                {
                    var matching = events.Values.Where(value => value.SessionId == session.Id && value.Kind == kind).ToArray();
                    if (matching.Length != 1 || matching[0].OccurredUtc != occurred || matching[0].Reason != reason || !consumedEvents.Add(matching[0].Id))
                        throw new ArgumentException("Événement du relevé absent ou incohérent.");
                    return matching[0];
                }
                if (change.ExpectedRevision == 0)
                {
                    if (creating || stored is not null || session.State != ReconciliationSessionState.Validated || session.StatementDate > today ||
                        (previous is not null && session.ValidatedUtc < previous.ValidatedUtc))
                        throw new ArgumentException("Nouveau relevé invalide ou déjà présent.");
                    if (CountLegacyRows(connection, account.Id, transaction) != 0) throw new ArgumentException("Libérez explicitement les rapprochements hérités avant de valider un relevé.");
                    ReconciliationRules.ValidateSession(session, account, previous, change.Entries);
                    RequiredEvent(ReconciliationEventKind.Validated, session.ValidatedUtc, string.Empty);
                    // Staging fixes the local marking date. UTC±14 can differ from the validation UTC date by one
                    // calendar day; saving later or in another time zone must neither replace nor invalidate it.
                    var validationDate = DateOnly.FromDateTime(session.ValidatedUtc.UtcDateTime);
                    foreach (var entry in change.Entries)
                    {
                        var original = entry.Original;
                        if (!proposed.TryGetValue(original.Id, out var patch) || originals[original.Id] is not { } current ||
                            current != original || !authorized.Add(current.Id) ||
                            !ReconciliationRules.FinancialPayloadEquals(current, patch.Value) || patch.Value.Status != ReconciliationStatus.Reconciled ||
                            patch.Value.ReconciliationId != session.Id || patch.Value.MarkedOn is not { } markedOn ||
                            (current.MarkedOn is { } existingMarkedOn ? markedOn != existingMarkedOn :
                                Math.Abs(markedOn.DayNumber - validationDate.DayNumber) > 1) ||
                            current.ModifiedUtc > session.ValidatedUtc)
                            throw new ArgumentException("Instantané, sélection ou changement de rapprochement obsolète.");
                        ValidateHistoricalOperation(original, metadata, lookup);
                    }
                    latest[account.Id] = session;
                }
                else
                {
                    if (stored is null || stored.State != ReconciliationSessionState.Validated || stored.Revision != change.ExpectedRevision ||
                        previous?.Id != stored.Id || change.Entries.Count != 0 || session.State != ReconciliationSessionState.Reopened ||
                        session.Revision != checked(stored.Revision + 1) || session.ReopenedUtc is not { } reopened || reopened < stored.ValidatedUtc ||
                        session != (stored with { State = ReconciliationSessionState.Reopened, Revision = 2, ReopenedUtc = session.ReopenedUtc, ReopenReason = session.ReopenReason }))
                        throw new ArgumentException("Seul le dernier relevé actif peut être rouvert, sans réécrire son contenu.");
                    ReconciliationRules.ValidateReason(session.ReopenReason!);
                    RequiredEvent(ReconciliationEventKind.Reopened, reopened, session.ReopenReason!);
                    foreach (var entry in StreamEntries(connection, stored.Id, metadata, lookup, transaction))
                    {
                        if (!proposed.TryGetValue(entry.Original.Id, out var patch) || originals[entry.Original.Id] is not { } current ||
                            current.Status != ReconciliationStatus.Reconciled || current.ReconciliationId != stored.Id || !authorized.Add(current.Id) ||
                            !ReconciliationRules.FinancialPayloadEquals(current, entry.Original) || !ReconciliationRules.FinancialPayloadEquals(current, patch.Value) ||
                            patch.Value.Status != entry.Original.Status || patch.Value.MarkedOn != entry.Original.MarkedOn || patch.Value.ReconciliationId is not null)
                            throw new ArgumentException("La réouverture exige chaque opération intacte et son état de pointage original.");
                    }
                    latest[account.Id] = stored.PreviousSessionId is { } previousId ? FindSession(connection, previousId, lookup, transaction) : null;
                }
            }
            foreach (var audit in events.Values.Where(value => value.Kind == ReconciliationEventKind.LegacyReleased))
            {
                var id = audit.TransactionId!.Value;
                if (!proposed.TryGetValue(id, out var patch) || originals[id] is not { } current || current.IsDeleted ||
                    current.Status != ReconciliationStatus.Reconciled || current.ReconciliationId is not null ||
                    audit.OperationBefore is not { } before || current != before ||
                    !ReconciliationRules.FinancialPayloadEquals(current, patch.Value) || patch.Value.Status != ReconciliationStatus.Unmarked ||
                    patch.Value.MarkedOn is not null || patch.Value.ReconciliationId is not null || !authorized.Add(id) ||
                    audit.OccurredUtc != patch.Value.ModifiedUtc)
                    throw new ArgumentException("Libération héritée invalide ou instantané obsolète.");
                consumedEvents.Add(audit.Id);
            }
            if (consumedEvents.Count != events.Count) throw new ArgumentException("Événement sans changement explicite correspondant.");
            foreach (var patch in proposed.Values)
            {
                var value = patch.Value; var original = originals[value.Id];
                if (authorized.Contains(value.Id)) continue;
                if (original?.Status == ReconciliationStatus.Reconciled || value.ReconciliationId is not null ||
                    (value.Status == ReconciliationStatus.Reconciled && !(creating && original is null)))
                    throw new ArgumentException("Une opération rapprochée est protégée : réouverture ou libération héritée explicite requise.");
                if (!creating && (original is null || value.Status != original.Status || value.MarkedOn != original.MarkedOn))
                {
                    if (value.Status == ReconciliationStatus.Marked && (value.MarkedOn is not { } marked || marked > today ||
                        ((original is null || original.Status == ReconciliationStatus.Unmarked) && value.Date > today)))
                        throw new ArgumentException("Une nouvelle date de pointage doit être connue, non future, et ne peut pointer une opération future.");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException or NullReferenceException)
        {
            throw new DocumentException(DocumentError.InvalidData, "Rapprochement ou modification invalide : " + exception.Message, exception);
        }
    }

    private static long CountLegacyRows(SqliteConnection connection, Guid accountId, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Transactions WHERE account_id=$id AND is_deleted=0 AND status=2 AND reconciliation_id IS NULL;";
        command.Parameters.AddWithValue("$id", accountId.ToString("D")); return (long)command.ExecuteScalar()!;
    }

    private static void ValidateAuditShape(ReconciliationEvent value, FinancialDocument metadata, LedgerLookup lookup)
    {
        if (value.Id == Guid.Empty || value.SessionId == Guid.Empty || value.TransactionId == Guid.Empty || !Enum.IsDefined(value.Kind) ||
            value.OccurredUtc == default || value.OccurredUtc.Offset != TimeSpan.Zero || value.Reason is null || value.Reason.Length > 1000)
            throw new ArgumentException("Événement invalide.");
        if (value.Kind == ReconciliationEventKind.LegacyReleased)
        {
            ReconciliationRules.ValidateReason(value.Reason);
            if (value.SessionId is not null || value.TransactionId is null || value.OperationBefore is not { } before ||
                before.Id != value.TransactionId || before.Status != ReconciliationStatus.Reconciled || before.ReconciliationId is not null || before.IsDeleted ||
                before.ModifiedUtc > value.OccurredUtc) throw new ArgumentException("Audit de libération invalide.");
            ValidateHistoricalOperation(before, metadata, lookup);
        }
        else if (value.SessionId is null || value.TransactionId is not null || value.OperationBefore is not null ||
            (value.Kind == ReconciliationEventKind.Validated ? value.Reason.Length != 0 : string.IsNullOrWhiteSpace(value.Reason)))
            throw new ArgumentException("Audit de relevé invalide.");
    }

    private static void WriteReconciliationChanges(SqliteConnection connection, FinancialDocument metadata, ReconciliationBatch batch, SqliteTransaction transaction)
    {
        foreach (var change in batch.Sessions)
        {
            var value = change.Value; var account = metadata.Accounts.Single(account => account.Id == value.AccountId);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO Reconciliations(" + SessionColumns + ") VALUES($id,$account,$currency,$date,$number,$opening,$expected,$calculated,$previous,$validated,$state,$revision,$reopened,$reason) ON CONFLICT(session_id) DO UPDATE SET state=excluded.state,revision=excluded.revision,reopened_utc=excluded.reopened_utc,reopen_reason=excluded.reopen_reason;";
            command.Parameters.AddWithValue("$id", value.Id.ToString("D")); command.Parameters.AddWithValue("$account", value.AccountId.ToString("D"));
            command.Parameters.AddWithValue("$currency", account.Currency.Code); command.Parameters.AddWithValue("$date", DateText(value.StatementDate)); command.Parameters.AddWithValue("$number", value.StatementNumber);
            command.Parameters.AddWithValue("$opening", ExactMoney.ToMinorUnits(value.OpeningBalance, account.Currency)); command.Parameters.AddWithValue("$expected", ExactMoney.ToMinorUnits(value.ExpectedClosingBalance, account.Currency));
            command.Parameters.AddWithValue("$calculated", ExactMoney.ToMinorUnits(value.CalculatedBalance, account.Currency)); command.Parameters.AddWithValue("$previous", (object?)value.PreviousSessionId?.ToString("D") ?? DBNull.Value);
            command.Parameters.AddWithValue("$validated", TimestampText(value.ValidatedUtc)); command.Parameters.AddWithValue("$state", (int)value.State); command.Parameters.AddWithValue("$revision", value.Revision);
            command.Parameters.AddWithValue("$reopened", value.ReopenedUtc is { } reopened ? TimestampText(reopened) : DBNull.Value); command.Parameters.AddWithValue("$reason", (object?)value.ReopenReason ?? DBNull.Value);
            command.ExecuteNonQuery();
            foreach (var entry in change.Entries)
            {
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO ReconciliationEntries(session_id,transaction_id,snapshot_version,snapshot_json) VALUES($session,$transaction,1,$snapshot);";
                insert.Parameters.AddWithValue("$session", value.Id.ToString("D")); insert.Parameters.AddWithValue("$transaction", entry.Original.Id.ToString("D"));
                insert.Parameters.AddWithValue("$snapshot", EncodeOperationSnapshot(entry.Original)); insert.ExecuteNonQuery();
            }
        }
        foreach (var value in batch.Events)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO ReconciliationHistory(" + EventColumns + ") VALUES($id,$session,$transaction,$kind,$occurred,$reason,$version,$snapshot);";
            command.Parameters.AddWithValue("$id", value.Id.ToString("D")); command.Parameters.AddWithValue("$session", (object?)value.SessionId?.ToString("D") ?? DBNull.Value);
            command.Parameters.AddWithValue("$transaction", (object?)value.TransactionId?.ToString("D") ?? DBNull.Value); command.Parameters.AddWithValue("$kind", (int)value.Kind);
            command.Parameters.AddWithValue("$occurred", TimestampText(value.OccurredUtc)); command.Parameters.AddWithValue("$reason", value.Reason);
            command.Parameters.AddWithValue("$version", value.OperationBefore is null ? DBNull.Value : 1); command.Parameters.AddWithValue("$snapshot", value.OperationBefore is { } before ? EncodeOperationSnapshot(before) : DBNull.Value);
            command.ExecuteNonQuery();
        }
    }
}
