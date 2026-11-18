using System.Globalization;

namespace Moneta.Core;

public enum LedgerSearchSort { Date, Amount, Label, Account, Category, Payee, Status }
public enum LedgerSearchKind { All, Income, Expense, Transfers, Fees }
public enum LedgerTransferLeg { Debit, Credit, Fee }

[Flags]
public enum LedgerSearchStatus { Unmarked = 1, Marked = 2, Reconciled = 4, All = 7 }

public sealed record LedgerSearchCriteria
{
    public const int PageSize = 100;
    public Guid? AccountId { get; init; }
    public bool IncludeArchived { get; init; }
    public string? Text { get; init; }
    public string? Payee { get; init; }
    public string? Category { get; init; }
    public string? Subcategory { get; init; }
    public string? PaymentMethod { get; init; }
    public string? CurrencyCode { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? Through { get; init; }
    public decimal? Exact { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public LedgerSearchStatus Status { get; init; } = LedgerSearchStatus.All;
    public LedgerSearchKind Kind { get; init; }
    public LedgerSearchSort Sort { get; init; }
    public bool Descending { get; init; } = true;

    public bool HasFilters => From.HasValue || Through.HasValue || Exact.HasValue || Minimum.HasValue || Maximum.HasValue ||
        Status != LedgerSearchStatus.All || Kind != LedgerSearchKind.All ||
        new[] { Text, Payee, Category, Subcategory, PaymentMethod, CurrencyCode }.Any(value => !string.IsNullOrWhiteSpace(value));

    public void Validate()
    {
        if (From > Through) throw new ArgumentException("La date de début doit précéder ou égaler la date de fin.");
        if (Minimum > Maximum) throw new ArgumentException("Le minimum doit être inférieur ou égal au maximum.");
        if (Status == 0 || (Status & ~LedgerSearchStatus.All) != 0 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Sort))
            throw new ArgumentException("Le filtre ou le tri est inconnu.");
        foreach (var field in new[] { Text, Payee, Category, Subcategory, PaymentMethod, CurrencyCode })
            if (field?.Length > 240 || field?.Contains('\0') == true)
                throw new ArgumentException("Chaque recherche textuelle est limitée à 240 caractères valides.");
    }

    public static decimal? ParseAmount(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        var parts = text.TrimStart('+', '-').Split(',');
        var digits = string.Concat(parts).TrimStart('0').TrimEnd('0').Length;
        var scale = parts.Length == 2 ? parts[1].TrimEnd('0').Length : 0;
        // Decimal.TryParse can round very long inputs. Check their precision before parsing.
        if (digits > 28 || scale > 28 ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.GetCultureInfo("fr-FR"), out var amount))
            throw new ArgumentException("Saisissez un montant signé, avec une virgule et au plus 28 chiffres significatifs et décimales.");
        return amount;
    }
}

public sealed record LedgerSearchRow(
    Guid Id, Guid AccountId, string AccountName, long AccountOrdinal, bool Archived,
    DateOnly Date, string Label, string Notes, string Payee, string Category, string Subcategory,
    string PaymentMethod, string CurrencyCode, int DecimalPlaces, decimal SignedAmount,
    TransactionDirection Direction, ReconciliationStatus Status, Guid? TransferId, LedgerTransferLeg? TransferRole,
    string Reference);

public sealed record LedgerSearchPage(IReadOnlyList<LedgerSearchRow> Rows, long Total, int Index,
    TimeSpan CountElapsed, TimeSpan PageElapsed);
