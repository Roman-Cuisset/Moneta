using System.Text;

namespace Moneta.Core;

public static class FinancialRules
{
    public static string NameKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    public static decimal SignedAmount(Transaction value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Direction switch
        {
            TransactionDirection.Debit => -value.Amount,
            TransactionDirection.Credit => value.Amount,
            _ => throw new ArgumentException(L.Get("Common.UnknownSenseOfTransaction"), nameof(value))
        };
    }

    public static void ValidateCatalog(FinancialDocument metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var categories = new Dictionary<Guid, LedgerCategory>();
        var categoryNames = new HashSet<(Guid?, string)>();
        foreach (var category in metadata.Categories)
        {
            if (category.Id == Guid.Empty || !categories.TryAdd(category.Id, category))
                throw new ArgumentException(L.Get("FinancialRules.InvalidOrDuplicatedCategoryIdentifier"), nameof(metadata));
            ValidateName(category.Name, 120, "catégorie");
            if (!categoryNames.Add((category.ParentId, NameKey(category.Name))))
                throw new ArgumentException(L.Get("FinancialRules.ACategoryOfTheSameNameAlreadyExists"), nameof(metadata));
        }
        foreach (var category in metadata.Categories)
        {
            if (category.ParentId is { } parent &&
                (!categories.TryGetValue(parent, out var root) || root.ParentId is not null || parent == category.Id))
                throw new ArgumentException(L.Get("FinancialRules.ASubcategoryMustDependOnARootCategory"), nameof(metadata));
        }
        ValidateReferences(metadata.Payees.Select(value => (value.Id, value.Name)), 120, "tiers");
        ValidateReferences(metadata.PaymentMethods.Select(value => (value.Id, value.Name)), 80, "mode de paiement");
    }

    public static void ValidateTransaction(Transaction value, FinancialDocument metadata)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metadata);
        if (value.Id == Guid.Empty || value.AccountId == Guid.Empty || value.PayeeId == Guid.Empty ||
            value.CategoryId == Guid.Empty || value.SubcategoryId == Guid.Empty || value.PaymentMethodId == Guid.Empty)
            throw new ArgumentException(L.Get("FinancialRules.InvalidTransactionAccountOrReferenceIdentifier"), nameof(value));
        var account = metadata.Accounts.SingleOrDefault(account => account.Id == value.AccountId)
            ?? throw new ArgumentException(L.Get("FinancialRules.TheAccountOfTheTransactionIsMissingFrom"), nameof(value));
        if (value.Currency != account.Currency || !metadata.Currencies.Contains(value.Currency))
            throw new ArgumentException(L.Get("FinancialRules.TheNativeCurrencyOfTheTransactionDoesNot"), nameof(value));
        if (value.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(value), L.Get("FinancialRules.TheAmountMustBePositiveOrZero"));
        _ = ExactMoney.ToMinorUnits(value.Amount, value.Currency);
        if (value.Date == default || !Enum.IsDefined(value.Direction) || !Enum.IsDefined(value.Status))
            throw new ArgumentException(L.Get("FinancialRules.DateMeaningOrConditionOfTransactionInvalid"), nameof(value));
        if ((value.Status == ReconciliationStatus.Unmarked && value.MarkedOn is not null) ||
            value.MarkedOn == default(DateOnly) || value.ReconciliationId == Guid.Empty ||
            (value.ReconciliationId is not null && (value.Status != ReconciliationStatus.Reconciled || value.IsDeleted)))
            throw new ArgumentException(L.Get("FinancialRules.IncoherentDateOrReconciliationReference"), nameof(value));
        if (value.CreatedUtc == default || value.ModifiedUtc == default ||
            value.CreatedUtc.Offset != TimeSpan.Zero || value.ModifiedUtc.Offset != TimeSpan.Zero ||
            value.ModifiedUtc < value.CreatedUtc || value.Revision < 1)
            throw new ArgumentException(L.Get("FinancialRules.InvalidUtcDatesOrRevisionOfTransaction"), nameof(value));
        ValidateName(value.Label, 240, "libellé");
        if (value.Details is null || value.Details.Length > 2000)
            throw new ArgumentException(L.Get("FinancialRules.NotesAreLimitedTo2000Characters"), nameof(value));
        if (value.Valuation is not null)
            throw new ArgumentException(L.Get("FinancialRules.TheValuationIsNotSupportedForAReal"), nameof(value));
        var payee = value.PayeeId is { } payeeId
            ? metadata.Payees.SingleOrDefault(item => item.Id == payeeId)?.Name
                ?? throw new ArgumentException(L.Get("FinancialRules.PayeePartyMissingFromTheCatalogue"), nameof(value))
            : string.Empty;
        var category = value.CategoryId is { } categoryId
            ? metadata.Categories.SingleOrDefault(item => item.Id == categoryId && item.ParentId is null)
                ?? throw new ArgumentException(L.Get("FinancialRules.RootCategoryAbsentFromTheCatalogue"), nameof(value))
            : null;
        var subcategory = value.SubcategoryId is { } subcategoryId
            ? metadata.Categories.SingleOrDefault(item => item.Id == subcategoryId && item.ParentId == value.CategoryId && value.CategoryId is not null)
                ?? throw new ArgumentException(L.Get("FinancialRules.SubcategoryIncompatibleWithTheChosenCategory"), nameof(value))
            : null;
        var method = value.PaymentMethodId is { } methodId
            ? metadata.PaymentMethods.SingleOrDefault(item => item.Id == methodId)?.Name
                ?? throw new ArgumentException(L.Get("FinancialRules.PaymentMethodMissingFromTheCatalogue"), nameof(value))
            : null;
        if (value.PayeeId is not null) ValidateName(payee, 120, "tiers");
        if (category is not null) ValidateName(category.Name, 120, "catégorie");
        if (subcategory is not null) ValidateName(subcategory.Name, 120, "sous-catégorie");
        if (method is not null) ValidateName(method, 80, "mode de paiement");
        if (value.Payee?.Name != payee || value.Category?.Name != (category?.Name ?? string.Empty) ||
            (value.Category?.Subcategory ?? string.Empty) != (subcategory?.Name ?? string.Empty) ||
            (value.PaymentMode ?? string.Empty) != (method ?? string.Empty))
            throw new ArgumentException(L.Get("FinancialRules.TheNamesOfTheTransactionMustComeFrom"), nameof(value));
    }

    internal static string CleanName(string name) => name.Trim().Normalize(NormalizationForm.FormC);

    private static void ValidateName(string? name, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > maximum)
            throw new ArgumentException(L.Format("FinancialRules.TheNameOf0IsMandatoryAndLimited", (field), (maximum)));
    }

    private static void ValidateReferences(IEnumerable<(Guid Id, string Name)> references, int maximum, string field)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, name) in references)
        {
            ValidateName(name, maximum, field);
            if (id == Guid.Empty || !ids.Add(id) || !names.Add(NameKey(name)))
                throw new ArgumentException(L.Format("FinancialRules.NameOrIdentifierOf0DuplicatedOrInvalid", (field)));
        }
    }
}
