using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Moneta.Core;

namespace Moneta.Infrastructure.ExchangeRates;

// This is an independent public-data cache, never a financial document or a migration target.
public sealed class SqliteRateCache : IRateCache, IHistoricalRateCache
{
    private const int ApplicationId = 0x4D465831;
    private const int SchemaVersion = 1;
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const string Columns = "id,provider,requested_date,effective_date,retrieved_utc,endpoint,sha256,payload";
    private const string TableSql = """
        CREATE TABLE RateSets (
            id TEXT NOT NULL PRIMARY KEY CHECK(length(id)=36),
            provider INTEGER NOT NULL CHECK(provider IN (0,1)),
            requested_date TEXT NOT NULL CHECK(length(requested_date)=10),
            effective_date TEXT NOT NULL CHECK(length(effective_date)=10),
            retrieved_utc TEXT NOT NULL CHECK(length(retrieved_utc)=33),
            endpoint TEXT NOT NULL CHECK(length(endpoint) BETWEEN 1 AND 2048),
            sha256 TEXT NOT NULL CHECK(length(sha256)=64),
            payload BLOB NOT NULL CHECK(length(payload) BETWEEN 1 AND 1048576)
        ) STRICT
        """;
    private const string IndexSql = "CREATE INDEX RateSets_ProviderDate ON RateSets(provider,effective_date,retrieved_utc)";

    public string FilePath { get; }

    public SqliteRateCache(string? path = null)
    {
        FilePath = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moneta", "exchange-rates.sqlite"));
    }

    public OfficialRateSet? Find(RateProvider provider, DateOnly date, int maximumAgeDays = 31,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(provider);
        if (date == default) throw new ArgumentOutOfRangeException(nameof(date));
        if (maximumAgeDays is < 0 or > RateRules.MaximumAgeDays) throw new ArgumentOutOfRangeException(nameof(maximumAgeDays));
        var earliest = DateOnly.FromDayNumber(Math.Max(0, date.DayNumber - maximumAgeDays));
        return Find(provider, date, earliest, cancellationToken);
    }

    public OfficialRateSet? FindForRequestedDate(RateProvider provider, DateOnly date, CancellationToken cancellationToken = default)
    {
        ValidateProvider(provider);
        if (date == default) throw new ArgumentOutOfRangeException(nameof(date));
        return Find(provider, date, null, cancellationToken);
    }

