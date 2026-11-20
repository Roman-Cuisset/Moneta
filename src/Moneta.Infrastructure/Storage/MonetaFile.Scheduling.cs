using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string SchedulePlansSql = """
        CREATE TABLE SchedulePlans (
            plan_id TEXT NOT NULL PRIMARY KEY,
            account_id TEXT NOT NULL REFERENCES Accounts(account_id),
            destination_id TEXT REFERENCES Accounts(account_id),
            fee_account_id TEXT REFERENCES Accounts(account_id),
            payload TEXT NOT NULL CHECK(length(payload) BETWEEN 1 AND 32768)
        ) STRICT
        """;
    private const string ScheduleDecisionsSql = """
        CREATE TABLE ScheduleDecisions (
            plan_id TEXT NOT NULL REFERENCES SchedulePlans(plan_id),
            occurrence INTEGER NOT NULL CHECK(occurrence>0),
            step INTEGER NOT NULL CHECK(step>0),
            outcome INTEGER NOT NULL CHECK(outcome BETWEEN 0 AND 2),
            operation_id TEXT UNIQUE REFERENCES Transactions(transaction_id),
            transfer_id TEXT UNIQUE REFERENCES Transfers(transfer_id),
            account_id TEXT NOT NULL REFERENCES Accounts(account_id),
            destination_id TEXT REFERENCES Accounts(account_id),
            fee_account_id TEXT REFERENCES Accounts(account_id),
            payload TEXT NOT NULL CHECK(length(payload) BETWEEN 1 AND 32768),
            PRIMARY KEY(plan_id,occurrence,step),
            CHECK((outcome=2 AND (operation_id IS NULL)<>(transfer_id IS NULL)) OR
                  (outcome<>2 AND operation_id IS NULL AND transfer_id IS NULL))
        ) STRICT
        """;
    private const string FinalScheduleIndex = "CREATE UNIQUE INDEX ScheduleDecisions_Final ON ScheduleDecisions(plan_id,occurrence) WHERE outcome<>0";
    private const string SchedulingSql = SchedulePlansSql + ";" + ScheduleDecisionsSql + ";" + FinalScheduleIndex + ";";
    private static readonly JsonSerializerOptions ScheduleJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static void UpgradeV6(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 6) throw Error(DocumentError.UnsupportedVersion, "Le dossier source doit être en version 6.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, SchedulingSql + "UPDATE Document SET schema_version=7,min_reader=7,min_writer=7; PRAGMA user_version=7;", transaction);
        InsertHistory(connection, transaction, 7, 6, "v6-to-v7-schedule", "schedule-and-decisions-v7");
        VerifySnapshot(original with { SchemaVersion = 7 }, ReadWithinTransaction(connection, true, transaction));
        transaction.Commit();
    }

    private static void AddScheduleSchema(Dictionary<string, string> tables, Dictionary<string, string> indexes,
        Dictionary<string, (string Table, string Sql)> declared)
    {
        tables.Add("SchedulePlans", SchedulePlansSql);
        tables.Add("ScheduleDecisions", ScheduleDecisionsSql);
        indexes.Add("sqlite_autoindex_SchedulePlans_1", "SchedulePlans");
        for (var i = 1; i <= 3; i++) indexes.Add($"sqlite_autoindex_ScheduleDecisions_{i}", "ScheduleDecisions");
        declared.Add("ScheduleDecisions_Final", ("ScheduleDecisions", FinalScheduleIndex));
    }

    private static FinancialDocument ReadSchedule(SqliteConnection connection, FinancialDocument snapshot, SqliteTransaction transaction)
    {
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        var plans = new List<SchedulePlan>();
        query.CommandText = "SELECT plan_id,account_id,destination_id,fee_account_id,payload FROM SchedulePlans ORDER BY plan_id;";
        using (var reader = query.ExecuteReader())
            while (reader.Read())
            {
                var plan = ReadSchedulePayload<SchedulePlan>(reader.GetString(4));
                if (plan.Id != ReadGuid(reader.GetString(0)) || !MatchesAccounts(reader, 1, plan.Terms))
                    throw ScheduleMismatch();
                plans.Add(plan);
            }
        var events = new List<ScheduleDecision>();
        query.CommandText = """
            SELECT d.plan_id,d.occurrence,d.step,d.outcome,d.operation_id,d.transfer_id,
                   d.account_id,d.destination_id,d.fee_account_id,d.payload,l.transfer_id
            FROM ScheduleDecisions d LEFT JOIN TransferLegs l ON l.transaction_id=d.operation_id ORDER BY d.plan_id,d.occurrence,d.step;
            """;
        using (var reader = query.ExecuteReader())
            while (reader.Read())
            {
                var item = ReadSchedulePayload<ScheduleDecision>(reader.GetString(9));
                if (ReadGuid(reader.GetString(0)) != item.PlanId || reader.GetInt64(1) != item.Number ||
                    reader.GetInt64(2) != item.Step || reader.GetInt64(3) != (int)item.Outcome ||
                    NullableId(reader, 4) != item.TransactionId || NullableId(reader, 5) != item.TransferId ||
                    !MatchesAccounts(reader, 6, item.Terms) || !reader.IsDBNull(10))
                    throw ScheduleMismatch();
                events.Add(item);
            }
        var full = snapshot with { Schedules = plans.AsReadOnly(), ScheduleHistory = events.AsReadOnly() };
        ValidateSchedule(full);
        return full;
    }

    private static T ReadSchedulePayload<T>(string payload) where T : class =>
        JsonSerializer.Deserialize<T>(payload, ScheduleJson) ?? throw ScheduleMismatch();

    private static Guid? NullableId(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : ReadGuid(reader.GetString(column));

    private static bool MatchesAccounts(SqliteDataReader reader, int start, ScheduleTerms? terms) =>
        terms is not null && NullableId(reader, start) == terms.AccountId &&
        NullableId(reader, start + 1) == terms.DestinationId && NullableId(reader, start + 2) == terms.FeeAccountId;

    private static DocumentException ScheduleMismatch() =>
        Error(DocumentError.Corrupt, "Les données de l’échéancier et leurs liens sont incohérents.");

    private static void ValidateSchedule(FinancialDocument snapshot)
    {
        var accounts = snapshot.Accounts.ToDictionary(account => account.Id);
        var plans = snapshot.Schedules.ToDictionary(plan => plan.Id);
        foreach (var plan in snapshot.Schedules)
        {
            if (plan.Rule is null || plan.Terms is null) throw ScheduleMismatch();
            plan.Validate(accounts);
        }

        var byPlan = snapshot.ScheduleHistory.ToLookup(item => item.PlanId);
        if (byPlan.Any(group => !plans.ContainsKey(group.Key))) throw ScheduleMismatch();
        foreach (var plan in snapshot.Schedules)
        {
            long expectedNumber = 1;
            var lastOriginal = DateOnly.MinValue;
            foreach (var series in byPlan[plan.Id].GroupBy(item => item.Number).OrderBy(items => items.Key))
            {
                if (series.Key != expectedNumber++) throw ScheduleMismatch();
                ScheduleDecision? previous = null;
                foreach (var item in series.OrderBy(value => value.Step))
                {
                    if (item.Terms is null || item.Step != (previous?.Step ?? 0) + 1 ||
                        item.OriginalDate == default || item.Date == default || item.RecordedUtc == default ||
                        item.RecordedUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(item.Outcome))
                        throw ScheduleMismatch();
                    if (previous is null)
                    {
                        if (item.OriginalDate <= lastOriginal) throw ScheduleMismatch();
                        lastOriginal = item.OriginalDate;
                    }
                    if (previous is not null && (previous.Outcome != ScheduleOutcome.Deferred ||
                        previous.OriginalDate != item.OriginalDate || previous.Terms.Kind != item.Terms.Kind ||
                        previous.Terms.AccountId != item.Terms.AccountId || previous.Terms.DestinationId != item.Terms.DestinationId))
                        throw ScheduleMismatch();
                    var posted = item.Outcome == ScheduleOutcome.Posted;
                    var transfer = item.Terms.Kind == ScheduleKind.Transfer;
                    if ((item.TransactionId is not null) != (posted && !transfer) ||
                        (item.TransferId is not null) != (posted && transfer) ||
                        (item.RateSource is not null && !(posted && transfer)))
                        throw ScheduleMismatch();
                    if (item.Outcome == ScheduleOutcome.Deferred && item.Date <= (previous?.Date ?? item.OriginalDate) ||
                        item.Outcome == ScheduleOutcome.Skipped && item.Date != (previous?.Date ?? item.OriginalDate))
                        throw ScheduleMismatch();
                    item.Terms.Validate(accounts, posting: posted);
                    previous = item;
                }
            }
            if (plan.NextNumber != expectedNumber || plan.Next <= lastOriginal) throw ScheduleMismatch();
        }
    }

    private static void WriteSchedule(SqliteConnection connection, SqliteTransaction transaction, FinancialDocument desired,
        FinancialDocument? previous, IReadOnlyCollection<TransactionChange> changes, IReadOnlyCollection<TransferChange> transfers)
    {
        var targets = desired.Schedules.ToDictionary(plan => plan.Id);
        var priorPlans = (previous?.Schedules ?? []).ToDictionary(plan => plan.Id);
        if (priorPlans.Values.Any(plan => !targets.TryGetValue(plan.Id, out var target) || (plan.Retired && target != plan)))
            throw Error(DocumentError.InvalidData, "Une programmation historique ne peut pas être effacée ou réactivée.");
        var priorEvents = (previous?.ScheduleHistory ?? []).ToDictionary(item => (item.PlanId, item.Number, item.Step));
        var targetEvents = desired.ScheduleHistory.ToDictionary(item => (item.PlanId, item.Number, item.Step));
        if (priorEvents.Any(entry => !targetEvents.TryGetValue(entry.Key, out var target) || target != entry.Value))
            throw Error(DocumentError.InvalidData, "Une décision historique de l’échéancier ne peut pas être modifiée.");

        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = """
            INSERT INTO SchedulePlans(plan_id,account_id,destination_id,fee_account_id,payload)
            VALUES($plan,$account,$destination,$fee,$payload)
            ON CONFLICT(plan_id) DO UPDATE SET account_id=excluded.account_id,destination_id=excluded.destination_id,
              fee_account_id=excluded.fee_account_id,payload=excluded.payload;
            """;
        foreach (var plan in desired.Schedules.Where(plan => !priorPlans.TryGetValue(plan.Id, out var original) || original != plan))
        {
            SetScheduleParameters(write, plan.Id, plan.Terms, plan);
            write.ExecuteNonQuery();
        }

        var inserted = changes.Where(change => change.ExpectedRevision == 0).ToDictionary(change => change.Value.Id, change => change.Value);
        var linked = transfers.Where(change => change.ExpectedRevision == 0).ToDictionary(change => change.Value.Id, change => change.Value);
        var legs = linked.Values.SelectMany(value => new[] { value.DebitTransactionId, value.CreditTransactionId, value.FeeTransactionId ?? Guid.Empty }).ToHashSet();
        write.CommandText = """
            INSERT INTO ScheduleDecisions(plan_id,occurrence,step,outcome,operation_id,transfer_id,
                account_id,destination_id,fee_account_id,payload)
            VALUES($plan,$number,$step,$outcome,$operation,$transfer,$account,$destination,$fee,$payload);
            """;
        foreach (var (key, decision) in targetEvents)
        {
            if (priorEvents.ContainsKey(key)) continue;
            if (decision.Outcome == ScheduleOutcome.Posted)
                ValidateSchedulePosting(decision, inserted, linked, legs);
            SetScheduleParameters(write, decision.PlanId, decision.Terms, decision);
            write.Parameters.AddWithValue("$number", decision.Number);
            write.Parameters.AddWithValue("$step", decision.Step);
            write.Parameters.AddWithValue("$outcome", (int)decision.Outcome);
            write.Parameters.AddWithValue("$operation", SqlId(decision.TransactionId));
            write.Parameters.AddWithValue("$transfer", SqlId(decision.TransferId));
            write.ExecuteNonQuery();
        }
    }

    private static void ValidateSchedulePosting(ScheduleDecision decision, Dictionary<Guid, Transaction> operations,
        Dictionary<Guid, Transfer> transfers, HashSet<Guid> legs)
    {
        var terms = decision.Terms;
        bool Matches(Guid id, Guid? account, long minor, TransactionDirection direction) =>
            operations.TryGetValue(id, out var value) && !value.IsDeleted && value.AccountId == account &&
            value.Direction == direction && value.Date == decision.Date && ExactMoney.ToMinorUnits(value.Amount, value.Currency) == minor;

        var valid = decision.TransactionId is { } id
            ? !legs.Contains(id) && Matches(id, terms.AccountId, terms.AmountMinor,
                terms.Kind == ScheduleKind.Income ? TransactionDirection.Credit : TransactionDirection.Debit)
            : decision.TransferId is { } transferId && transfers.TryGetValue(transferId, out var transfer) &&
                !transfer.IsDeleted && transfer.Date == decision.Date && transfer.RateSource == decision.RateSource &&
                Matches(transfer.DebitTransactionId, terms.AccountId, terms.AmountMinor, TransactionDirection.Debit) &&
                Matches(transfer.CreditTransactionId, terms.DestinationId, terms.DestinationMinor ?? terms.AmountMinor, TransactionDirection.Credit) &&
                (terms.FeeAccountId is null ? transfer.FeeTransactionId is null
                    : transfer.FeeTransactionId is { } fee && Matches(fee, terms.FeeAccountId, terms.FeeMinor, TransactionDirection.Debit));
        if (!valid) throw Error(DocumentError.InvalidData, "L’occurrence exige une nouvelle écriture ou un nouveau virement complet dans la même transaction.");
    }

    private static void SetScheduleParameters<T>(SqliteCommand command, Guid plan, ScheduleTerms terms, T payload)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$plan", plan.ToString("D"));
        command.Parameters.AddWithValue("$account", terms.AccountId.ToString("D"));
        command.Parameters.AddWithValue("$destination", SqlId(terms.DestinationId));
        command.Parameters.AddWithValue("$fee", SqlId(terms.FeeAccountId));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload, ScheduleJson));
    }

    private static object SqlId(Guid? id) => (object?)id?.ToString("D") ?? DBNull.Value;
}
