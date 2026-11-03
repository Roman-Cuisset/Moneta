namespace Moneta.Core;

public sealed record Currency(string Code, string Name, int DecimalPlaces)
{
    public string Symbol { get; init; } = Code;
    public CurrencyDisplayRules DisplayRules { get; init; } = new();
}

public sealed record CurrencyDisplayRules(
    string CultureName = "fr-FR",
    bool UseSymbol = false,
    bool SymbolBeforeAmount = false);

public sealed record Account(Guid Id, string Name, Currency Currency, decimal OpeningBalance)
{
    // Native currency is fixed when the account is created, never by a display preference.
    public Currency Currency { get; } = Currency;
    public string? Type { get; init; }
    public string? Notes { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record Category(string Name)
{
    public string? Subcategory { get; init; }
}

public sealed record Payee(string Name);

public enum TransactionDirection
{
    Debit,
    Credit
}

public enum ReconciliationStatus
{
    Unmarked,
    Marked,
    Reconciled
}

public sealed record MoneyValuation(decimal Amount, Currency Currency, DateOnly RateDate, string Source);


public sealed record Transaction(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    string Label,
    Payee Payee,
    Category Category,
    decimal Amount,
    Currency Currency,
    TransactionDirection Direction,
    ReconciliationStatus Status,
    string Details)
{
    public MoneyValuation? Valuation { get; init; }
    public string? PaymentMode { get; init; }
    public Guid? PayeeId { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? SubcategoryId { get; init; }
    public Guid? PaymentMethodId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Revision { get; init; } = 1;
    public bool IsDeleted { get; init; }
    public DateOnly? MarkedOn { get; init; }
    public Guid? ReconciliationId { get; init; }
}

public sealed record DemoPortfolio(
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<Transaction> Transactions);
