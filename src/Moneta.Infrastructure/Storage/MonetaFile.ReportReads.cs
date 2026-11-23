using System.Globalization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    public static ReportSource ReadReport(string path, FinancialDocument snapshot, ReportCriteria criteria,
        bool balances = true, CancellationToken cancellationToken = default)
    {
        criteria.Validate();
        if (criteria.Accounts.Count > ReportContent.MaximumBalanceAccounts)
            throw new ArgumentException(L.Get("MonetaFile.LimitTheReportTo1000Accounts"));
        var requested = criteria.Accounts.ToHashSet();
        var accounts = snapshot.Accounts.Where(item => requested.Contains(item.Id))
            .ToDictionary(item => item.Id);
        if (accounts.Count != requested.Count || !criteria.IncludeArchived && accounts.Values.Any(item => item.IsArchived))
            throw new ArgumentException(L.Get("MonetaFile.ThePerimeterRequiresAnAccountThatIsMissing"));
        var labels = snapshot.Accounts.Select((account, index) => (account.Id, Text: $"{index + 1}. {account.Name}"))
            .ToDictionary(item => item.Id, item => item.Text);
        return ReadHistorical(path, snapshot.Id, (connection, transaction) =>
        {
            using var query = connection.CreateCommand();
            query.Transaction = transaction;
            var accountParameters = criteria.Accounts.Select((_, index) => "$a" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
            for (var index = 0; index < accountParameters.Length; index++)
                query.Parameters.AddWithValue(accountParameters[index], criteria.Accounts[index].ToString("D"));
            query.Parameters.AddWithValue("$from", criteria.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var observed = criteria.ObservedThrough ?? DateOnly.FromDateTime(DateTime.Today);
            var through = criteria.Through < observed ? criteria.Through : observed;
            query.Parameters.AddWithValue("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            query.Parameters.AddWithValue("$category", SqlId(criteria.CategoryId));
            query.Parameters.AddWithValue("$limit", HistoricalValuation.MaximumBatchSize + 1);
            query.CommandText = """
                SELECT t.transaction_id,t.account_id,t.date,t.amount_minor,t.native_code,t.direction,t.revision,
                       t.category_id,g.name,t.subcategory_id,s.name,l.transfer_id
                FROM Transactions t
                LEFT JOIN Categories g ON g.category_id=t.category_id
                LEFT JOIN Categories s ON s.category_id=t.subcategory_id
                LEFT JOIN TransferLegs l ON l.transaction_id=t.transaction_id
                LEFT JOIN Transfers v ON v.transfer_id=l.transfer_id
                WHERE t.is_deleted=0 AND t.date BETWEEN $from AND $through
                  AND (l.transfer_id IS NULL OR (l.role=2 AND v.is_deleted=0))
                  AND ($category IS NULL OR t.category_id=$category OR t.subcategory_id=$category)
                """ + " AND t.account_id IN (" + string.Join(',', accountParameters) + ") ORDER BY t.date,t.transaction_id LIMIT $limit;";
            var rows = new List<ReportInput>();
            using (var reader = query.ExecuteReader())
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (rows.Count == HistoricalValuation.MaximumBatchSize)
                        throw new ArgumentException(L.Get("MonetaFile.TheReportExceeds50000FlowsReduceIts"));
                    var account = accounts[ReadGuid(reader.GetString(1))];
                    var date = DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var direction = (TransactionDirection)reader.GetInt32(5);
                    var minor = reader.GetInt64(3);
                    if (minor < 0 || account.Currency.Code != reader.GetString(4) || !Enum.IsDefined(direction))
                        throw Error(DocumentError.Corrupt, L.Get("MonetaFile.ATransactionInTheReportContainsAnInvalid"));
                    var transfer = NullableId(reader, 11);
                    var kind = transfer is not null ? HistoricalFlowKind.Fee
                        : direction == TransactionDirection.Credit ? HistoricalFlowKind.Income : HistoricalFlowKind.Expense;
                    var amount = ExactMoney.FromMinorUnits(direction == TransactionDirection.Credit ? minor : -minor, account.Currency);
                    var subject = new ReportSubject(account.Id, labels[account.Id], NullableId(reader, 7), reader.IsDBNull(8) ? null : reader.GetString(8),
                        NullableId(reader, 9), reader.IsDBNull(10) ? null : reader.GetString(10));
                    rows.Add(new(subject, new(HistoricalUse.Flow, date, amount, account.Currency.Code,
                        ReadGuid(reader.GetString(0)), reader.GetInt64(6), kind, transfer)));
                }
            var stock = balances ? accounts.Values.Select(account => new ReportInput(new(account.Id, labels[account.Id]),
                ReadHistoricalBalance(connection, transaction, snapshot.Id, account, criteria.Through, cancellationToken).Input())).ToArray() : [];
            return new ReportSource(rows.AsReadOnly(), Array.AsReadOnly(stock));
        }, cancellationToken);
    }
}
