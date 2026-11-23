using System.Globalization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string BudgetSql = """
        CREATE TABLE Budgets (
            budget_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 120),
            month TEXT NOT NULL CHECK(length(month)=10),
            currency TEXT NOT NULL REFERENCES Currencies(code),
            planned_minor INTEGER NOT NULL CHECK(planned_minor>=0)
        ) STRICT
        """;
    private const string BudgetAccountsSql = """
        CREATE TABLE BudgetAccounts (
            budget_id TEXT NOT NULL REFERENCES Budgets(budget_id) ON DELETE CASCADE,
            account_id TEXT NOT NULL REFERENCES Accounts(account_id),
            PRIMARY KEY(budget_id,account_id)
        ) STRICT
        """;
    private const string BudgetCategoriesSql = """
        CREATE TABLE BudgetCategories (
            budget_id TEXT NOT NULL REFERENCES Budgets(budget_id) ON DELETE CASCADE,
            category_id TEXT NOT NULL REFERENCES Categories(category_id),
            PRIMARY KEY(budget_id,category_id)
        ) STRICT
        """;
    private const string FrozenReportsSql = """
        CREATE TABLE FrozenReports (
            report_id TEXT NOT NULL PRIMARY KEY,
            predecessor_id TEXT REFERENCES FrozenReports(report_id),
            name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 120),
            captured_utc TEXT NOT NULL,
            sha256 TEXT NOT NULL CHECK(length(sha256)=64),
            payload TEXT NOT NULL CHECK(length(CAST(payload AS BLOB)) BETWEEN 1 AND 33554432)
        ) STRICT
        """;
    private const string AnalysisSql = BudgetSql + ";" + BudgetAccountsSql + ";" + BudgetCategoriesSql + ";" + FrozenReportsSql + ";";

    internal static void UpgradeV7(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 7) throw Error(DocumentError.UnsupportedVersion, L.Get("MonetaFile.TheSourceDocumentMustBeInVersion7"));
        using var transaction = connection.BeginTransaction();
        Execute(connection, AnalysisSql + "UPDATE Document SET schema_version=8,min_reader=8,min_writer=8; PRAGMA user_version=8;", transaction);
        InsertHistory(connection, transaction, 8, 7, "v7-to-v8-analysis", "budgets-and-frozen-reports-v8");
        VerifySnapshot(original with { SchemaVersion = 8 }, ReadWithinTransaction(connection, false, transaction));
        transaction.Commit();
    }

    private static void AddAnalysisSchema(Dictionary<string, string> tables, Dictionary<string, string> indexes)
    {
        foreach (var (table, sql) in new[] { ("Budgets", BudgetSql), ("BudgetAccounts", BudgetAccountsSql),
                     ("BudgetCategories", BudgetCategoriesSql), ("FrozenReports", FrozenReportsSql) })
        {
            tables.Add(table, sql);
            indexes.Add($"sqlite_autoindex_{table}_1", table);
        }
    }

    private static FinancialDocument ReadAnalysis(SqliteConnection connection, FinancialDocument snapshot, SqliteTransaction transaction)
    {
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        Dictionary<Guid, List<Guid>> ReadLinks(string table, string column)
        {
            query.CommandText = $"SELECT budget_id,{column} FROM {table} ORDER BY budget_id,{column};";
            var result = new Dictionary<Guid, List<Guid>>();
            using var entries = query.ExecuteReader();
            while (entries.Read())
            {
                var budget = ReadGuid(entries.GetString(0));
                if (!result.TryGetValue(budget, out var list)) result.Add(budget, list = []);
                list.Add(ReadGuid(entries.GetString(1)));
            }
            return result;
        }
        var accounts = ReadLinks("BudgetAccounts", "account_id");
        var categories = ReadLinks("BudgetCategories", "category_id");
        var budgets = new List<BudgetPlan>();
        query.CommandText = "SELECT budget_id,name,month,currency,planned_minor FROM Budgets ORDER BY budget_id;";
        using (var reader = query.ExecuteReader())
            while (reader.Read())
            {
                var id = ReadGuid(reader.GetString(0));
                var month = DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var value = new BudgetPlan(id, reader.GetString(1), month, reader.GetString(3), reader.GetInt64(4),
                    Array.AsReadOnly((accounts.GetValueOrDefault(id) ?? []).Order().ToArray()),
                    Array.AsReadOnly((categories.GetValueOrDefault(id) ?? []).Order().ToArray()));
                value.Validate(snapshot);
                budgets.Add(value);
            }

        var reports = new List<FrozenReport>();
        query.CommandText = "SELECT report_id,predecessor_id,name,captured_utc,sha256,payload FROM FrozenReports ORDER BY report_id;";
        using (var reader = query.ExecuteReader())
            while (reader.Read())
            {
                var report = ReadFrozenReportHeader(reader);
                _ = report.Reproduce(reader.GetString(5));
                reports.Add(report);
            }
        ValidateReportFamily(reports);
        return snapshot with { Budgets = budgets.AsReadOnly(), FrozenReports = reports.AsReadOnly() };
    }

    private static FrozenReport ReadFrozenReportHeader(SqliteDataReader reader) => new(ReadGuid(reader.GetString(0)),
        reader.IsDBNull(1) ? null : ReadGuid(reader.GetString(1)), reader.GetString(2), ReadTimestamp(reader.GetString(3)), reader.GetString(4));

    public static ReportContent ReadFrozenReport(string path, Guid documentId, Guid reportId, CancellationToken token = default) =>
        ReadHistorical(path, documentId, (connection, transaction) =>
        {
            using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = "SELECT report_id,predecessor_id,name,captured_utc,sha256,payload FROM FrozenReports WHERE report_id=$id;";
            query.Parameters.AddWithValue("$id", reportId.ToString("D"));
            using var reader = query.ExecuteReader();
            if (!reader.Read()) throw Error(DocumentError.InvalidData, L.Get("MonetaFile.UnrecognizedRecordedReportNotFound"));
            return ReadFrozenReportHeader(reader).Reproduce(reader.GetString(5));
        }, token);

    private static void ValidateReportFamily(IReadOnlyList<FrozenReport> reports)
    {
        var byId = reports.ToDictionary(report => report.Id);
        var checkedReports = new HashSet<Guid>();
        foreach (var report in reports)
        {
            report.Validate();
            var path = new HashSet<Guid>();
            var current = report;
            while (!checkedReports.Contains(current.Id))
            {
                if (!path.Add(current.Id)) throw new ArgumentException(L.Get("MonetaFile.AFamilyOfReportsContainsACycle"));
                if (current.PredecessorId is not { } parent) break;
                if (!byId.TryGetValue(parent, out current)) throw new ArgumentException(L.Get("MonetaFile.PreviousVersionOfReportNotFound"));
            }
            checkedReports.UnionWith(path);
        }
    }

    private static void ValidateAnalysis(FinancialDocument value)
    {
        if (value.Budgets.Select(item => item.Id).Distinct().Count() != value.Budgets.Count)
            throw new ArgumentException(L.Get("MonetaFile.DoubleBudgetIdentifier"));
        foreach (var budget in value.Budgets) budget.Validate(value);
        ValidateReportFamily(value.FrozenReports);
    }

    private static bool SameBudget(BudgetPlan left, BudgetPlan right) =>
        left with { Accounts = right.Accounts, Categories = right.Categories } == right &&
        left.Accounts.Order().SequenceEqual(right.Accounts.Order()) && left.Categories.Order().SequenceEqual(right.Categories.Order());

    private static bool SameAnalysis(FinancialDocument left, FinancialDocument right) =>
        left.Budgets.Count == right.Budgets.Count && left.Budgets.OrderBy(item => item.Id).Zip(right.Budgets.OrderBy(item => item.Id), SameBudget).All(equal => equal) &&
        left.FrozenReports.OrderBy(item => item.Id).Select(item => item with { DraftPayload = null })
            .SequenceEqual(right.FrozenReports.OrderBy(item => item.Id).Select(item => item with { DraftPayload = null }));

    private static void RemoveObsoleteBudgets(SqliteConnection connection, SqliteTransaction transaction,
        FinancialDocument desired, FinancialDocument? previous)
    {
        if (previous is null) return;
        var targets = desired.Budgets.ToDictionary(item => item.Id);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Budgets WHERE budget_id=$id;";
        var id = command.Parameters.Add("$id", SqliteType.Text);
        foreach (var budget in previous.Budgets)
            if (!targets.TryGetValue(budget.Id, out var target) || !SameBudget(budget, target))
            {
                id.Value = budget.Id.ToString("D");
                command.ExecuteNonQuery();
            }
    }

    private static void WriteAnalysis(SqliteConnection connection, SqliteTransaction transaction,
        FinancialDocument desired, FinancialDocument? previous)
    {
        var priorBudgets = (previous?.Budgets ?? []).ToDictionary(item => item.Id);
        var priorReports = (previous?.FrozenReports ?? []).ToDictionary(item => item.Id);
        var targets = desired.FrozenReports.ToDictionary(item => item.Id);
        if (priorReports.Values.Any(report => !targets.TryGetValue(report.Id, out var target) || target != report))
            throw Error(DocumentError.InvalidData, L.Get("MonetaFile.RecordedFrozenReportsAreImmutable"));

        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        foreach (var budget in desired.Budgets.Where(item => !priorBudgets.TryGetValue(item.Id, out var prior) || !SameBudget(item, prior)))
        {
            Run("INSERT INTO Budgets VALUES($id,$name,$month,$currency,$minor);", ("$id", budget.Id.ToString("D")),
                ("$name", budget.Name), ("$month", budget.Month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$currency", budget.CurrencyCode), ("$minor", budget.PlannedMinor));
            foreach (var account in budget.Accounts) Run("INSERT INTO BudgetAccounts VALUES($id,$account);",
                ("$id", budget.Id.ToString("D")), ("$account", account.ToString("D")));
            foreach (var category in budget.Categories) Run("INSERT INTO BudgetCategories VALUES($id,$category);",
                ("$id", budget.Id.ToString("D")), ("$category", category.ToString("D")));
        }

        var written = priorReports.Keys.ToHashSet();
        var pending = desired.FrozenReports.Where(item => !written.Contains(item.Id)).ToList();
        while (pending.Count != 0)
        {
            var ready = pending.Where(item => item.PredecessorId is null || written.Contains(item.PredecessorId.Value)).ToArray();
            if (ready.Length == 0) throw Error(DocumentError.InvalidData, L.Get("MonetaFile.VersionsOfTheFrozenInconsistentReport"));
            foreach (var report in ready)
            {
                if (report.DraftPayload is not { } payload) throw Error(DocumentError.InvalidData, L.Get("MonetaFile.ProofOfMissingFrozenReport"));
                _ = report.Reproduce(payload);
                Run("INSERT INTO FrozenReports VALUES($id,$parent,$name,$utc,$hash,$payload);", ("$id", report.Id.ToString("D")),
                    ("$parent", SqlId(report.PredecessorId)), ("$name", report.Name), ("$utc", report.CapturedUtc.ToString("O", CultureInfo.InvariantCulture)),
                    ("$hash", report.Sha256), ("$payload", payload));
                written.Add(report.Id);
                pending.Remove(report);
            }
        }

        void Run(string sql, params (string Name, object Value)[] values)
        {
            write.CommandText = sql;
            write.Parameters.Clear();
            foreach (var (name, value) in values) write.Parameters.AddWithValue(name, value);
            write.ExecuteNonQuery();
        }
    }
}
