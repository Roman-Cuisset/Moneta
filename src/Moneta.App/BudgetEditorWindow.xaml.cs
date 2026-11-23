using System.Globalization;
using System.Windows;
using Moneta.Core;

namespace Moneta.App;

public partial class BudgetEditorWindow : Window
{
    private static CultureInfo DisplayCulture => L.Culture;
    private readonly FinancialDocument _document;
    private readonly Guid _id;
    internal sealed record Choice(Guid Id, string Label);

    public BudgetPlan? Result { get; private set; }

    public BudgetEditorWindow(FinancialDocument document, DateOnly month, BudgetPlan? original = null, bool duplicate = false)
    {
        _document = document;
        _id = original is null || duplicate ? Guid.NewGuid() : original.Id;
        InitializeComponent();
        Heading.Text = duplicate ? L.Get("BudgetEditorWindow.DuplicateBudget") : original is null ? L.Get("BudgetEditorWindow.NewBudget") : L.Get("BudgetEditorWindow.ChangeBudget");
        NameInput.Text = original?.Name ?? "";
        MonthInput.Text = month.ToString("MM/yyyy", DisplayCulture);
        CurrencyInput.ItemsSource = document.Currencies;
        var currency = document.Currencies.FirstOrDefault(item => item.Code == (original?.CurrencyCode ?? "EUR")) ?? document.Currencies[0];
        CurrencyInput.SelectedItem = currency;
        AmountInput.Text = original is null ? "" : ExactMoney.FromMinorUnits(original.PlannedMinor, currency).ToString(DisplayCulture);
        AccountsInput.ItemsSource = AccountChoices(document);
        CategoriesInput.ItemsSource = CategoryChoices(document);
        if (original is not null)
        {
            foreach (var item in AccountsInput.Items.Cast<Choice>().Where(choice => original.Accounts.Contains(choice.Id))) AccountsInput.SelectedItems.Add(item);
            foreach (var item in CategoriesInput.Items.Cast<Choice>().Where(choice => original.Categories.Contains(choice.Id))) CategoriesInput.SelectedItems.Add(item);
        }
        Loaded += (_, _) => { if (duplicate) MonthInput.Focus(); else NameInput.Focus(); };
    }

    internal static Choice[] AccountChoices(FinancialDocument document) => document.Accounts.Select((item, index) =>
        new Choice(item.Id, $"{index + 1}. {item.Name} · {item.Currency.Code}" + (item.IsArchived ? L.Get("BudgetEditorWindow.Archived") : ""))).ToArray();

    internal static Choice[] CategoryChoices(FinancialDocument document)
    {
        var names = document.Categories.ToDictionary(item => item.Id, item => item.Name);
        return document.Categories.Select(item => new Choice(item.Id, item.ParentId is { } parent ? $"{names[parent]} › {item.Name}" : item.Name))
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Id).ToArray();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var currency = (Currency)CurrencyInput.SelectedItem;
            var amount = MoneyInput.ParseLocalized(AmountInput.Text, currency);
            var result = new BudgetPlan(_id, NameInput.Text.Trim(), ReadMonth(MonthInput.Text), currency.Code, ExactMoney.ToMinorUnits(amount, currency),
                AccountsInput.SelectedItems.Cast<Choice>().Select(item => item.Id).ToArray(), CategoriesInput.SelectedItems.Cast<Choice>().Select(item => item.Id).ToArray());
            result.Validate(_document);
            Result = result;
            DialogResult = true;
        }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
    }

    internal static DateOnly ReadMonth(string text) => DateOnly.TryParseExact("01/" + text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var month) && month != default ? month : throw new ArgumentException(L.Get("BudgetEditorWindow.EnterTheMonthInMmYyyyFormat"));
}
