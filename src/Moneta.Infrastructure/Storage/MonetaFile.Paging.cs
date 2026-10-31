using System.Globalization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private readonly record struct LedgerLight(Guid Id, string CanonicalId, DateOnly Date, DateTimeOffset CreatedUtc, decimal SignedAmount);

    public LedgerPage ReadPage(Account account, int offset, int pageSize, IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        try
        {
            var overlay = new Dictionary<Guid, Transaction>();
            foreach (var change in pending)
            {
                if (change?.Value is not { } value || !overlay.TryAdd(value.Id, value))
                    throw new ArgumentException("Opération de brouillon absente ou dupliquée.");
                if (value.AccountId == account.Id && (value.Currency != account.Currency || value.Amount < 0 ||
                    !Enum.IsDefined(value.Direction) || value.Date == default || value.CreatedUtc.Offset != TimeSpan.Zero))
                    throw new ArgumentException("Valeurs du brouillon incohérentes avec le compte.");
                if (value.AccountId == account.Id) _ = ExactMoney.ToMinorUnits(value.Amount, account.Currency);
            }
            var pendingRows = overlay.Values.Where(value => value.AccountId == account.Id && !value.IsDeleted)
                .Select(value => new LedgerLight(value.Id, value.Id.ToString("D"), value.Date, value.CreatedUtc, FinancialRules.SignedAmount(value)))
                .OrderBy(value => value.Date).ThenBy(value => value.CreatedUtc).ThenBy(value => value.CanonicalId, StringComparer.Ordinal).ToArray();
            using var transaction = Connection.BeginTransaction(deferred: true);
            long count = 0, anchorIndex = -1;
            var balance = account.OpeningBalance;
            foreach (var value in StreamEffectiveRows(account, overlay, pendingRows, transaction))
            {
                if (value.Id == anchorId) anchorIndex = count;
                balance = checked(balance + value.SignedAmount);
                count = checked(count + 1);
            }
            var requestedOffset = anchorIndex >= 0 ? anchorIndex / pageSize * pageSize : offset;
            var lastOffset = count == 0 ? 0 : (count - 1) / pageSize * pageSize;
            var actualOffset = checked((int)Math.Min(requestedOffset, lastOffset));
            var before = account.OpeningBalance;
            var pageIds = new List<Guid>(checked((int)Math.Min(pageSize, count - actualOffset)));
            long index = 0;
            foreach (var value in StreamEffectiveRows(account, overlay, pendingRows, transaction))
            {
                if (index < actualOffset) before = checked(before + value.SignedAmount);
                else if (pageIds.Count < pageSize) pageIds.Add(value.Id);
                else break;
                index++;
            }
            var lookup = ledgerLookup;
            var rows = new List<Transaction>(pageIds.Count);
            foreach (var id in pageIds)
            {
                if (overlay.TryGetValue(id, out var draft)) rows.Add(draft);
                else
                {
                    var value = FindTransaction(Connection, id, lookup, transaction) ?? throw Error(DocumentError.Corrupt, "Une opération de la page est introuvable.");
                    FinancialRules.ValidateTransaction(value, Snapshot);
                    rows.Add(value);
                }
            }
            transaction.Commit();
            return new(rows.AsReadOnly(), before, balance, count, actualOffset);
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception, DocumentError.InvalidData); }
    }

    private IEnumerable<LedgerLight> StreamEffectiveRows(Account account, IReadOnlyDictionary<Guid, Transaction> overlay, LedgerLight[] pending, SqliteTransaction transaction)
    {
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT transaction_id,date,created_utc,amount_minor,direction,native_code FROM Transactions WHERE account_id=$account AND is_deleted=0 ORDER BY date,created_utc,transaction_id COLLATE BINARY;";
        command.Parameters.AddWithValue("$account", account.Id.ToString("D"));
        using var reader = command.ExecuteReader();
        var nextPending = 0;
        while (reader.Read())
        {
            var id = ReadGuid(reader.GetString(0));
            if (overlay.ContainsKey(id)) continue;
            if (!DateOnly.TryParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == default)
                throw Error(DocumentError.Corrupt, "Date de l’opération invalide.");
            var created = ReadTimestamp(reader.GetString(2));
            var minor = reader.GetInt64(3);
            var direction = reader.GetInt64(4);
            if (minor < 0 || direction is not (0 or 1) || reader.GetString(5) != account.Currency.Code)
                throw Error(DocumentError.Corrupt, "Montant ou devise de l’opération invalide.");
            var amount = ExactMoney.FromMinorUnits(minor, account.Currency);
            var value = new LedgerLight(id, reader.GetString(0), date, created, direction == 0 ? -amount : amount);
            while (nextPending < pending.Length && CompareLight(pending[nextPending], value) < 0) yield return pending[nextPending++];
            yield return value;
        }
        while (nextPending < pending.Length) yield return pending[nextPending++];
    }

    private static int CompareLight(LedgerLight left, LedgerLight right)
    {
        var date = left.Date.CompareTo(right.Date);
        if (date != 0) return date;
        var created = left.CreatedUtc.CompareTo(right.CreatedUtc);
        return created != 0 ? created : string.CompareOrdinal(left.CanonicalId, right.CanonicalId);
    }
}
