using System.Globalization;

namespace Moneta.Core;

public static class TransferRules
{
    // Durable invariants only: archived accounts and historical dates remain readable.
    public static void Validate(TransferDetails details, FinancialDocument document)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(document);
        var value = details.Value;
        if (value.Id == Guid.Empty || value.DebitTransactionId == Guid.Empty || value.CreditTransactionId == Guid.Empty ||
            value.FeeTransactionId == Guid.Empty || value.DebitTransactionId == value.CreditTransactionId ||
            value.FeeTransactionId == value.DebitTransactionId || value.FeeTransactionId == value.CreditTransactionId ||
            value.DebitTransactionId != details.Debit.Id || value.CreditTransactionId != details.Credit.Id ||
            value.FeeTransactionId != details.Fee?.Id)
            throw new ArgumentException("Les liens du virement sont invalides.", nameof(details));
        if (value.Date == default || value.ExchangeDate == default(DateOnly) || value.Revision < 1 ||
            value.CreatedUtc == default || value.ModifiedUtc == default ||
            value.CreatedUtc.Offset != TimeSpan.Zero || value.ModifiedUtc.Offset != TimeSpan.Zero || value.ModifiedUtc < value.CreatedUtc)
            throw new ArgumentException("La date, la révision ou les dates UTC du virement sont invalides.", nameof(details));
        if (value.Reference is null || value.Reference.Length > 120 || value.Notes is null || value.Notes.Length > 2000)
            throw new ArgumentException("La référence est limitée à 120 caractères et les notes à 2 000 caractères.", nameof(details));
        if (value.Reference.Contains('\0') || value.Notes.Contains('\0') || value.RateSource?.Contains('\0') == true)
            throw new ArgumentException("Les textes du virement ne peuvent pas contenir NUL.", nameof(details));
        ValidateLeg(details.Debit, TransactionDirection.Debit, value, document);
        ValidateLeg(details.Credit, TransactionDirection.Credit, value, document);
        if (details.Debit.AccountId == details.Credit.AccountId || details.Debit.Amount <= 0 || details.Credit.Amount <= 0)
            throw new ArgumentException("Choisissez deux comptes différents et des montants principaux strictement positifs.", nameof(details));
        if (details.Fee is { } fee) ValidateLeg(fee, TransactionDirection.Debit, value, document);
        if (details.IsCrossCurrency)
        {
            if (value.ExchangeDate is null || string.IsNullOrWhiteSpace(value.RateSource))
                throw new ArgumentException("La date du change et sa source déclarée sont obligatoires.", nameof(details));
        }
        else if (details.Debit.Amount != details.Credit.Amount || value.ExchangeDate is not null || value.RateSource is not null)
            throw new ArgumentException("Un virement de même devise conserve exactement le montant, sans métadonnées de change.", nameof(details));
    }

    private static void ValidateLeg(Transaction leg, TransactionDirection direction, Transfer value, FinancialDocument document)
    {
        FinancialRules.ValidateTransaction(leg, document);
        if (leg.Direction != direction || leg.Date != value.Date || leg.Details != value.Notes || leg.IsDeleted != value.IsDeleted)
            throw new ArgumentException("Le sens, la date, les notes ou la suppression d'une jambe diffèrent du virement.");
    }

    public static string FormatEffectiveRate(TransferDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return FormatEffectiveRate(details.Debit.Amount, details.Debit.Currency, details.Credit.Amount, details.Credit.Currency);
    }

    public static string FormatEffectiveRate(decimal debited, Currency source, decimal credited, Currency destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (debited <= 0 || credited <= 0)
            throw new ArgumentException("Les montants principaux doivent être strictement positifs.");
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var ratio = $"Rapport exact : {credited.ToString("G29", culture)} {destination.Code} / {debited.ToString("G29", culture)} {source.Code} (hors frais)";
        try
        {
            var quotient = credited / debited;
            if (quotient != 0m)
                return $"Taux approximatif : 1 {source.Code} ≈ {quotient.ToString("G29", culture)} {destination.Code} · {ratio}";
        }
        catch (OverflowException) { }
        return ratio;
    }
}
