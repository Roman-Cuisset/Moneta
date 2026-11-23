using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string LegacyTransactionColumns = "transaction_id,account_id,date,label,amount_minor,native_code,direction,status,notes,payee_id,category_id,subcategory_id,payment_method_id,is_deleted,created_utc,modified_utc,revision";
    private const string TransactionColumns = LegacyTransactionColumns + ",marked_on,reconciliation_id";

    private sealed class LedgerLookup(FinancialDocument metadata)
    {
        public readonly Dictionary<Guid, Account> Accounts = metadata.Accounts.ToDictionary(value => value.Id);
        public readonly Dictionary<Guid, LedgerCategory> Categories = metadata.Categories.ToDictionary(value => value.Id);
        public readonly Dictionary<Guid, LedgerPayee> Payees = metadata.Payees.ToDictionary(value => value.Id);
        public readonly Dictionary<Guid, PaymentMethod> Methods = metadata.PaymentMethods.ToDictionary(value => value.Id);
    }

    private static FinancialDocument ReadCatalog(SqliteConnection connection, FinancialDocument metadata, SqliteTransaction transaction)
    {
        var categories = new List<LedgerCategory>();
        var payees = new List<LedgerPayee>();
        var methods = new List<PaymentMethod>();
        foreach (var table in new[] { "Categories", "Payees", "PaymentMethods" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var idColumn = table == "Categories" ? "category_id" : table == "Payees" ? "payee_id" : "payment_method_id";
            command.CommandText = $"SELECT {idColumn},name,name_key,ordinal," + (table == "Categories" ? "parent_id" : "NULL") + $" FROM {table} ORDER BY ordinal;";
            using var reader = command.ExecuteReader();
            var ordinal = 0;
            while (reader.Read())
            {
                var id = ReadGuid(reader.GetString(0));
                var name = reader.GetString(1);
                if (reader.GetString(2) != FinancialRules.NameKey(name) || reader.GetInt64(3) != ordinal++)
                    throw Error(DocumentError.Corrupt, L.Get("MonetaFile.StandardizedKeyOrInvalidCatalogOrder"));
                if (table == "Categories") categories.Add(new(id, name, reader.IsDBNull(4) ? null : ReadGuid(reader.GetString(4))));
                else if (table == "Payees") payees.Add(new(id, name));
                else methods.Add(new(id, name));
            }
        }
        return metadata with { Categories = categories.AsReadOnly(), Payees = payees.AsReadOnly(), PaymentMethods = methods.AsReadOnly() };
    }

    private static void WriteCatalog(SqliteConnection connection, FinancialDocument metadata, SqliteTransaction transaction)
    {
        // Names and ordinals can be reordered without transient unique-key collisions.
        foreach (var table in new[] { "Categories", "Payees", "PaymentMethods" })
        {
            using var shift = connection.CreateCommand();
            shift.Transaction = transaction;
            var count = table == "Categories" ? metadata.Categories.Count : table == "Payees" ? metadata.Payees.Count : metadata.PaymentMethods.Count;
            shift.CommandText = $"UPDATE {table} SET ordinal=ordinal+$count,name_key='~temporary:'||" +
                (table == "Categories" ? "category_id" : table == "Payees" ? "payee_id" : "payment_method_id") + ";";
            shift.Parameters.AddWithValue("$count", count);
            shift.ExecuteNonQuery();
        }
        foreach (var item in metadata.Categories.Select((value, index) => (value, index)).OrderBy(item => item.value.ParentId.HasValue))
            WriteReference(connection, transaction, "Categories", "category_id", item.value.Id, item.value.Name, item.index, item.value.ParentId);
        for (var index = 0; index < metadata.Payees.Count; index++)
            WriteReference(connection, transaction, "Payees", "payee_id", metadata.Payees[index].Id, metadata.Payees[index].Name, index);
        for (var index = 0; index < metadata.PaymentMethods.Count; index++)
            WriteReference(connection, transaction, "PaymentMethods", "payment_method_id", metadata.PaymentMethods[index].Id, metadata.PaymentMethods[index].Name, index);
    }

    private static void WriteReference(SqliteConnection connection, SqliteTransaction transaction, string table, string idColumn, Guid id, string name, int ordinal, Guid? parent = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table}({idColumn},name,name_key,ordinal" + (table == "Categories" ? ",parent_id" : "") + ") VALUES($id,$name,$key,$ordinal" +
            (table == "Categories" ? ",$parent" : "") + $") ON CONFLICT({idColumn}) DO UPDATE SET name=excluded.name,name_key=excluded.name_key,ordinal=excluded.ordinal;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$key", FinancialRules.NameKey(name));
        command.Parameters.AddWithValue("$ordinal", ordinal);
        if (table == "Categories") command.Parameters.AddWithValue("$parent", (object?)parent?.ToString("D") ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static FinancialDocument ReadLedgerMetadata(SqliteConnection connection, FinancialDocument metadata, SqliteTransaction transaction)
    {
        try
        {
            var lookup = new LedgerLookup(metadata);
            var balances = metadata.Accounts.ToDictionary(value => value.Id, value => value.OpeningBalance);
            var pointed = metadata.Accounts.ToDictionary(value => value.Id, value => value.OpeningBalance);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            HashValue(digest, metadata.SchemaVersion >= 4 ? "Moneta-normalized-ledger-v4" : "Moneta-normalized-ledger-v3");
            foreach (var value in metadata.Categories.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
            {
                HashValue(digest, "category"); HashValue(digest, value.Id.ToString("D")); HashValue(digest, value.Name);
                HashValue(digest, FinancialRules.NameKey(value.Name)); HashValue(digest, value.ParentId?.ToString("D"));
            }
            foreach (var value in metadata.Payees.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
            {
                HashValue(digest, "payee"); HashValue(digest, value.Id.ToString("D")); HashValue(digest, value.Name); HashValue(digest, FinancialRules.NameKey(value.Name));
            }
            foreach (var value in metadata.PaymentMethods.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal))
            {
                HashValue(digest, "method"); HashValue(digest, value.Id.ToString("D")); HashValue(digest, value.Name); HashValue(digest, FinancialRules.NameKey(value.Name));
            }
            long count = 0;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT {(metadata.SchemaVersion >= 4 ? TransactionColumns : LegacyTransactionColumns)} FROM Transactions ORDER BY transaction_id COLLATE BINARY;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var value = ReadTransaction(reader, lookup);
                FinancialRules.ValidateTransaction(value, metadata);
                HashValue(digest, "transaction");
                for (var index = 0; index < reader.FieldCount; index++)
                    HashValue(digest, reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
                if (!value.IsDeleted)
                {
                    balances[value.AccountId] = checked(balances[value.AccountId] + FinancialRules.SignedAmount(value));
                    if (value.Status != ReconciliationStatus.Unmarked) pointed[value.AccountId] = checked(pointed[value.AccountId] + FinancialRules.SignedAmount(value));
                }
                count = checked(count + 1);
            }
            reader.Close();
            var historyCounts = metadata.SchemaVersion >= 4 ? ValidateAndHashReconciliations(connection, metadata, lookup, digest, transaction) : (Sessions: 0L, Events: 0L);
            var transferCount = metadata.SchemaVersion >= 5 ? ValidateAndHashTransfers(connection, metadata, lookup, digest, transaction) : 0L;
            return metadata with
            {
                LedgerDigest = Convert.ToHexString(digest.GetHashAndReset()), OperationCount = count,
                AccountBalances = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(balances),
                AccountPointedBalances = metadata.SchemaVersion >= 4 ? new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(pointed) : null,
                ReconciliationCount = historyCounts.Sessions, ReconciliationEventCount = historyCounts.Events,
                TransferCount = transferCount
            };
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or KeyNotFoundException)
        {
            throw new DocumentException(DocumentError.Corrupt, L.Get("MonetaFile.ARegisteredTransactionViolatesFinancialInvariants"), exception);
        }
    }

    private static void HashValue(IncrementalHash digest, string? value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value is null ? -1 : Encoding.UTF8.GetByteCount(value));
        digest.AppendData(length);
        if (value is not null)
        {
            var size = Encoding.UTF8.GetByteCount(value);
            byte[]? rented = null;
            Span<byte> bytes = size <= 512 ? stackalloc byte[size] : (rented = ArrayPool<byte>.Shared.Rent(size));
            try
            {
                var written = Encoding.UTF8.GetBytes(value.AsSpan(), bytes);
                digest.AppendData(bytes[..written]);
            }
            finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
        }
    }

    private static Transaction ReadTransaction(SqliteDataReader reader, LedgerLookup lookup)
    {
        var account = lookup.Accounts[ReadGuid(reader.GetString(1))];
        if (reader.GetString(5) != account.Currency.Code) throw Error(DocumentError.Corrupt, L.Get("MonetaFile.IncoherentTransactionNativeCurrency"));
        var dateText = reader.GetString(2);
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == default || date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != dateText)
            throw Error(DocumentError.Corrupt, L.Get("MonetaFile.DateOfNonCanonicalTransaction"));
        Guid? OptionalId(int index) => reader.IsDBNull(index) ? null : ReadGuid(reader.GetString(index));
        var payee = OptionalId(9); var category = OptionalId(10); var subcategory = OptionalId(11); var method = OptionalId(12);
        return new Transaction(ReadGuid(reader.GetString(0)), account.Id, date, reader.GetString(3),
            new Payee(payee.HasValue ? lookup.Payees[payee.Value].Name : ""),
            new Category(category.HasValue ? lookup.Categories[category.Value].Name : "") { Subcategory = subcategory.HasValue ? lookup.Categories[subcategory.Value].Name : null },
            ExactMoney.FromMinorUnits(reader.GetInt64(4), account.Currency), account.Currency,
            (TransactionDirection)checked((int)reader.GetInt64(6)), (ReconciliationStatus)checked((int)reader.GetInt64(7)), reader.GetString(8))
        {
            PayeeId = payee, CategoryId = category, SubcategoryId = subcategory, PaymentMethodId = method,
            PaymentMode = method.HasValue ? lookup.Methods[method.Value].Name : null,
            IsDeleted = ReadBoolean(reader, 13), CreatedUtc = ReadTimestamp(reader.GetString(14)),
            ModifiedUtc = ReadTimestamp(reader.GetString(15)), Revision = reader.GetInt64(16),
            MarkedOn = reader.FieldCount > 17 && !reader.IsDBNull(17) ? ReadDate(reader.GetString(17)) : null,
            ReconciliationId = reader.FieldCount > 18 ? OptionalId(18) : null
        };
    }

    public Transaction? GetTransaction(Guid id, bool includeDeleted = false)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        try
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var value = FindTransaction(Connection, id, ledgerLookup, transaction);
            if (value is not null) FinancialRules.ValidateTransaction(value, Snapshot);
            transaction.Commit();
            return value is { IsDeleted: true } && !includeDeleted ? null : value;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
    }

    private static Transaction? FindTransaction(SqliteConnection connection, Guid id, LedgerLookup lookup, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {TransactionColumns} FROM Transactions WHERE transaction_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTransaction(reader, lookup) : null;
    }

    private static void ApplyChanges(SqliteConnection connection, FinancialDocument metadata, IReadOnlyCollection<TransactionChange> changes, SqliteTransaction transaction, bool creating)
    {
        try
        {
            var lookup = new LedgerLookup(metadata);
            var ids = new HashSet<Guid>();
            foreach (var change in changes)
            {
                if (change is null || change.Value is null || !ids.Add(change.Value.Id) || change.ExpectedRevision < 0 || (creating && change.ExpectedRevision != 0))
                    throw new ArgumentException(L.Get("MonetaFile.SetOfInvalidOrDuplicatedRevisions"));
                var value = change.Value;
                FinancialRules.ValidateTransaction(value, metadata);
                if (value.Revision != checked(change.ExpectedRevision + 1)) throw new ArgumentException(L.Get("MonetaFile.NextRevisionRequired"));
                var previous = FindTransaction(connection, value.Id, lookup, transaction);
                if (change.ExpectedRevision == 0)
                {
                    if (previous is not null || value.IsDeleted) throw new ArgumentException(L.Get("MonetaFile.InsertAlreadyPresentOrDeleted"));
                }
                else if (previous is null || previous.IsDeleted || previous.Revision != change.ExpectedRevision ||
                    previous.AccountId != value.AccountId || previous.Currency.Code != value.Currency.Code || previous.CreatedUtc != value.CreatedUtc || value.ModifiedUtc < previous.ModifiedUtc)
                    throw new ArgumentException(L.Get("MonetaFile.ObsoleteRevisionDeletedTransactionOrModifiedFinancialIdentity"));
                if (lookup.Accounts[value.AccountId].IsArchived) throw new ArgumentException(L.Get("MonetaFile.AnArchivedAccountCannotReceiveATransactionChange"));
                WriteTransaction(connection, value, transaction);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new DocumentException(DocumentError.InvalidData, L.Get("MonetaFile.InvalidTransactionModification") + exception.Message, exception);
        }
    }

    private static void WriteTransaction(SqliteConnection connection, Transaction value, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Transactions(" + TransactionColumns + ") VALUES($id,$account,$date,$label,$minor,$currency,$direction,$status,$notes,$payee,$category,$subcategory,$method,$deleted,$created,$modified,$revision,$marked,$reconciliation) ON CONFLICT(transaction_id) DO UPDATE SET date=excluded.date,label=excluded.label,amount_minor=excluded.amount_minor,direction=excluded.direction,status=excluded.status,notes=excluded.notes,payee_id=excluded.payee_id,category_id=excluded.category_id,subcategory_id=excluded.subcategory_id,payment_method_id=excluded.payment_method_id,is_deleted=excluded.is_deleted,modified_utc=excluded.modified_utc,revision=excluded.revision,marked_on=excluded.marked_on,reconciliation_id=excluded.reconciliation_id;";
        command.Parameters.AddWithValue("$id", value.Id.ToString("D")); command.Parameters.AddWithValue("$account", value.AccountId.ToString("D"));
        command.Parameters.AddWithValue("$date", value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$label", value.Label);
        command.Parameters.AddWithValue("$minor", ExactMoney.ToMinorUnits(value.Amount, value.Currency)); command.Parameters.AddWithValue("$currency", value.Currency.Code);
        command.Parameters.AddWithValue("$direction", (int)value.Direction); command.Parameters.AddWithValue("$status", (int)value.Status); command.Parameters.AddWithValue("$notes", value.Details);
        command.Parameters.AddWithValue("$payee", (object?)value.PayeeId?.ToString("D") ?? DBNull.Value); command.Parameters.AddWithValue("$category", (object?)value.CategoryId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$subcategory", (object?)value.SubcategoryId?.ToString("D") ?? DBNull.Value); command.Parameters.AddWithValue("$method", (object?)value.PaymentMethodId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$deleted", value.IsDeleted ? 1 : 0); command.Parameters.AddWithValue("$created", value.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$modified", value.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$revision", value.Revision);
        command.Parameters.AddWithValue("$marked", (object?)value.MarkedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$reconciliation", (object?)value.ReconciliationId?.ToString("D") ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
