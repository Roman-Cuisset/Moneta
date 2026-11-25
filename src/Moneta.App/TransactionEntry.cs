using System.Globalization;
using Moneta.Core;

namespace Moneta.App;

internal sealed record TransactionEntry(string Date, int Direction, string Amount, string Label,
    string PaymentMode, string Payee, string Category, string Subcategory, string Notes = "")
{
    internal static string DatePattern(CultureInfo culture) => culture.Name == L.English ? "MM/dd/yyyy" : "dd/MM/yyyy";

    internal TransactionDraft CreateDraft(Currency currency, CultureInfo culture)
    {
        if (!DateOnly.TryParseExact(Date.Trim(), DatePattern(culture), culture, DateTimeStyles.None, out var date) || date == default)
            throw new ArgumentException(L.Get("TransactionWindow.InvalidDateEnterADateInMmDd"));
        if (Direction is not (0 or 1))
            throw new ArgumentException(L.Get("TransactionWindow.ChooseExpenditureOrRecipeToIndicateTheMeaning"));
        if (string.IsNullOrWhiteSpace(Label))
            throw new ArgumentException(L.Get("TransactionWindow.TheWordingIsMandatoryAndMustContainNo"));

        return new TransactionDraft(date, MoneyInput.ParseLocalized(Amount, currency),
            Direction == 0 ? TransactionDirection.Debit : TransactionDirection.Credit,
            Label, Payee, Category, Subcategory, PaymentMode, Notes);
    }

    internal static string[] Subcategories(IReadOnlyList<LedgerCategory> categories, string categoryName)
    {
        Guid? parent;
        try
        {
            var key = FinancialRules.NameKey(categoryName);
            parent = categories.FirstOrDefault(category => category.ParentId is null && FinancialRules.NameKey(category.Name) == key)?.Id;
        }
        catch (ArgumentException)
        {
            // A partial name remains editable and is validated only when staged.
            return [];
        }

        return parent is null ? [] : Names(categories.Where(category => category.ParentId == parent).Select(category => category.Name));
    }

    internal static string[] Names(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
}
