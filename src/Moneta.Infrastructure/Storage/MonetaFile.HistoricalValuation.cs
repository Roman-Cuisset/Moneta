using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moneta.Core;
using SQLitePCL;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    public static HistoricalFlow ReadHistoricalFlow(string path, FinancialDocument snapshot, Guid id,
        CancellationToken cancellationToken = default) => ReadHistorical(path, snapshot.Id, (connection, transaction) =>
    {
        var entry = FindTransaction(connection, id, new LedgerLookup(snapshot), transaction);
        if (entry is null || entry.IsDeleted)
            throw Error(DocumentError.InvalidData, "Cette opération n’existe pas dans le registre enregistré.");
        FinancialRules.ValidateTransaction(entry, snapshot);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT l.transfer_id,l.role,v.is_deleted FROM TransferLegs l JOIN Transfers v ON v.transfer_id=l.transfer_id WHERE l.transaction_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new HistoricalFlow(entry);
        var role = (LedgerTransferLeg)reader.GetInt32(1);
        if (!Enum.IsDefined(role) || ReadBoolean(reader, 2))
            throw Error(DocumentError.Corrupt, "Lien du virement invalide.");
        return new HistoricalFlow(entry, ReadGuid(reader.GetString(0)), role);
    }, cancellationToken);

    public static HistoricalBalance ReadHistoricalBalance(string path, FinancialDocument snapshot, Guid accountId, DateOnly cutoff,
        CancellationToken cancellationToken = default)
    {
        if (cutoff == default) throw new ArgumentException("Choisissez une date d’arrêté valide.", nameof(cutoff));
        var account = snapshot.Accounts.SingleOrDefault(item => item.Id == accountId)
            ?? throw Error(DocumentError.InvalidData, "Ce compte n’existe pas dans le dossier enregistré.");
        return ReadHistorical(path, snapshot.Id, (connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$account", accountId.ToString("D"));
            command.CommandText = "SELECT a.opening_minor,a.native_code,c.decimal_places FROM Accounts a JOIN Currencies c ON c.code=a.native_code WHERE a.account_id=$account;";
            long opening;
            using (var header = command.ExecuteReader())
            {
                if (!header.Read() || header.GetString(1) != account.Currency.Code || header.GetInt32(2) != account.Currency.DecimalPlaces)
                    throw Error(DocumentError.InvalidData, "Le compte enregistré a changé. Rouvrez le dossier avant de calculer.");
                opening = header.GetInt64(0);
            }

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            HashValue(digest, "Moneta-historical-balance-v1");
            HashValue(digest, snapshot.Id.ToString("D"));
            HashValue(digest, accountId.ToString("D"));
            HashValue(digest, account.Currency.Code);
            HashValue(digest, account.Currency.DecimalPlaces.ToString(CultureInfo.InvariantCulture));
            HashValue(digest, opening.ToString(CultureInfo.InvariantCulture));
            var date = cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            HashValue(digest, date);
            command.Parameters.AddWithValue("$cutoff", date);
            command.CommandText = """
                SELECT transaction_id,revision,date,direction,amount_minor,native_code FROM Transactions
                WHERE account_id=$account AND is_deleted=0 AND date<=$cutoff
                ORDER BY date,transaction_id;
                """;
            using var reader = command.ExecuteReader();
            BigInteger total = opening;
            long count = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var direction = (TransactionDirection)reader.GetInt32(3);
                var amount = reader.GetInt64(4);
                if (!Enum.IsDefined(direction) || amount < 0 || reader.GetString(5) != account.Currency.Code)
                    throw Error(DocumentError.Corrupt, "Une écriture enregistrée a une devise, un montant ou un sens invalide.");
                var signed = direction == TransactionDirection.Debit ? -amount : amount;
                total += signed;
                count++;
                HashValue(digest, reader.GetString(0));
                HashValue(digest, reader.GetInt64(1).ToString(CultureInfo.InvariantCulture));
                HashValue(digest, reader.GetString(2));
                HashValue(digest, signed.ToString(CultureInfo.InvariantCulture));
            }
            var amountAtDate = ExactMoney.FromMinorUnits(total, account.Currency);
            return new HistoricalBalance(accountId, cutoff, account.Currency, amountAtDate, count, Convert.ToHexString(digest.GetHashAndReset()));
        }, cancellationToken);
    }

    private static T ReadHistorical<T>(string path, Guid documentId, Func<SqliteConnection, SqliteTransaction, T> read,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenSearchConnection(path, cancellationToken);
        raw.sqlite3_progress_handler(connection.Handle!, 1000, _ => cancellationToken.IsCancellationRequested ? 1 : 0, null);
        try
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT document_id FROM Document WHERE singleton=1 AND format='Moneta' AND schema_version=$version;";
            check.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            if (check.ExecuteScalar() is not string id || id != documentId.ToString("D"))
                throw Error(DocumentError.InvalidData, "Le dossier enregistré ne correspond plus au dossier ouvert.");
            var result = read(connection, transaction);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }
        catch (SqliteException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (SqliteException error) { throw Translate(error); }
        finally { raw.sqlite3_progress_handler(connection.Handle!, 0, null, null); }
    }
}
