namespace Moneta.Core;

public sealed record Currency(string Code, string Name, int DecimalPlaces);

public sealed record Account(Guid Id, string Name, Currency Currency, decimal OpeningBalance);

public sealed record Category(string Name);

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
}

public sealed record DemoPortfolio(
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<Transaction> Transactions);
