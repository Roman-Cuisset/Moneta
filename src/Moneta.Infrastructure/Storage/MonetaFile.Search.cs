using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Moneta.Core;
using SQLitePCL;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string SearchFrom = """
        FROM Transactions t
        JOIN Accounts a ON a.account_id=t.account_id
        JOIN Currencies c ON c.code=t.native_code
        LEFT JOIN Payees p ON p.payee_id=t.payee_id
        LEFT JOIN Categories g ON g.category_id=t.category_id
        LEFT JOIN Categories s ON s.category_id=t.subcategory_id
        LEFT JOIN PaymentMethods m ON m.payment_method_id=t.payment_method_id
        LEFT JOIN TransferLegs l ON l.transaction_id=t.transaction_id
        LEFT JOIN Transfers x ON x.transfer_id=l.transfer_id AND x.is_deleted=0
        """;
    private const string SignedMinor = "(CASE t.direction WHEN 0 THEN -t.amount_minor ELSE t.amount_minor END)";
    private static readonly decimal[] SearchScales = Enumerable.Range(0, 29)
        .Select(places => 1m / ExactMoney.FromMinorUnits(1, new Currency("XXX", "Recherche", places))).ToArray();

    public static LedgerSearchPage SearchOperations(string path, Guid documentId, LedgerSearchCriteria criteria, int index,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        criteria.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection(ValidatePath(path, true), true);
        raw.sqlite3_progress_handler(connection.Handle!, 1000, _ => cancellationToken.IsCancellationRequested ? 1 : 0, null);
        try
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT document_id FROM Document WHERE singleton=1 AND format='Moneta' AND schema_version=$version;";
            command.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            if (command.ExecuteScalar() is not string id || id != documentId.ToString("D"))
                throw Error(DocumentError.InvalidData, "Le dossier de recherche n’est plus le dossier ouvert.");
            command.Parameters.Clear();

            var filter = ConfigureSearch(connection, command, criteria);
            var clock = Stopwatch.StartNew();
            command.CommandText = "SELECT COUNT(*) " + SearchFrom + " WHERE " + filter;
            var total = (long)command.ExecuteScalar()!;
            var countElapsed = clock.Elapsed;
            var pageIndex = total == 0 ? 0 : checked((int)Math.Min(index, (total - 1) / LedgerSearchCriteria.PageSize));
            clock.Restart();
            if (total == 0) return new([], 0, 0, countElapsed, TimeSpan.Zero);
            var direction = criteria.Descending ? " DESC" : " ASC";
            var sort = criteria.Sort switch
            {
                LedgerSearchSort.Amount => "t.native_code ASC," + SignedMinor,
                LedgerSearchSort.Label => "t.label COLLATE moneta_text",
                LedgerSearchSort.Account => "a.name COLLATE moneta_text",
                LedgerSearchSort.Category => "g.name COLLATE moneta_text",
                LedgerSearchSort.Payee => "p.name COLLATE moneta_text",
                LedgerSearchSort.Status => "t.status",
                _ => "t.date"
            };
            command.CommandText = """
                SELECT t.transaction_id,t.account_id,a.name,a.ordinal,a.is_archived,t.date,t.label,t.notes,
                       p.name,g.name,s.name,m.name,t.native_code,c.decimal_places,t.direction,t.status,
                       x.transfer_id,l.role,x.reference,
                """ + SignedMinor + " " + SearchFrom + " WHERE " + filter +
                " ORDER BY " + sort + direction + ",t.transaction_id ASC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$limit", LedgerSearchCriteria.PageSize);
            command.Parameters.AddWithValue("$offset", (long)pageIndex * LedgerSearchCriteria.PageSize);
            var rows = new List<LedgerSearchRow>(LedgerSearchCriteria.PageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string Text(int i) => reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
                var places = reader.GetInt32(13);
                var transfer = !reader.IsDBNull(16);
                rows.Add(new(ReadGuid(Text(0)), ReadGuid(Text(1)), Text(2), reader.GetInt64(3), ReadBoolean(reader, 4),
                    DateOnly.ParseExact(Text(5), "yyyy-MM-dd", CultureInfo.InvariantCulture), Text(6), Text(7), Text(8), Text(9),
                    Text(10), Text(11), Text(12), places, reader.GetInt64(19) / SearchScales[places],
                    (TransactionDirection)reader.GetInt32(14), (ReconciliationStatus)reader.GetInt32(15),
                    transfer ? ReadGuid(Text(16)) : null, transfer ? (LedgerTransferLeg)reader.GetInt32(17) : null, Text(18)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(rows.AsReadOnly(), total, pageIndex, countElapsed, clock.Elapsed);
        }
        catch (SqliteException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqliteException error) { throw Translate(error); }
        finally { raw.sqlite3_progress_handler(connection.Handle!, 0, null, null); }
    }

    private static string ConfigureSearch(SqliteConnection connection, SqliteCommand command, LedgerSearchCriteria query)
    {
        var clauses = new List<string> { "t.is_deleted=0" };
        void Add(string predicate, string name, object? value)
        {
            if (value is null) return;
            clauses.Add(predicate);
            command.Parameters.AddWithValue(name, value);
        }
        // The explicitly selected account remains searchable even when it is archived.
        if (query.AccountId is { } account) Add("t.account_id=$account", "$account", account.ToString("D"));
        else if (!query.IncludeArchived) clauses.Add("a.is_archived=0");
        Add("t.native_code=$currency", "$currency", query.CurrencyCode);
        Add("t.date >= $from", "$from", query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("t.date <= $through", "$through", query.Through?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (query.Status != LedgerSearchStatus.All) Add("((1 << t.status) & $status)<>0", "$status", (int)query.Status);
        switch (query.Kind)
        {
            case LedgerSearchKind.Income: clauses.Add("x.transfer_id IS NULL AND t.direction=1"); break;
            case LedgerSearchKind.Expense: clauses.Add("x.transfer_id IS NULL AND t.direction=0"); break;
            case LedgerSearchKind.Transfers: clauses.Add("x.transfer_id IS NOT NULL"); break;
            case LedgerSearchKind.Fees: clauses.Add("x.transfer_id IS NOT NULL AND l.role=2"); break;
        }
        connection.CreateCollation("moneta_text", (left, right) =>
            string.Compare(left?.Normalize(), right?.Normalize(), StringComparison.OrdinalIgnoreCase));
        connection.CreateFunction<string?, string, bool>("moneta_contains",
            (source, term) => source?.Normalize().Contains(term, StringComparison.OrdinalIgnoreCase) == true,
            isDeterministic: true);
        void TextFilter(string column, string parameter, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Add($"moneta_contains({column},{parameter})", parameter, text.Trim().Normalize());
        }
        TextFilter("p.name", "$payee", query.Payee);
        TextFilter("g.name", "$category", query.Category);
        TextFilter("s.name", "$subcategory", query.Subcategory);
        TextFilter("m.name", "$method", query.PaymentMethod);

        var words = (query.Text ?? string.Empty).Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (words.Length > 0)
        {
            connection.CreateFunction("moneta_words", (string fields) =>
            {
                var normalized = fields.Normalize();
                foreach (var word in words)
                    if (!normalized.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }, isDeterministic: true);
            // A term contains no whitespace, so it can never match across this field separator.
            clauses.Add("""
                moneta_words(t.label||char(10)||t.notes||char(10)||coalesce(p.name,'')||char(10)||
                  coalesce(g.name,'')||char(10)||coalesce(s.name,'')||char(10)||coalesce(m.name,'')||char(10)||
                  coalesce(x.reference,'')||char(10)||coalesce(x.notes,''))
                """);
        }
        if (query.Exact.HasValue || query.Minimum.HasValue || query.Maximum.HasValue)
        {
            connection.CreateFunction("moneta_amount", (long signed, int places) =>
            {
                var amount = signed / SearchScales[places];
                return (!query.Exact.HasValue || amount == query.Exact.Value) &&
                       (!query.Minimum.HasValue || amount >= query.Minimum.Value) &&
                       (!query.Maximum.HasValue || amount <= query.Maximum.Value);
            }, isDeterministic: true);
            clauses.Add($"moneta_amount({SignedMinor},c.decimal_places)");
        }
        return string.Join(" AND ", clauses);
    }
}
