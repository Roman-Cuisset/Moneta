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
            _ => throw new ArgumentException("Sens d'opération inconnu.", nameof(value))
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
                throw new ArgumentException("Identifiant de catégorie invalide ou dupliqué.", nameof(metadata));
            ValidateName(category.Name, 120, "catégorie");
            if (!categoryNames.Add((category.ParentId, NameKey(category.Name))))
                throw new ArgumentException("Une catégorie de même nom existe déjà sous ce parent.", nameof(metadata));
        }
        foreach (var category in metadata.Categories)
        {
            if (category.ParentId is { } parent &&
                (!categories.TryGetValue(parent, out var root) || root.ParentId is not null || parent == category.Id))
                throw new ArgumentException("Une sous-catégorie doit dépendre d'une catégorie racine.", nameof(metadata));
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
            throw new ArgumentException("Identifiant d'opération, de compte ou de référence invalide.", nameof(value));
        var account = metadata.Accounts.SingleOrDefault(account => account.Id == value.AccountId)
            ?? throw new ArgumentException("Le compte de l'opération est absent du dossier.", nameof(value));
        if (value.Currency != account.Currency || !metadata.Currencies.Contains(value.Currency))
            throw new ArgumentException("La devise native de l'opération ne correspond pas au compte.", nameof(value));
        if (value.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Le montant doit être positif ou nul.");
        _ = ExactMoney.ToMinorUnits(value.Amount, value.Currency);
        if (value.Date == default || !Enum.IsDefined(value.Direction) || !Enum.IsDefined(value.Status))
            throw new ArgumentException("Date, sens ou état de l'opération invalide.", nameof(value));
        if (value.CreatedUtc == default || value.ModifiedUtc == default ||
            value.CreatedUtc.Offset != TimeSpan.Zero || value.ModifiedUtc.Offset != TimeSpan.Zero ||
            value.ModifiedUtc < value.CreatedUtc || value.Revision < 1)
            throw new ArgumentException("Dates UTC ou révision de l'opération invalides.", nameof(value));
        ValidateName(value.Label, 240, "libellé");
        if (value.Details is null || value.Details.Length > 2000)
            throw new ArgumentException("Les notes sont limitées à 2 000 caractères.", nameof(value));
        if (value.Valuation is not null)
            throw new ArgumentException("La valorisation n'est pas prise en charge pour une opération réelle.", nameof(value));
        var payee = value.PayeeId is { } payeeId
            ? metadata.Payees.SingleOrDefault(item => item.Id == payeeId)?.Name
                ?? throw new ArgumentException("Tiers absent du catalogue.", nameof(value))
            : string.Empty;
        var category = value.CategoryId is { } categoryId
            ? metadata.Categories.SingleOrDefault(item => item.Id == categoryId && item.ParentId is null)
                ?? throw new ArgumentException("Catégorie racine absente du catalogue.", nameof(value))
            : null;
        var subcategory = value.SubcategoryId is { } subcategoryId
            ? metadata.Categories.SingleOrDefault(item => item.Id == subcategoryId && item.ParentId == value.CategoryId && value.CategoryId is not null)
                ?? throw new ArgumentException("Sous-catégorie incompatible avec la catégorie choisie.", nameof(value))
            : null;
        var method = value.PaymentMethodId is { } methodId
            ? metadata.PaymentMethods.SingleOrDefault(item => item.Id == methodId)?.Name
                ?? throw new ArgumentException("Mode de paiement absent du catalogue.", nameof(value))
            : null;
        if (value.PayeeId is not null) ValidateName(payee, 120, "tiers");
        if (category is not null) ValidateName(category.Name, 120, "catégorie");
        if (subcategory is not null) ValidateName(subcategory.Name, 120, "sous-catégorie");
        if (method is not null) ValidateName(method, 80, "mode de paiement");
        if (value.Payee?.Name != payee || value.Category?.Name != (category?.Name ?? string.Empty) ||
            (value.Category?.Subcategory ?? string.Empty) != (subcategory?.Name ?? string.Empty) ||
            (value.PaymentMode ?? string.Empty) != (method ?? string.Empty))
            throw new ArgumentException("Les noms de l'opération doivent provenir des références du catalogue.", nameof(value));
    }

    internal static string CleanName(string name) => name.Trim().Normalize(NormalizationForm.FormC);

    private static void ValidateName(string? name, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > maximum)
            throw new ArgumentException($"Le nom de {field} est obligatoire et limité à {maximum} caractères.");
    }

    private static void ValidateReferences(IEnumerable<(Guid Id, string Name)> references, int maximum, string field)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, name) in references)
        {
            ValidateName(name, maximum, field);
            if (id == Guid.Empty || !ids.Add(id) || !names.Add(NameKey(name)))
                throw new ArgumentException($"Identifiant ou nom de {field} dupliqué ou invalide.");
        }
    }
}
