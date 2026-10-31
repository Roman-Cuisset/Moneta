namespace Moneta.Core;

// Copies only reference metadata. A failed draft never changes the live catalogue.
internal sealed class DraftCatalog(FinancialDocument document)
{
    private readonly List<LedgerCategory> _categories = document.Categories.ToList();
    private readonly List<LedgerPayee> _payees = document.Payees.ToList();
    private readonly List<PaymentMethod> _methods = document.PaymentMethods.ToList();

    public FinancialDocument Capture() => document with
    {
        Categories = _categories.ToArray(), Payees = _payees.ToArray(), PaymentMethods = _methods.ToArray()
    };

    public Transaction Build(Account account, TransactionDraft draft, Transaction? previous = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var payee = ResolvePayee(draft.PayeeName);
        var category = ResolveCategory(draft.CategoryName, null);
        if (!string.IsNullOrWhiteSpace(draft.SubcategoryName) && category is null)
            throw new ArgumentException("Choisissez une catégorie pour la sous-catégorie.", nameof(draft));
        var subcategory = ResolveCategory(draft.SubcategoryName, category?.Id);
        var method = ResolveMethod(draft.PaymentMode);
        var now = DateTimeOffset.UtcNow;
        if (previous is not null && now < previous.ModifiedUtc) now = previous.ModifiedUtc;
        return new Transaction(previous?.Id ?? Guid.NewGuid(), account.Id, draft.Date,
            draft.Label?.Trim() ?? string.Empty, new Payee(payee?.Name ?? string.Empty),
            new Category(category?.Name ?? string.Empty) { Subcategory = subcategory?.Name },
            draft.Amount, account.Currency, draft.Direction, previous?.Status ?? ReconciliationStatus.Unmarked,
            draft.Notes ?? string.Empty)
        {
            PayeeId = payee?.Id, CategoryId = category?.Id, SubcategoryId = subcategory?.Id,
            PaymentMethodId = method?.Id, PaymentMode = method?.Name,
            CreatedUtc = previous?.CreatedUtc ?? now, ModifiedUtc = now,
            Revision = previous?.Revision ?? 1
        };
    }

    private LedgerPayee? ResolvePayee(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = FinancialRules.NameKey(name);
        var result = _payees.FirstOrDefault(value => FinancialRules.NameKey(value.Name) == key);
        if (result is not null) return result;
        result = new LedgerPayee(Guid.NewGuid(), FinancialRules.CleanName(name));
        _payees.Add(result);
        return result;
    }

    private PaymentMethod? ResolveMethod(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = FinancialRules.NameKey(name);
        var result = _methods.FirstOrDefault(value => FinancialRules.NameKey(value.Name) == key);
        if (result is not null) return result;
        result = new PaymentMethod(Guid.NewGuid(), FinancialRules.CleanName(name));
        _methods.Add(result);
        return result;
    }

    private LedgerCategory? ResolveCategory(string? name, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = FinancialRules.NameKey(name);
        var result = _categories.FirstOrDefault(value => value.ParentId == parentId && FinancialRules.NameKey(value.Name) == key);
        if (result is not null) return result;
        result = new LedgerCategory(Guid.NewGuid(), FinancialRules.CleanName(name), parentId);
        _categories.Add(result);
        return result;
    }
}

internal static class LedgerProjection
{
    // Used only before a document has storage: this sequence contains pending inserts, never a persisted ledger.
    public static LedgerPage ReadPending(Account account, int offset, int pageSize,
        IReadOnlyCollection<TransactionChange> pending, Guid? anchorId)
    {
        var transactions = pending.Select(change => change.Value)
            .Where(value => value.AccountId == account.Id && !value.IsDeleted)
            .OrderBy(value => value.Date).ThenBy(value => value.CreatedUtc)
            .ThenBy(value => value.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        if (anchorId is { } anchor)
        {
            var index = Array.FindIndex(transactions, value => value.Id == anchor);
            if (index >= 0) offset = index / pageSize * pageSize;
        }
        offset = transactions.Length == 0 ? 0 : Math.Min(offset, (transactions.Length - 1) / pageSize * pageSize);
        var balance = account.OpeningBalance;
        var before = balance;
        var page = new List<Transaction>(pageSize);
        for (var index = 0; index < transactions.Length; index++)
        {
            if (index == offset) before = balance;
            var transaction = transactions[index];
            balance = checked(balance + FinancialRules.SignedAmount(transaction));
            if (index >= offset && index < offset + pageSize) page.Add(transaction);
        }
        return new LedgerPage(page.AsReadOnly(), before, balance, transactions.LongLength, offset);
    }
}
