using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    private const string DocumentSql = """
        CREATE TABLE Document (
            singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton=1),
            format TEXT NOT NULL CHECK(format='Moneta'),
            document_id TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            min_reader INTEGER NOT NULL,
            min_writer INTEGER NOT NULL,
            creator_version TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            modified_utc TEXT NOT NULL,
            selected_account_id TEXT
        ) STRICT
        """;
    private const string CurrenciesSql = """
        CREATE TABLE Currencies (
            code TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            decimal_places INTEGER NOT NULL CHECK(decimal_places BETWEEN 0 AND 28),
            symbol TEXT NOT NULL,
            culture_name TEXT NOT NULL,
            use_symbol INTEGER NOT NULL CHECK(use_symbol IN (0,1)),
            symbol_before_amount INTEGER NOT NULL CHECK(symbol_before_amount IN (0,1)),
            ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal>=0)
        ) STRICT
        """;
    private const string HistorySql = """
        CREATE TABLE MigrationHistory (
            version INTEGER NOT NULL PRIMARY KEY,
            from_version INTEGER NOT NULL,
            migration_id TEXT NOT NULL,
            definition TEXT NOT NULL,
            applied_utc TEXT NOT NULL
        ) STRICT
        """;

    private static string AccountsSql(int version) => """
        CREATE TABLE Accounts (
            account_id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            type TEXT,
            native_code TEXT NOT NULL REFERENCES Currencies(code),
            opening_minor INTEGER NOT NULL CHECK(opening_minor>=-9223372036854775807),
            notes TEXT,
            created_utc TEXT NOT NULL,
            modified_utc TEXT NOT NULL,
            ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal>=0)
        """ + (version >= 2 ? ",\n    is_archived INTEGER NOT NULL CHECK(is_archived IN (0,1))" : "") +
        (version >= 6 ? ",\n    bank_name TEXT,\n    bank_reference TEXT,\n    holder TEXT" : "") + "\n) STRICT";

    internal static string SchemaSql(int version)
    {
        if (version is < 1 or > CurrentSchemaVersion) throw Error(DocumentError.UnsupportedVersion, "Schéma inconnu.");
        return string.Join(";\n", DocumentSql, CurrenciesSql, AccountsSql(version), HistorySql) + ";" +
            (version >= 4 ? LedgerSchemaSql4 : version == 3 ? LedgerSchemaSql : "") + (version >= 5 ? TransferSchemaSql : "") +
            (version >= 7 ? SchedulingSql : "");
    }

    internal static FinancialDocument ReadValidated(SqliteConnection connection, bool allowVersion1 = false)
    {
        try
        {
            // Pin all checks and reads to the same committed SQLite snapshot.
            using var transaction = connection.BeginTransaction(deferred: true);
            var snapshot = ReadWithinTransaction(connection, allowVersion1, transaction);
            transaction.Commit();
            return snapshot;
        }
        catch (Exception exception) when (exception is not DocumentException) { throw Translate(exception); }
    }

    private static FinancialDocument ReadWithinTransaction(SqliteConnection connection, bool allowVersion1, SqliteTransaction transaction)
    {
        if (Integer(connection, "PRAGMA application_id;", transaction) != ApplicationId)
            throw Error(DocumentError.InvalidFormat, "Cette base SQLite n’appartient pas à Moneta.");
        var version = Integer(connection, "PRAGMA user_version;", transaction);
        if (version is < 1 or > CurrentSchemaVersion) throw Error(DocumentError.UnsupportedVersion, "Version du dossier inconnue ou plus récente.");
        var hasProvenance = ValidateSchema(connection, (int)version, transaction);
        using (var integrity = connection.CreateCommand())
        {
            integrity.Transaction = transaction;
            integrity.CommandText = "PRAGMA integrity_check;";
            using var reader = integrity.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw Error(DocumentError.Corrupt, "L’intégrité structurelle SQLite n’est pas confirmée.");
        }
        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.Transaction = transaction;
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var reader = foreignKeys.ExecuteReader();
            if (reader.Read()) throw Error(DocumentError.Corrupt, "Référence de devise invalide.");
        }
        Guid id;
        string name;
        DateTimeOffset created, modified;
        Guid? selected;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT singleton,format,document_id,name,schema_version,min_reader,min_writer,creator_version,created_utc,modified_utc,selected_account_id FROM Document;";
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(0) != 1 || reader.GetString(1) != "Moneta" || reader.GetInt64(4) != version ||
                reader.GetInt64(5) != version || reader.GetInt64(6) != version ||
                (reader.GetString(7) != "Moneta-6" && !(version >= 7 && reader.GetString(7) == "Moneta-7")))
                throw Error(DocumentError.InvalidFormat, "Métadonnées Moneta incohérentes.");
            id = ReadGuid(reader.GetString(2));
            name = reader.GetString(3);
            created = ReadTimestamp(reader.GetString(8));
            modified = ReadTimestamp(reader.GetString(9));
            selected = reader.IsDBNull(10) ? null : ReadGuid(reader.GetString(10));
            if (reader.Read()) throw Error(DocumentError.Corrupt, "Métadonnées multiples.");
        }
        ValidateHistory(connection, (int)version, transaction, hasProvenance);
        var currencies = new List<Currency>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT code,name,decimal_places,symbol,culture_name,use_symbol,symbol_before_amount,ordinal FROM Currencies ORDER BY ordinal;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(7) != currencies.Count) throw Error(DocumentError.Corrupt, "Ordre des devises invalide.");
                currencies.Add(new Currency(reader.GetString(0), reader.GetString(1), checked((int)reader.GetInt64(2)))
                {
                    Symbol = reader.GetString(3),
                    DisplayRules = new(reader.GetString(4), ReadBoolean(reader, 5), ReadBoolean(reader, 6))
                });
            }
        }
        var byCode = currencies.ToDictionary(currency => currency.Code, StringComparer.Ordinal);
        var accounts = new List<Account>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal," +
                (version >= 2 ? "is_archived" : "0") + (version >= 6 ? ",bank_name,bank_reference,holder" : ",NULL,NULL,NULL") + " FROM Accounts ORDER BY ordinal;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(8) != accounts.Count) throw Error(DocumentError.Corrupt, "Ordre des comptes invalide.");
                var currency = byCode[reader.GetString(3)];
                accounts.Add(new Account(ReadGuid(reader.GetString(0)), reader.GetString(1), currency, ExactMoney.FromMinorUnits(reader.GetInt64(4), currency))
                {
                    Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedUtc = ReadTimestamp(reader.GetString(6)),
                    ModifiedUtc = ReadTimestamp(reader.GetString(7)),
                    IsArchived = ReadBoolean(reader, 9),
                    BankName = reader.IsDBNull(10) ? null : reader.GetString(10),
                    BankReference = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Holder = reader.IsDBNull(12) ? null : reader.GetString(12)
                });
            }
        }
        var document = new FinancialDocument(id, name, created, modified, currencies.AsReadOnly(), accounts.AsReadOnly()) { SchemaVersion = (int)version, SelectedAccountId = selected };
        if (version >= 3) document = ReadCatalog(connection, document, transaction);
        try { ValidateSnapshot(document, allowLegacy: true); }
        catch (DocumentException exception) when (exception.Error == DocumentError.InvalidData)
        {
            throw new DocumentException(DocumentError.Corrupt, "Les données enregistrées violent les invariants du dossier.", exception);
        }
        if (version >= 3) document = ReadLedgerMetadata(connection, document, transaction);
        if (hasProvenance)
            document = document with { BankPerfectProvenanceHash = VerifyBankPerfectProvenance(connection, transaction) };
        if (version >= 7) document = ReadSchedule(connection, document, transaction);
        if (version < CurrentSchemaVersion && !allowVersion1) throw Error(DocumentError.MigrationRequired, "Ce dossier doit être migré sur une nouvelle copie avec consentement.");
        return Freeze(document);
    }

    private static bool ValidateSchema(SqliteConnection connection, int version, SqliteTransaction transaction)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Document"] = DocumentSql,
            ["Currencies"] = CurrenciesSql,
            ["Accounts"] = AccountsSql(version),
            ["MigrationHistory"] = HistorySql
        };
        if (version >= 3)
            foreach (var entry in LedgerTables) expected.Add(entry.Key, version >= 4 && entry.Key == "Transactions" ? TransactionsSql4 : entry.Value);
        if (version >= 4)
            foreach (var entry in ReconciliationTables) expected.Add(entry.Key, entry.Value);
        if (version >= 5)
            foreach (var entry in TransferTables) expected.Add(entry.Key, entry.Value);
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlite_autoindex_Document_1"] = "Document",
            ["sqlite_autoindex_Currencies_1"] = "Currencies",
            ["sqlite_autoindex_Currencies_2"] = "Currencies",
            ["sqlite_autoindex_Accounts_1"] = "Accounts",
            ["sqlite_autoindex_Accounts_2"] = "Accounts"
        };
        if (version >= 3)
            foreach (var table in new[] { "Categories", "Payees", "PaymentMethods", "Transactions" })
            {
                indexes.Add($"sqlite_autoindex_{table}_1", table);
                if (table != "Transactions") indexes.Add($"sqlite_autoindex_{table}_2", table);
                if (table == "Categories") indexes.Add("sqlite_autoindex_Categories_3", table);
            }
        if (version >= 4)
            foreach (var table in ReconciliationTables.Keys) indexes.Add($"sqlite_autoindex_{table}_1", table);
        if (version >= 5)
        {
            indexes.Add("sqlite_autoindex_Transfers_1", "Transfers");
            indexes.Add("sqlite_autoindex_TransferLegs_1", "TransferLegs");
            indexes.Add("sqlite_autoindex_TransferLegs_2", "TransferLegs");
        }
        var declaredIndexes = version >= 3 ? new Dictionary<string, (string Table, string Sql)>(LedgerIndexes, StringComparer.Ordinal) : new Dictionary<string, (string Table, string Sql)>();
        if (version >= 4)
            foreach (var entry in ReconciliationIndexes) declaredIndexes.Add(entry.Key, entry.Value);
        if (version >= 7) AddScheduleSchema(expected, indexes, declaredIndexes);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_schema ORDER BY name;";
        using var reader = command.ExecuteReader();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenIndexes = new HashSet<string>(StringComparer.Ordinal);
        var hasProvenance = false;
        while (reader.Read())
        {
            var type = reader.GetString(0);
            var name = reader.GetString(1);
            if (version >= 5 && type == "table" && name == "BankPerfectProvenance" &&
                reader.GetString(2) == name && !reader.IsDBNull(3) &&
                CanonicalSql(reader.GetString(3)) == CanonicalSql(BankPerfectProvenanceSql))
            {
                hasProvenance = true;
                continue;
            }
            // Only the exact indexes generated by the known UNIQUE/PRIMARY KEY constraints are allowed.
            if (type == "index" && reader.IsDBNull(3) && indexes.TryGetValue(name, out var table) && reader.GetString(2) == table && seenIndexes.Add(name)) continue;
            if (type == "index" && !reader.IsDBNull(3) && declaredIndexes.TryGetValue(name, out var declared) &&
                reader.GetString(2) == declared.Table && CanonicalSql(reader.GetString(3)) == CanonicalSql(declared.Sql) && seenIndexes.Add(name)) continue;
            if (type != "table" || !expected.TryGetValue(name, out var sql) || reader.GetString(2) != name || reader.IsDBNull(3) || CanonicalSql(reader.GetString(3)) != CanonicalSql(sql) || !seen.Add(name))
                throw Error(DocumentError.InvalidFormat, "Schéma non reconnu : table, index, vue ou déclencheur inattendu.");
        }
        if (seen.Count != expected.Count || seenIndexes.Count != indexes.Count + declaredIndexes.Count) throw Error(DocumentError.InvalidFormat, "Schéma Moneta incomplet.");
        return hasProvenance;
    }

    // Ignore formatting outside literals only; constraints and literal values remain exact.
    private static string CanonicalSql(string sql)
    {
        var result = new StringBuilder(sql.Length);
        var quoted = false;
        foreach (var character in sql.Trim().TrimEnd(';'))
        {
            if (character == '\'') quoted = !quoted;
            if (quoted || !char.IsWhiteSpace(character)) result.Append(character);
        }
        return result.ToString();
    }

    internal static void ValidateSnapshot(FinancialDocument snapshot) => ValidateSnapshot(snapshot, false);

    private static void ValidateSnapshot(FinancialDocument snapshot, bool allowLegacy)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.SchemaVersion != CurrentSchemaVersion && !(allowLegacy && snapshot.SchemaVersion is >= 1 and <= 6)) throw new ArgumentException("Version du snapshot invalide.");
            if (snapshot.SchemaVersion >= 7) ValidateSchedule(snapshot);
            if (snapshot.Id == Guid.Empty) throw new ArgumentException("Identifiant du dossier vide.");
            ValidateText(snapshot.Name, true);
            ValidateTimes(snapshot.CreatedUtc, snapshot.ModifiedUtc);
            if (snapshot.Currencies is null || snapshot.Accounts is null) throw new ArgumentException("Collections du dossier absentes.");
            var registry = new CurrencyRegistry(snapshot.Currencies);
            if (registry.Currencies.Count != snapshot.Currencies.Count) throw new ArgumentException("Code devise dupliqué.");
            foreach (var currency in snapshot.Currencies)
            {
                ValidateText(currency.Name, true);
                ValidateText(currency.Symbol, true);
                ValidateText(currency.DisplayRules.CultureName, false);
            }
            var ids = new HashSet<Guid>();
            foreach (var account in snapshot.Accounts)
            {
                if (account is null || account.Id == Guid.Empty || !ids.Add(account.Id)) throw new ArgumentException("Identifiant du compte vide ou dupliqué.");
                ValidateText(account.Name, true);
                ValidateText(account.Type, false);
                ValidateText(account.Notes, false);
                ValidateAccountField(account.BankName, 120);
                ValidateAccountField(account.BankReference, 120);
                ValidateAccountField(account.Holder, 120);
                ValidateTimes(account.CreatedUtc, account.ModifiedUtc);
                if (registry.Get(account.Currency.Code) != account.Currency) throw new ArgumentException("Définition de devise du compte incohérente.");
                _ = ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency);
            }
            if (snapshot.SelectedAccountId is { } selected && !ids.Contains(selected)) throw new ArgumentException("Compte sélectionné absent.");
            FinancialRules.ValidateCatalog(snapshot);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or NullReferenceException)
        {
            throw new DocumentException(DocumentError.InvalidData, "Données du dossier invalides : " + exception.Message, exception);
        }
    }

    private static void ValidateText(string? value, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > 1_000_000 || value?.Contains('\0') == true)
            throw new ArgumentException("Texte vide, trop long ou contenant NUL.");
    }

    private static void ValidateAccountField(string? value, int maxLength)
    {
        if (value?.Length > maxLength || value?.Contains('\0') == true)
            throw new ArgumentException("Information bancaire trop longue ou contenant NUL.");
    }

    private static void ValidateTimes(DateTimeOffset created, DateTimeOffset modified)
    {
        if (created == default || modified == default || created.Offset != TimeSpan.Zero || modified.Offset != TimeSpan.Zero || modified < created)
            throw new ArgumentException("Horodatages UTC chronologiques requis.");
    }

    private static Guid ReadGuid(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty || id.ToString("D") != value) throw Error(DocumentError.Corrupt, "UUID non canonique.");
        return id;
    }

    private static DateTimeOffset ReadTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp.Offset != TimeSpan.Zero || timestamp.ToString("O", CultureInfo.InvariantCulture) != value)
            throw Error(DocumentError.Corrupt, "Horodatage UTC non canonique.");
        return timestamp;
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) switch
    {
        0 => false,
        1 => true,
        _ => throw Error(DocumentError.Corrupt, "Booléen invalide.")
    };

    private static void ValidateHistory(SqliteConnection connection, int version, SqliteTransaction transaction, bool hasProvenance)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version,from_version,migration_id,definition,applied_utc FROM MigrationHistory ORDER BY version;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw Error(DocumentError.Corrupt, "Historique de schéma absent.");
        var first = reader.GetInt64(0);
        var definition = $"native-accounts-v{first}" + (hasProvenance ? "+bankperfect-v1" : "");
        if (reader.GetInt64(1) != 0 || first < 1 || first > version ||
            (hasProvenance && first < 5) || reader.GetString(2) != $"create-v{first}" || reader.GetString(3) != definition)
            throw Error(DocumentError.Corrupt, "Origine du schéma inconnue.");
        _ = ReadTimestamp(reader.GetString(4));
        if (first == 1 && version >= 2)
        {
            if (!reader.Read() || reader.GetInt64(0) != 2 || reader.GetInt64(1) != 1 || reader.GetString(2) != "v1-to-v2-archive" || reader.GetString(3) != "add-is-archived-default-false")
                throw Error(DocumentError.Corrupt, "Historique de migration incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (first <= 2 && version >= 3)
        {
            if (!reader.Read() || reader.GetInt64(0) != 3 || reader.GetInt64(1) != 2 || reader.GetString(2) != "v2-to-v3-ledger" || reader.GetString(3) != "normalized-ledger-v3")
                throw Error(DocumentError.Corrupt, "Historique de migration du registre incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (first <= 3 && version >= 4)
        {
            if (!reader.Read() || reader.GetInt64(0) != 4 || reader.GetInt64(1) != 3 || reader.GetString(2) != "v3-to-v4-reconciliation" || reader.GetString(3) != "pointing-and-statement-history-v4")
                throw Error(DocumentError.Corrupt, "Historique du rapprochement incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (first <= 4 && version >= 5)
        {
            if (!reader.Read() || reader.GetInt64(0) != 5 || reader.GetInt64(1) != 4 || reader.GetString(2) != "v4-to-v5-transfers" || reader.GetString(3) != "linked-transfer-legs-v5")
                throw Error(DocumentError.Corrupt, "Historique des virements incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (first <= 5 && version >= 6)
        {
            if (!reader.Read() || reader.GetInt64(0) != 6 || reader.GetInt64(1) != 5 || reader.GetString(2) != "v5-to-v6-account-properties" || reader.GetString(3) != "account-descriptions-v6")
                throw Error(DocumentError.Corrupt, "Historique des propriétés de comptes incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (first <= 6 && version >= 7)
        {
            if (!reader.Read() || reader.GetInt64(0) != 7 || reader.GetInt64(1) != 6 || reader.GetString(2) != "v6-to-v7-schedule" || reader.GetString(3) != "schedule-and-decisions-v7")
                throw Error(DocumentError.Corrupt, "Historique de migration de l’échéancier incomplet.");
            _ = ReadTimestamp(reader.GetString(4));
        }
        if (reader.Read()) throw Error(DocumentError.Corrupt, "Migration inconnue.");
    }

    private static void InsertHistory(SqliteConnection connection, SqliteTransaction transaction, int version, int from, string id, string definition)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO MigrationHistory(version,from_version,migration_id,definition,applied_utc) VALUES($version,$from,$id,$definition,$utc);";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$definition", definition);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    internal static void UpgradeV1(SqliteConnection connection)
    {
        var original = ReadValidated(connection, true);
        if (original.SchemaVersion != 1) throw Error(DocumentError.UnsupportedVersion, "Seul le brouillon interne v1 peut être migré.");
        using var transaction = connection.BeginTransaction();
        Execute(connection, "ALTER TABLE Accounts RENAME TO Accounts_v1;" + AccountsSql(2) + "; INSERT INTO Accounts(account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal,is_archived) SELECT account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal,0 FROM Accounts_v1; DROP TABLE Accounts_v1; UPDATE Document SET schema_version=2,min_reader=2,min_writer=2; PRAGMA user_version=2;", transaction);
        InsertHistory(connection, transaction, 2, 1, "v1-to-v2-archive", "add-is-archived-default-false");
        _ = ReadWithinTransaction(connection, true, transaction);
        transaction.Commit();
    }

    private static void WriteSnapshot(SqliteConnection connection, FinancialDocument snapshot, SqliteTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Document(singleton,format,document_id,name,schema_version,min_reader,min_writer,creator_version,created_utc,modified_utc,selected_account_id) VALUES(1,'Moneta',$id,$name,$version,$version,$version,'Moneta-7',$created,$modified,$selected) ON CONFLICT(singleton) DO UPDATE SET name=excluded.name,modified_utc=excluded.modified_utc,selected_account_id=excluded.selected_account_id;";
            command.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            command.Parameters.AddWithValue("$id", snapshot.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", snapshot.Name);
            command.Parameters.AddWithValue("$created", snapshot.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$modified", snapshot.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$selected", (object?)snapshot.SelectedAccountId?.ToString("D") ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Currencies(code,name,decimal_places,symbol,culture_name,use_symbol,symbol_before_amount,ordinal) VALUES($code,$name,$places,$symbol,$culture,$use,$before,$ordinal) ON CONFLICT(code) DO UPDATE SET name=excluded.name,symbol=excluded.symbol,culture_name=excluded.culture_name,use_symbol=excluded.use_symbol,symbol_before_amount=excluded.symbol_before_amount,ordinal=excluded.ordinal;";
            var code = command.Parameters.Add("$code", SqliteType.Text);
            var name = command.Parameters.Add("$name", SqliteType.Text);
            var places = command.Parameters.Add("$places", SqliteType.Integer);
            var symbol = command.Parameters.Add("$symbol", SqliteType.Text);
            var culture = command.Parameters.Add("$culture", SqliteType.Text);
            var use = command.Parameters.Add("$use", SqliteType.Integer);
            var before = command.Parameters.Add("$before", SqliteType.Integer);
            var ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
            // Shift above the complete final range before any inserts/reordering.
            using (var shift = connection.CreateCommand())
            {
                shift.Transaction = transaction;
                shift.CommandText = "UPDATE Currencies SET ordinal=ordinal+$currencies; UPDATE Accounts SET ordinal=ordinal+$accounts;";
                shift.Parameters.AddWithValue("$currencies", snapshot.Currencies.Count);
                shift.Parameters.AddWithValue("$accounts", snapshot.Accounts.Count);
                shift.ExecuteNonQuery();
            }
            for (var index = 0; index < snapshot.Currencies.Count; index++)
            {
                var currency = snapshot.Currencies[index];
                code.Value = currency.Code; name.Value = currency.Name; places.Value = currency.DecimalPlaces;
                symbol.Value = currency.Symbol; culture.Value = currency.DisplayRules.CultureName;
                use.Value = currency.DisplayRules.UseSymbol ? 1 : 0; before.Value = currency.DisplayRules.SymbolBeforeAmount ? 1 : 0; ordinal.Value = index;
                command.ExecuteNonQuery();
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Accounts(account_id,name,type,native_code,opening_minor,notes,created_utc,modified_utc,ordinal,is_archived,bank_name,bank_reference,holder)
                VALUES($id,$name,$type,$currency,$minor,$notes,$created,$modified,$ordinal,$archived,$bank,$reference,$holder)
                ON CONFLICT(account_id) DO UPDATE SET name=excluded.name,type=excluded.type,
                    notes=excluded.notes,modified_utc=excluded.modified_utc,ordinal=excluded.ordinal,is_archived=excluded.is_archived,
                    bank_name=excluded.bank_name,bank_reference=excluded.bank_reference,holder=excluded.holder;
                """;
            var id = command.Parameters.Add("$id", SqliteType.Text);
            var name = command.Parameters.Add("$name", SqliteType.Text);
            var type = command.Parameters.Add("$type", SqliteType.Text);
            var currency = command.Parameters.Add("$currency", SqliteType.Text);
            var minor = command.Parameters.Add("$minor", SqliteType.Integer);
            var notes = command.Parameters.Add("$notes", SqliteType.Text);
            var created = command.Parameters.Add("$created", SqliteType.Text);
            var modified = command.Parameters.Add("$modified", SqliteType.Text);
            var ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
            var archived = command.Parameters.Add("$archived", SqliteType.Integer);
            var bank = command.Parameters.Add("$bank", SqliteType.Text);
            var reference = command.Parameters.Add("$reference", SqliteType.Text);
            var holder = command.Parameters.Add("$holder", SqliteType.Text);
            for (var index = 0; index < snapshot.Accounts.Count; index++)
            {
                var account = snapshot.Accounts[index];
                id.Value = account.Id.ToString("D"); name.Value = account.Name; type.Value = (object?)account.Type ?? DBNull.Value;
                currency.Value = account.Currency.Code; minor.Value = ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency);
                notes.Value = (object?)account.Notes ?? DBNull.Value; created.Value = account.CreatedUtc.ToString("O", CultureInfo.InvariantCulture);
                modified.Value = account.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture); ordinal.Value = index; archived.Value = account.IsArchived ? 1 : 0;
                bank.Value = (object?)account.BankName ?? DBNull.Value;
                reference.Value = (object?)account.BankReference ?? DBNull.Value;
                holder.Value = (object?)account.Holder ?? DBNull.Value;
                command.ExecuteNonQuery();
            }
        }
    }
}