    private OfficialRateSet? Find(RateProvider provider, DateOnly date, DateOnly? earliest, CancellationToken cancellationToken)
    {
        return Guard(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Exists()) return null;
            using var connection = Open(FilePath, SqliteOpenMode.ReadOnly);
            using var transaction = connection.BeginTransaction(deferred: true);
            ValidateDatabase(connection, transaction, cancellationToken);
            var filter = earliest is null ? "requested_date=$date AND effective_date<=$date" : "effective_date BETWEEN $earliest AND $date";
            var order = earliest is null ? "retrieved_utc DESC,rowid DESC" : "effective_date DESC,retrieved_utc DESC,id ASC";
            using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM RateSets WHERE provider=$provider AND {filter} ORDER BY {order} LIMIT 1;");
            command.Parameters.AddWithValue("$provider", (int)provider);
            if (earliest is { } from) command.Parameters.AddWithValue("$earliest", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var found = ReadValidatedRows(command, cancellationToken).SingleOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return found;
        }, cancellationToken);
    }

    public IReadOnlyList<RateHistoryEntry> ReadHistory(RateProvider provider, int offset = 0, int count = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(provider);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        return Guard<IReadOnlyList<RateHistoryEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Exists()) return Array.Empty<RateHistoryEntry>();
            using var connection = Open(FilePath, SqliteOpenMode.ReadOnly);
            using var transaction = connection.BeginTransaction(deferred: true);
            ValidateDatabase(connection, transaction, cancellationToken);
            using var command = Command(connection, transaction,
                $"SELECT {Columns} FROM RateSets WHERE provider=$provider ORDER BY effective_date DESC,retrieved_utc DESC,id ASC LIMIT $count OFFSET $offset;");
            command.Parameters.AddWithValue("$provider", (int)provider);
            command.Parameters.AddWithValue("$count", count);
            command.Parameters.AddWithValue("$offset", offset);
            var result = new List<RateHistoryEntry>();
            foreach (var rates in ReadValidatedRows(command, cancellationToken))
                result.Add(new(rates.Id, rates.Provider, rates.RequestedDate, rates.EffectiveDate, rates.RetrievedUtc, rates.PayloadSha256));
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result.AsReadOnly();
        }, cancellationToken);
    }

    public void Store(RateDownload download, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(download);
        Guard(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validated = ValidateDownload(download);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Exists() && TryCreate(validated, cancellationToken)) return true;
            using var connection = Open(FilePath, SqliteOpenMode.ReadWrite);
            using var transaction = connection.BeginTransaction();
            ValidateDatabase(connection, transaction, cancellationToken);
            Insert(connection, transaction, validated, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);
    }

    private bool Exists()
    {
        // File.Exists suppresses access errors; distinguish an absent cache from an unreadable one.
        try { return (File.GetAttributes(FilePath) & FileAttributes.Directory) == 0
                ? true : throw CacheError(L.GetCanonical("SqliteRateCache.TheCachePathRefersToADirectory")); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private bool TryCreate(RateDownload download, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".exchange-rates-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Publish only a complete committed database. Concurrent creators never see an empty schema.
            using (var connection = Open(temporary, SqliteOpenMode.ReadWriteCreate))
            {
                using var transaction = connection.BeginTransaction();
                ValidateJournalSettings(connection, transaction);
                Execute(connection, transaction, TableSql + ";" + IndexSql + ";");
                Execute(connection, transaction, $"PRAGMA application_id={ApplicationId}; PRAGMA user_version={SchemaVersion};");
                Insert(connection, transaction, download, token);
                token.ThrowIfCancellationRequested();
                transaction.Commit();
            }
            token.ThrowIfCancellationRequested();
            try { File.Move(temporary, FilePath, overwrite: false); return true; }
            catch (IOException) when (Exists()) { return false; }
        }
        finally
        {
            // Only this operation's unpublished temporary files are ours to remove.
            File.Delete(temporary);
            File.Delete(temporary + "-journal");
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 1
        }.ToString());
        try
        {
            connection.Open();
            connection.EnableExtensions(false);
            Execute(connection, null, "PRAGMA trusted_schema=OFF; PRAGMA busy_timeout=100; PRAGMA synchronous=FULL;");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void ValidateJournalSettings(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Changing journal mode inside a transaction is prohibited; reject non-cache journal configurations.
        if (!string.Equals(Convert.ToString(Scalar(connection, transaction, "PRAGMA journal_mode;"), CultureInfo.InvariantCulture), "delete", StringComparison.OrdinalIgnoreCase))
            throw CacheError(L.GetCanonical("SqliteRateCache.TheCacheMustUseTheSqliteDeleteLog"));
        if (Convert.ToInt64(Scalar(connection, transaction, "PRAGMA synchronous;"), CultureInfo.InvariantCulture) != 2)
            throw CacheError(L.GetCanonical("SqliteRateCache.TheTimingOfTheCacheMustBeFull"));
    }

    private static void ValidateDatabase(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Convert.ToInt64(Scalar(connection, transaction, "PRAGMA application_id;"), CultureInfo.InvariantCulture) != ApplicationId)
            throw CacheError(L.GetCanonical("SqliteRateCache.ThisFileIsNotAMonetaRateCache"));
        if (Convert.ToInt64(Scalar(connection, transaction, "PRAGMA user_version;"), CultureInfo.InvariantCulture) != SchemaVersion)
            throw new RateException(RateFailure.UnsupportedVersion, L.GetCanonical("SqliteRateCache.UnknownRateCacheVersion"));
        ValidateJournalSettings(connection, transaction);
        using (var command = Command(connection, transaction, "SELECT type,name,tbl_name,sql FROM sqlite_schema ORDER BY name;"))
        using (var reader = command.ExecuteReader())
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var name = reader.GetString(1);
                var valid = name switch
                {
                    "RateSets" => reader.GetString(0) == "table" && reader.GetString(2) == "RateSets" && !reader.IsDBNull(3) && CanonicalSql(reader.GetString(3)) == CanonicalSql(TableSql),
                    "RateSets_ProviderDate" => reader.GetString(0) == "index" && reader.GetString(2) == "RateSets" && !reader.IsDBNull(3) && CanonicalSql(reader.GetString(3)) == CanonicalSql(IndexSql),
                    "sqlite_autoindex_RateSets_1" => reader.GetString(0) == "index" && reader.GetString(2) == "RateSets" && reader.IsDBNull(3),
                    _ => false
                };
                if (!valid || !seen.Add(name)) throw CacheError(L.GetCanonical("SqliteRateCache.UnrecognizedCacheDiagram"));
            }
            if (seen.Count != 3) throw CacheError(L.GetCanonical("SqliteRateCache.IncompleteCacheDiagram"));
        }
        using var integrity = Command(connection, transaction, "PRAGMA integrity_check;");
        using var check = integrity.ExecuteReader();
        if (!check.Read() || check.GetString(0) != "ok" || check.Read()) throw CacheError(L.GetCanonical("SqliteRateCache.TheSqliteCacheIsCorrupt"));
        token.ThrowIfCancellationRequested();
    }

    private static IEnumerable<OfficialRateSet> ReadValidatedRows(SqliteCommand command, CancellationToken token)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var idText = reader.GetString(0);
            if (!Guid.TryParseExact(idText, "D", out var id) || id == Guid.Empty || id.ToString("D") != idText)
                throw CacheError(L.GetCanonical("SqliteRateCache.InvalidBatchId"));
            var providerNumber = reader.GetInt64(1);
            if (providerNumber is not (0 or 1)) throw CacheError(L.GetCanonical("SqliteRateCache.SourceOfInvalidBatch"));
            var requested = ReadDate(reader.GetString(2));
            var effective = ReadDate(reader.GetString(3));
            var utcText = reader.GetString(4);
            if (!DateTimeOffset.TryParseExact(utcText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc) ||
                utc.Offset != TimeSpan.Zero || utc.ToString("O", CultureInfo.InvariantCulture) != utcText)
                throw CacheError(L.GetCanonical("SqliteRateCache.InvalidLotTimeStamping"));
            var length = reader.GetBytes(7, 0, null, 0, 0);
            if (length is < 1 or > MaximumPayloadBytes) throw CacheError(L.GetCanonical("SqliteRateCache.ResponseSizeOfInvalidCache"));
            var payload = (byte[])reader.GetValue(7);
            var parsed = OfficialRateParsers.Parse((RateProvider)providerNumber, requested, utc, reader.GetString(5), payload).Rates;
            if (parsed.EffectiveDate != effective || !string.Equals(parsed.PayloadSha256, reader.GetString(6), StringComparison.Ordinal))
                throw CacheError(L.GetCanonical("SqliteRateCache.TheOriginalResponseDoesNotMatchTheMetadata"));
            yield return parsed with { Id = id };
        }
    }

    private static RateDownload ValidateDownload(RateDownload download)
    {
        RateRules.Validate(download.Rates);
        if (download.Payload.Length is < 1 or > MaximumPayloadBytes) throw CacheError(L.GetCanonical("SqliteRateCache.InvalidResponseSize"));
        var parsed = OfficialRateParsers.Parse(download.Rates.Provider, download.Rates.RequestedDate,
            download.Rates.RetrievedUtc, download.Rates.Endpoint, download.Payload);
        var original = download.Rates;
        var rates = parsed.Rates;
        if (rates.Provider != original.Provider || rates.ReferenceCurrency != original.ReferenceCurrency ||
            rates.RequestedDate != original.RequestedDate || rates.EffectiveDate != original.EffectiveDate ||
            rates.RetrievedUtc != original.RetrievedUtc || rates.Endpoint != original.Endpoint ||
            rates.PayloadSha256 != original.PayloadSha256 || !rates.Quotes.SequenceEqual(original.Quotes))
            throw CacheError(L.GetCanonical("SqliteRateCache.TheBatchDoesNotMatchItsOriginalResponse"));
        return parsed with { Rates = rates with { Id = original.Id } };
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, RateDownload download, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rates = download.Rates;
        using var command = Command(connection, transaction, $"INSERT INTO RateSets({Columns}) VALUES($id,$provider,$requested,$effective,$utc,$endpoint,$hash,$payload);");
        command.Parameters.AddWithValue("$id", rates.Id.ToString("D"));
        command.Parameters.AddWithValue("$provider", (int)rates.Provider);
        command.Parameters.AddWithValue("$requested", rates.RequestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$effective", rates.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$utc", rates.RetrievedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$endpoint", rates.Endpoint);
        command.Parameters.AddWithValue("$hash", rates.PayloadSha256);
        var bytes = MemoryMarshal.TryGetArray(download.Payload, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array : download.Payload.ToArray();
        command.Parameters.Add("$payload", SqliteType.Blob).Value = bytes;
        using var registration = token.Register(command.Cancel);
        command.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
    }

    private static DateOnly ReadDate(string text)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == default)
            throw CacheError(L.GetCanonical("SqliteRateCache.DateOfInvalidLot"));
        return date;
    }

    private static void ValidateProvider(RateProvider provider)
    {
        if (provider is not (RateProvider.Nbrb or RateProvider.Cbr)) throw new ArgumentOutOfRangeException(nameof(provider));
    }

    private static string CanonicalSql(string sql)
    {
        var builder = new System.Text.StringBuilder(sql.Length);
        var quoted = false;
        foreach (var character in sql.Trim().TrimEnd(';'))
        {
            if (character == '\'') quoted = !quoted;
            if (quoted || !char.IsWhiteSpace(character)) builder.Append(character);
        }
        return builder.ToString();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        return command.ExecuteScalar();
    }

    private static T Guard<T>(Func<T> action, CancellationToken token)
    {
        try { return action(); }
        catch (OperationCanceledException) { throw; }
        catch (RateException exception) when (exception.Failure is RateFailure.Cache or RateFailure.UnsupportedVersion) { throw; }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or
            RateException or FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            token.ThrowIfCancellationRequested();
            throw new RateException(RateFailure.Cache, L.GetCanonical("SqliteRateCache.TheRateCacheCannotBeReadOrRecorded"), exception);
        }
    }

    private static RateException CacheError(string message) => new(RateFailure.Cache, message);
}
