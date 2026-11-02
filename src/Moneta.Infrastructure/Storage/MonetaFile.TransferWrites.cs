using System.Globalization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private static void ValidateTransferChanges(SqliteConnection connection, FinancialDocument metadata,
        IReadOnlyCollection<TransactionChange> changes, IReadOnlyCollection<TransferChange> transfers,
        SqliteTransaction transaction, bool creating, FinancialDocument? baseline = null)
    {
        try
        {
            var lookup = new LedgerLookup(baseline ?? metadata);
            var patches = changes.ToDictionary(change => change.Value.Id);
            var transferIds = new HashSet<Guid>();
            var coveredLegs = new Dictionary<Guid, Guid>();
            foreach (var change in transfers)
            {
                if (change?.Value is not { } value || !transferIds.Add(value.Id) || change.ExpectedRevision < 0 ||
                    (creating && change.ExpectedRevision != 0) || value.Revision != checked(change.ExpectedRevision + 1))
                    throw new ArgumentException("Lot de révisions de virements invalide.");
                var previous = FindTransfer(connection, value.Id, baseline ?? metadata, lookup, transaction);
                if (change.ExpectedRevision == 0)
                {
                    if (previous is not null || value.IsDeleted) throw new ArgumentException("Virement déjà présent ou supprimé à l’insertion.");
                }
                else if (previous is null || previous.Value.IsDeleted || previous.Value.Revision != change.ExpectedRevision ||
                    previous.Value.CreatedUtc != value.CreatedUtc || value.ModifiedUtc < previous.Value.ModifiedUtc ||
                    previous.Value.DebitTransactionId != value.DebitTransactionId || previous.Value.CreditTransactionId != value.CreditTransactionId ||
                    (previous.Value.FeeTransactionId is not null && previous.Value.FeeTransactionId != value.FeeTransactionId))
                    throw new ArgumentException("Révision obsolète ou identité/liens du virement modifiés.");
                if (previous?.HasReconciledLeg == true)
                    throw new ArgumentException("Une jambe rapprochée protège le virement entier. Rouvrez ou libérez-la, puis enregistrez avant modification.");

                Transaction Leg(Guid id, Transaction? original)
                {
                    if (!coveredLegs.TryAdd(id, value.Id) || !patches.TryGetValue(id, out var patch))
                        throw new ArgumentException("Chaque virement exige un lot complet de jambes distinctes.");
                    var owner = FindTransferId(connection, id, transaction);
                    if (owner is not null && owner != value.Id) throw new ArgumentException("Jambe déjà liée à un autre virement.");
                    if (original is null)
                    {
                        if (patch.ExpectedRevision != 0 || FindTransaction(connection, id, lookup, transaction) is not null)
                            throw new ArgumentException("Une nouvelle jambe doit être une nouvelle opération.");
                    }
                    else if (patch.ExpectedRevision != original.Revision || patch.Value.ReconciliationId != original.ReconciliationId)
                        throw new ArgumentException("La révision et l’appartenance au rapprochement de chaque jambe doivent être conservées.");
                    return patch.Value;
                }
                var debit = Leg(value.DebitTransactionId, previous?.Debit);
                var credit = Leg(value.CreditTransactionId, previous?.Credit);
                var fee = value.FeeTransactionId is { } feeId ? Leg(feeId, previous?.Fee) : null;
                // A positive fee may have been corrected to zero in the unsaved draft.
                // NP/P transitions are independently checked by ValidateReconciliationChanges.
                TransferRules.Validate(new(value, debit, credit, fee), metadata);
            }
            foreach (var patch in changes)
            {
                var owner = FindTransferId(connection, patch.Value.Id, transaction);
                if (owner is null || coveredLegs.TryGetValue(patch.Value.Id, out var covered) && covered == owner) continue;
                var original = FindTransaction(connection, patch.Value.Id, lookup, transaction)
                    ?? throw new ArgumentException("Jambe de virement orpheline.");
                if (!ReconciliationRules.FinancialPayloadEquals(original, patch.Value))
                    throw new ArgumentException("Une jambe liée ne peut être modifiée séparément : modifiez le virement entier.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException or NullReferenceException)
        {
            throw new DocumentException(DocumentError.InvalidData, "Modification de virement invalide : " + exception.Message, exception);
        }
    }

    private static void WriteTransferChanges(SqliteConnection connection, IReadOnlyCollection<TransferChange> changes, SqliteTransaction transaction)
    {
        foreach (var change in changes)
        {
            var value = change.Value;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO Transfers(" + TransferColumns + ") VALUES($id,$date,$reference,$notes,$exchange,$source,$created,$modified,$revision,$deleted) ON CONFLICT(transfer_id) DO UPDATE SET date=excluded.date,reference=excluded.reference,notes=excluded.notes,exchange_date=excluded.exchange_date,rate_source=excluded.rate_source,modified_utc=excluded.modified_utc,revision=excluded.revision,is_deleted=excluded.is_deleted;";
                command.Parameters.AddWithValue("$id", value.Id.ToString("D"));
                command.Parameters.AddWithValue("$date", value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$reference", value.Reference); command.Parameters.AddWithValue("$notes", value.Notes);
                command.Parameters.AddWithValue("$exchange", (object?)value.ExchangeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
                command.Parameters.AddWithValue("$source", (object?)value.RateSource ?? DBNull.Value);
                command.Parameters.AddWithValue("$created", value.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$modified", value.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$revision", value.Revision); command.Parameters.AddWithValue("$deleted", value.IsDeleted ? 1 : 0);
                command.ExecuteNonQuery();
            }
            void Link(int role, Guid id)
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                // Existing links are immutable and were compared before any writes.
                command.CommandText = "INSERT INTO TransferLegs(transfer_id,role,transaction_id) VALUES($transfer,$role,$transaction) ON CONFLICT(transfer_id,role) DO NOTHING;";
                command.Parameters.AddWithValue("$transfer", value.Id.ToString("D")); command.Parameters.AddWithValue("$role", role);
                command.Parameters.AddWithValue("$transaction", id.ToString("D")); command.ExecuteNonQuery();
            }
            Link(0, value.DebitTransactionId); Link(1, value.CreditTransactionId);
            if (value.FeeTransactionId is { } fee) Link(2, fee);
        }
    }
}
