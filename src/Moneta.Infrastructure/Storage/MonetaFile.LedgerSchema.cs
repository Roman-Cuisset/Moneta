using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string CategoriesSql = """
        CREATE TABLE Categories (
            category_id TEXT NOT NULL PRIMARY KEY,
            parent_id TEXT REFERENCES Categories(category_id),
            name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 120),
            name_key TEXT NOT NULL CHECK(length(name_key)>0),
            ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal>=0),
            UNIQUE(category_id,parent_id),
            CHECK(parent_id IS NULL OR parent_id<>category_id)
        ) STRICT
        """;
    private const string PayeesSql = """
        CREATE TABLE Payees (
            payee_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 120),
            name_key TEXT NOT NULL CHECK(length(name_key)>0),
            ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal>=0)
        ) STRICT
        """;
    private const string MethodsSql = """
        CREATE TABLE PaymentMethods (
            payment_method_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 80),
            name_key TEXT NOT NULL CHECK(length(name_key)>0),
            ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal>=0)
        ) STRICT
        """;
    private const string TransactionsSql = """
        CREATE TABLE Transactions (
            transaction_id TEXT NOT NULL PRIMARY KEY CHECK(length(transaction_id)=36 AND transaction_id=lower(transaction_id)),
            account_id TEXT NOT NULL REFERENCES Accounts(account_id),
            native_code TEXT NOT NULL REFERENCES Currencies(code),
            date TEXT NOT NULL CHECK(length(date)=10 AND date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date BETWEEN '0001-01-01' AND '9999-12-31' AND date(date,'+0 days') IS NOT NULL AND date(date,'+0 days')=date),
            amount_minor INTEGER NOT NULL CHECK(amount_minor>=0),
            direction INTEGER NOT NULL CHECK(direction IN (0,1)),
            status INTEGER NOT NULL CHECK(status IN (0,1,2)),
            payee_id TEXT REFERENCES Payees(payee_id),
            category_id TEXT REFERENCES Categories(category_id),
            subcategory_id TEXT REFERENCES Categories(category_id),
            payment_method_id TEXT REFERENCES PaymentMethods(payment_method_id),
            label TEXT NOT NULL CHECK(length(label) BETWEEN 1 AND 240),
            notes TEXT NOT NULL CHECK(length(notes)<=2000),
            is_deleted INTEGER NOT NULL CHECK(is_deleted IN (0,1)),
            created_utc TEXT NOT NULL,
            modified_utc TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK(revision>=1),
            FOREIGN KEY(account_id,native_code) REFERENCES Accounts(account_id,native_code),
            FOREIGN KEY(subcategory_id,category_id) REFERENCES Categories(category_id,parent_id),
            CHECK(subcategory_id IS NULL OR category_id IS NOT NULL)
        ) STRICT
        """;

    private static readonly IReadOnlyDictionary<string, string> LedgerTables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Categories"] = CategoriesSql, ["Payees"] = PayeesSql,
        ["PaymentMethods"] = MethodsSql, ["Transactions"] = TransactionsSql
    };
    private static readonly Dictionary<string, (string Table, string Sql)> LedgerIndexes = new(StringComparer.Ordinal)
    {
        ["Accounts_NativeCurrency"] = ("Accounts", "CREATE UNIQUE INDEX Accounts_NativeCurrency ON Accounts(account_id,native_code)"),
        ["Categories_RootNames"] = ("Categories", "CREATE UNIQUE INDEX Categories_RootNames ON Categories(name_key) WHERE parent_id IS NULL"),
        ["Categories_ChildNames"] = ("Categories", "CREATE UNIQUE INDEX Categories_ChildNames ON Categories(parent_id,name_key) WHERE parent_id IS NOT NULL"),
        ["Payees_Names"] = ("Payees", "CREATE UNIQUE INDEX Payees_Names ON Payees(name_key)"),
        ["PaymentMethods_Names"] = ("PaymentMethods", "CREATE UNIQUE INDEX PaymentMethods_Names ON PaymentMethods(name_key)"),
        ["Transactions_AccountOrder"] = ("Transactions", "CREATE INDEX Transactions_AccountOrder ON Transactions(account_id,date,created_utc,transaction_id)")
    };
    private static string LedgerSchemaSql => string.Join(";\n", LedgerTables.Values.Concat(LedgerIndexes.Values.Select(value => value.Sql))) + ";";

    internal static void UpgradeV2(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 2) throw Error(DocumentError.UnsupportedVersion, "Seul un dossier v2 peut recevoir le registre v3.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, LedgerSchemaSql + "UPDATE Document SET schema_version=3,min_reader=3,min_writer=3; PRAGMA user_version=3;", transaction);
        InsertHistory(connection, transaction, 3, 2, "v2-to-v3-ledger", "normalized-ledger-v3");
        var migrated = ReadWithinTransaction(connection, false, transaction);
        if (migrated.Id != original.Id || migrated.CreatedUtc != original.CreatedUtc || migrated.ModifiedUtc != original.ModifiedUtc ||
            migrated.Name != original.Name || migrated.SelectedAccountId != original.SelectedAccountId ||
            !migrated.Accounts.SequenceEqual(original.Accounts) || !migrated.Currencies.SequenceEqual(original.Currencies) || migrated.OperationCount != 0)
            throw Error(DocumentError.VerificationFailed, "La migration a modifié les données du dossier.");
        transaction.Commit();
    }
}
