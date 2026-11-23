using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string TransferColumns = "transfer_id,date,reference,notes,exchange_date,rate_source,created_utc,modified_utc,revision,is_deleted";
    private static readonly string TransferReadSql = "SELECT " + string.Join(",", TransactionColumns.Split(',').Select(value => "t." + value)) + "," +
        string.Join(",", TransferColumns.Split(',').Select(value => "v." + value)) + ",l.role FROM Transfers v LEFT JOIN TransferLegs l ON l.transfer_id=v.transfer_id LEFT JOIN Transactions t ON t.transaction_id=l.transaction_id";

    public TransferDetails ReadTransfer(Guid id)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var result = FindTransfer(Connection, id, Snapshot, ledgerLookup, transaction)
                ?? throw Error(DocumentError.InvalidData, L.Get("MonetaFile.TransferUnobtainable"));
            transaction.Commit();
            return result;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
    }

    public Guid? FindTransferId(Guid transactionId)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var id = FindTransferId(Connection, transactionId, transaction);
            transaction.Commit();
            return id;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
    }

    private static Guid? FindTransferId(SqliteConnection connection, Guid transactionId, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT transfer_id FROM TransferLegs WHERE transaction_id=$id;";
        command.Parameters.AddWithValue("$id", transactionId.ToString("D"));
        var result = command.ExecuteScalar();
        return result is null ? null : ReadGuid((string)result);
    }

    private static TransferDetails? FindTransfer(SqliteConnection connection, Guid id, FinancialDocument metadata, LedgerLookup lookup, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = TransferReadSql + " WHERE v.transfer_id=$id ORDER BY l.role;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return StreamTransfers(reader, metadata, lookup).SingleOrDefault();
    }

    // One ordered join, retaining only the current transfer and its two or three legs.
    // Transaction columns lead the projection to reuse the canonical transaction decoder.
    private static IEnumerable<TransferDetails> StreamTransfers(SqliteDataReader reader, FinancialDocument metadata, LedgerLookup lookup)
    {
        Transfer? current = null;
        Transaction? debit = null, credit = null, fee = null;
        TransferDetails Complete()
        {
            if (current is null || debit is null || credit is null) throw new ArgumentException(L.Get("MonetaFile.IncompleteTransferDebitAndCreditRequired"));
            var details = new TransferDetails(current with { DebitTransactionId = debit.Id, CreditTransactionId = credit.Id, FeeTransactionId = fee?.Id }, debit, credit, fee);
            TransferRules.Validate(details, metadata);
            return details;
        }
        while (reader.Read())
        {
            var id = ReadGuid(reader.GetString(19));
            if (current is not null && current.Id != id)
            {
                yield return Complete();
                current = null; debit = null; credit = null; fee = null;
            }
            current ??= new Transfer(id, Guid.Empty, Guid.Empty, null, ReadDate(reader.GetString(20)), reader.GetString(21), reader.GetString(22),
                reader.IsDBNull(23) ? null : ReadDate(reader.GetString(23)), reader.IsDBNull(24) ? null : reader.GetString(24),
                ReadTimestamp(reader.GetString(25)), ReadTimestamp(reader.GetString(26)), reader.GetInt64(27), ReadBoolean(reader, 28));
            if (reader.IsDBNull(0) || reader.IsDBNull(29)) throw new ArgumentException(L.Get("MonetaFile.TransferLegAbsentOrOrphan"));
            var value = ReadTransaction(reader, lookup);
            FinancialRules.ValidateTransaction(value, metadata);
            switch (reader.GetInt64(29))
            {
                case 0 when debit is null: debit = value; break;
                case 1 when credit is null: credit = value; break;
                case 2 when fee is null: fee = value; break;
                default: throw new ArgumentException(L.Get("MonetaFile.UnknownOrDuplicatedTransferRole"));
            }
        }
        if (current is not null) yield return Complete();
    }

    private static long ValidateAndHashTransfers(SqliteConnection connection, FinancialDocument metadata, LedgerLookup lookup, IncrementalHash digest, SqliteTransaction transaction)
    {
        HashValue(digest, "Moneta-transfers-v5");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = TransferReadSql + " ORDER BY v.transfer_id COLLATE BINARY,l.role;";
        using var reader = command.ExecuteReader();
        long count = 0;
        foreach (var details in StreamTransfers(reader, metadata, lookup))
        {
            var value = details.Value;
            HashValue(digest, "transfer"); HashValue(digest, value.Id.ToString("D"));
            HashValue(digest, value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            HashValue(digest, value.Reference); HashValue(digest, value.Notes);
            HashValue(digest, value.ExchangeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); HashValue(digest, value.RateSource);
            HashValue(digest, value.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)); HashValue(digest, value.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
            HashValue(digest, value.Revision.ToString(CultureInfo.InvariantCulture)); HashValue(digest, value.IsDeleted ? "1" : "0");
            HashValue(digest, "0"); HashValue(digest, value.DebitTransactionId.ToString("D"));
            HashValue(digest, "1"); HashValue(digest, value.CreditTransactionId.ToString("D"));
            HashValue(digest, value.FeeTransactionId is null ? null : "2"); HashValue(digest, value.FeeTransactionId?.ToString("D"));
            count = checked(count + 1);
        }
        return count;
    }

    private IReadOnlyDictionary<Guid, Guid> ReadPageTransferIds(IReadOnlyCollection<Guid> pageIds, SqliteTransaction transaction)
    {
        var result = new Dictionary<Guid, Guid>(pageIds.Count);
        if (pageIds.Count == 0) return result;
        using var command = Connection.CreateCommand(); command.Transaction = transaction;
        var parameters = new List<string>(pageIds.Count);
        foreach (var id in pageIds)
        {
            var name = "$id" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(name); command.Parameters.AddWithValue(name, id.ToString("D"));
        }
        command.CommandText = "SELECT transaction_id,transfer_id FROM TransferLegs WHERE transaction_id IN (" + string.Join(",", parameters) + ");";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ReadGuid(reader.GetString(0)), ReadGuid(reader.GetString(1)));
        return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, Guid>(result);
    }
}
