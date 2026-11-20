using System.Globalization;
using System.Windows;
using Moneta.Core;

namespace Moneta.App;

public partial class BudgetEditorWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly FinancialDocument _document;
    private readonly Guid _id;
    internal sealed record Choice(Guid Id, string Label);

    public BudgetPlan? Result { get; private set; }

    public BudgetEditorWindow(FinancialDocument document, DateOnly month, BudgetPlan? original = null, bool duplicate = false)
    {
        _document = document;
        _id = original is null || duplicate ? Guid.NewGuid() : original.Id;
        InitializeComponent();
        Heading.Text = duplicate ? "Dupliquer le budget" : original is null ? "Nouveau budget" : "Modifier le budget";
        NameInput.Text = original?.Name ?? "";
        MonthInput.Text = month.ToString("MM/yyyy", French);
        CurrencyInput.ItemsSource = document.Currencies;
        var currency = document.Currencies.FirstOrDefault(item => item.Code == (original?.CurrencyCode ?? "EUR")) ?? document.Currencies[0];
        CurrencyInput.SelectedItem = currency;
        AmountInput.Text = original is null ? "" : ExactMoney.FromMinorUnits(original.PlannedMinor, currency).ToString(French);
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
        new Choice(item.Id, $"{index + 1}. {item.Name} · {item.Currency.Code}" + (item.IsArchived ? " · archivé" : ""))).ToArray();

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
            var amount = MoneyInput.ParseFrench(AmountInput.Text, currency);
            var result = new BudgetPlan(_id, NameInput.Text.Trim(), ReadMonth(MonthInput.Text), currency.Code, ExactMoney.ToMinorUnits(amount, currency),
                AccountsInput.SelectedItems.Cast<Choice>().Select(item => item.Id).ToArray(), CategoriesInput.SelectedItems.Cast<Choice>().Select(item => item.Id).ToArray());
            result.Validate(_document);
            Result = result;
            DialogResult = true;
        }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
    }

    internal static DateOnly ReadMonth(string text) => DateOnly.TryParseExact("01/" + text.Trim(), "dd/MM/yyyy", French,
        DateTimeStyles.None, out var month) && month != default ? month : throw new ArgumentException("Saisissez le mois au format mm/aaaa.");
}
