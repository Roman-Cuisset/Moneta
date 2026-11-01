using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moneta.Core;

namespace Moneta.Infrastructure.Storage;

public sealed partial class MonetaFile
{
    // This is a storage format, not a serialization of the evolving domain record. Never change V1.
    private sealed record OperationSnapshotV1(
        int Version, string Id, string AccountId, string Date, string Label,
        long AmountMinor, string CurrencyCode, int CurrencyPrecision, string CurrencyName,
        string CurrencySymbol, string CurrencyCulture, bool CurrencyUseSymbol, bool CurrencySymbolBefore,
        int Direction, int Status, string Notes, string? PayeeId, string PayeeName,
        string? CategoryId, string CategoryName, string? SubcategoryId, string? SubcategoryName,
        string? PaymentMethodId, string? PaymentMethodName, bool IsDeleted,
        string CreatedUtc, string ModifiedUtc, long Revision, string? MarkedOn, string? ReconciliationId);
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8
    };

    private static string EncodeOperationSnapshot(Transaction value)
    {
        if (value.Valuation is not null) throw new ArgumentException("Une valorisation ne fait pas partie d’un instantané réel.");
        var dto = new OperationSnapshotV1(1, value.Id.ToString("D"), value.AccountId.ToString("D"), DateText(value.Date), value.Label,
            ExactMoney.ToMinorUnits(value.Amount, value.Currency), value.Currency.Code, value.Currency.DecimalPlaces,
            value.Currency.Name, value.Currency.Symbol, value.Currency.DisplayRules.CultureName,
            value.Currency.DisplayRules.UseSymbol, value.Currency.DisplayRules.SymbolBeforeAmount,
            (int)value.Direction, (int)value.Status, value.Details, value.PayeeId?.ToString("D"), value.Payee.Name,
            value.CategoryId?.ToString("D"), value.Category.Name, value.SubcategoryId?.ToString("D"), value.Category.Subcategory,
            value.PaymentMethodId?.ToString("D"), value.PaymentMode, value.IsDeleted, TimestampText(value.CreatedUtc),
            TimestampText(value.ModifiedUtc), value.Revision, value.MarkedOn is { } marked ? DateText(marked) : null,
            value.ReconciliationId?.ToString("D"));
        return JsonSerializer.Serialize(dto, SnapshotJsonOptions);
    }

    private static Transaction DecodeOperationSnapshot(string json, FinancialDocument metadata, LedgerLookup lookup)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<OperationSnapshotV1>(json, SnapshotJsonOptions)
                ?? throw new ArgumentException("Instantané absent.");
            if (dto.Version != 1 || JsonSerializer.Serialize(dto, SnapshotJsonOptions) != json)
                throw new ArgumentException("Instantané V1 non canonique ou incomplet.");
            Guid? OptionalId(string? text) => text is null ? null : ReadGuid(text);
            var currency = new Currency(dto.CurrencyCode, dto.CurrencyName, dto.CurrencyPrecision)
            {
                Symbol = dto.CurrencySymbol,
                DisplayRules = new(dto.CurrencyCulture, dto.CurrencyUseSymbol, dto.CurrencySymbolBefore)
            };
            var value = new Transaction(ReadGuid(dto.Id), ReadGuid(dto.AccountId), ReadDate(dto.Date), dto.Label,
                new(dto.PayeeName), new(dto.CategoryName) { Subcategory = dto.SubcategoryName },
                ExactMoney.FromMinorUnits(dto.AmountMinor, currency), currency,
                (TransactionDirection)dto.Direction, (ReconciliationStatus)dto.Status, dto.Notes)
            {
                PayeeId = OptionalId(dto.PayeeId), CategoryId = OptionalId(dto.CategoryId), SubcategoryId = OptionalId(dto.SubcategoryId),
                PaymentMethodId = OptionalId(dto.PaymentMethodId), PaymentMode = dto.PaymentMethodName, IsDeleted = dto.IsDeleted,
                CreatedUtc = ReadTimestamp(dto.CreatedUtc), ModifiedUtc = ReadTimestamp(dto.ModifiedUtc), Revision = dto.Revision,
                MarkedOn = dto.MarkedOn is null ? null : ReadDate(dto.MarkedOn), ReconciliationId = OptionalId(dto.ReconciliationId)
            };
            ValidateHistoricalOperation(value, metadata, lookup);
            if (ExactMoney.ToMinorUnits(value.Amount, currency) != dto.AmountMinor) throw new ArgumentException("Perte de précision dans l’instantané.");
            return value;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or OverflowException or NullReferenceException or KeyNotFoundException)
        {
            throw new DocumentException(DocumentError.Corrupt, "Instantané historique invalide.", exception);
        }
    }

    private static void ValidateHistoricalOperation(Transaction value, FinancialDocument metadata, LedgerLookup lookup)
    {
        var account = lookup.Accounts[value.AccountId];
        if (value.Currency.Code != account.Currency.Code || value.Currency.DecimalPlaces != account.Currency.DecimalPlaces)
            throw new ArgumentException("Devise historique incohérente.");
        ValidateText(value.Currency.Name, true); ValidateText(value.Currency.Symbol, true); ValidateText(value.Currency.DisplayRules.CultureName, false);
        void HistoricalName(Guid? id, string? name, int maximum)
        {
            if (id is null ? !string.IsNullOrEmpty(name) : string.IsNullOrWhiteSpace(name) || name.Length > maximum)
                throw new ArgumentException("Nom historique incohérent.");
        }
        HistoricalName(value.PayeeId, value.Payee.Name, 120); HistoricalName(value.CategoryId, value.Category.Name, 120);
        HistoricalName(value.SubcategoryId, value.Category.Subcategory, 120); HistoricalName(value.PaymentMethodId, value.PaymentMode, 80);
        // Names are historical evidence: only keys and native precision must match the current catalogue.
        FinancialRules.ValidateTransaction(value with
        {
            Currency = account.Currency,
            Payee = new(value.PayeeId is { } p ? lookup.Payees[p].Name : ""),
            Category = new(value.CategoryId is { } c ? lookup.Categories[c].Name : "")
                { Subcategory = value.SubcategoryId is { } s ? lookup.Categories[s].Name : null },
            PaymentMode = value.PaymentMethodId is { } m ? lookup.Methods[m].Name : null
        }, metadata);
    }

    private static ReconciliationBatch FreezeBatch(ReconciliationBatch? batch)
    {
        if (batch is null) return ReconciliationBatch.Empty;
        try
        {
            ArgumentNullException.ThrowIfNull(batch.Sessions); ArgumentNullException.ThrowIfNull(batch.Events);
            return new(Array.AsReadOnly(batch.Sessions.Select(change => change with
            {
                Entries = Array.AsReadOnly(change.Entries.ToArray())
            }).ToArray()), Array.AsReadOnly(batch.Events.ToArray()));
        }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        {
            throw new DocumentException(DocumentError.InvalidData, "Lot de rapprochement incomplet.", exception);
        }
    }

    private static DateOnly ReadDate(string text)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            date == default || DateText(date) != text) throw Error(DocumentError.Corrupt, "Date non canonique.");
        return date;
    }
    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string TimestampText(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
