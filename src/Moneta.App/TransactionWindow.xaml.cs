using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class TransactionWindow : Window
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _model;
    private readonly Guid _accountId;
    private readonly Currency _currency;
    private readonly Guid? _editingId;
    private readonly long _editingRevision;
    private readonly IReadOnlyList<LedgerCategory> _categories;
    private bool _submitting;

    public TransactionWindow(MainViewModel model, Transaction? editing = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.IsDemo)
            throw new InvalidOperationException("La saisie d’opérations n’est pas disponible dans la démonstration.");
        var account = model.SelectedAccount?.Account
            ?? throw new InvalidOperationException("Sélectionnez un compte pour saisir une opération.");
        if (account.IsArchived)
            throw new InvalidOperationException("Un compte archivé ne peut pas recevoir de modifications.");
        if (editing is not null && (editing.AccountId != account.Id || editing.IsDeleted))
            throw new ArgumentException("L’opération à modifier doit appartenir au compte sélectionné et ne pas être supprimée.", nameof(editing));
        if (editing?.Valuation is not null)
            throw new ArgumentException("La modification d’une opération avec valorisation n’est pas prise en charge.", nameof(editing));

        _model = model;
        _accountId = account.Id;
        _currency = account.Currency;
        _editingId = editing?.Id;
        _editingRevision = editing?.Revision ?? 0;
        var document = model.CaptureDocument();
        _categories = document.Categories;

        InitializeComponent();
        Title = editing is null ? "Nouvelle opération" : "Modifier l’opération";
        Heading.Text = Title;
        TransactionAccount.Text = account.Name;
        NativeCurrency.Text = _currency.Code;
        AmountHint.Text = $"Montant en {_currency.Code}, sans signe : le sens indique la dépense ou la recette. "
            + $"Précision : {_currency.DecimalPlaces} décimale(s), sans arrondi. Zéro est autorisé.";
        EntryDate.Text = (editing?.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd/MM/yyyy", FrenchCulture);
        EntryAmount.Text = (editing?.Amount ?? 0m).ToString($"F{_currency.DecimalPlaces}", FrenchCulture);
        EntryDirection.SelectedIndex = editing?.Direction switch
        {
            null or TransactionDirection.Debit => 0,
            TransactionDirection.Credit => 1,
            _ => -1
        };
        EntryLabel.Text = editing?.Label ?? string.Empty;
        EntryNotes.Text = editing?.Details ?? string.Empty;
        EntryPaymentMode.ItemsSource = document.PaymentMethods.Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        EntryPayee.ItemsSource = document.Payees.Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        EntryCategory.ItemsSource = _categories.Where(item => item.ParentId is null).Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        EntryPaymentMode.Text = editing?.PaymentMode ?? string.Empty;
        EntryPayee.Text = editing?.Payee.Name ?? string.Empty;
        EntryCategory.Text = editing?.Category.Name ?? string.Empty;
        EntrySubcategory.Text = editing?.Category.Subcategory ?? string.Empty;
        RefreshSubcategories(EntryCategory.Text);
        EntryCategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Category_TextChanged));
        Loaded += (_, _) =>
        {
            EntryDate.Focus();
            EntryDate.SelectAll();
        };
    }

    private void Category_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSubcategories(e.OriginalSource is TextBox textBox ? textBox.Text : EntryCategory.Text);
    }

    private void RefreshSubcategories(string categoryName)
    {
        string? key = null;
        try
        {
            key = FinancialRules.NameKey(categoryName);
        }
        catch (ArgumentException)
        {
            // Incomplete text (including a partial Unicode pair) is validated on submission.
        }
        var root = key is null ? null
            : _categories.FirstOrDefault(category => category.ParentId is null && FinancialRules.NameKey(category.Name) == key);
        // Keep typed input when the root changes; never silently discard a subcategory.
        var typedName = EntrySubcategory.Text;
        EntrySubcategory.ItemsSource = root is null
            ? Array.Empty<string>()
            : _categories.Where(category => category.ParentId == root.Id).Select(category => category.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        EntrySubcategory.Text = typedName;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting)
            return;
        _submitting = true;
        ConfirmTransaction.IsEnabled = false;
        ErrorText.Text = string.Empty;
        var staged = false;
        try
        {
            if (!TryBuildDraft(out var draft))
                return;
            if (_model.SelectedAccountId != _accountId)
                throw new InvalidOperationException("Le compte sélectionné a changé. Fermez cette fenêtre puis recommencez sur le compte voulu.");
            if (_editingId is { } id)
                _model.EditTransaction(id, _editingRevision, draft);
            else
                _model.AddTransaction(draft);
            staged = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = "Le brouillon n’a pas pu être validé. Vos champs sont conservés.\n" + error.Message;
            ErrorText.BringIntoView();
        }
        finally
        {
            if (!staged)
            {
                _submitting = false;
                ConfirmTransaction.IsEnabled = true;
            }
        }
    }

    private bool TryBuildDraft(out TransactionDraft draft)
    {
        draft = null!;
        if (!DateOnly.TryParseExact(EntryDate.Text.Trim(), "dd/MM/yyyy", FrenchCulture, DateTimeStyles.None, out var date)
            || date == default)
            return ShowError("Date invalide : saisissez une date au format jj/mm/aaaa, par exemple 31/10/2026.", EntryDate);
        if (EntryDirection.SelectedIndex is not (0 or 1))
            return ShowError("Choisissez Dépense ou Recette pour indiquer le sens de l’opération.", EntryDirection);

        decimal amount;
        try
        {
            amount = ParseMagnitude(EntryAmount.Text, _currency);
        }
        catch (ArgumentException error)
        {
            return ShowError(error.Message, EntryAmount);
        }

        var label = EntryLabel.Text.Trim();
        if (string.IsNullOrWhiteSpace(label) || label.Length > 240)
            return ShowError("Le libellé est obligatoire et doit contenir au plus 240 caractères.", EntryLabel);
        var mode = OptionalName(EntryPaymentMode.Text);
        var payee = OptionalName(EntryPayee.Text);
        var category = OptionalName(EntryCategory.Text);
        var subcategory = OptionalName(EntrySubcategory.Text);
        if (mode?.Length > 80)
            return ShowError("Le mode de paiement doit contenir au plus 80 caractères.", EntryPaymentMode);
        if (payee?.Length > 120)
            return ShowError("Le tiers doit contenir au plus 120 caractères.", EntryPayee);
        if (category?.Length > 120)
            return ShowError("La catégorie doit contenir au plus 120 caractères.", EntryCategory);
        if (subcategory?.Length > 120)
            return ShowError("La sous-catégorie doit contenir au plus 120 caractères.", EntrySubcategory);
        if (subcategory is not null && category is null)
            return ShowError("Saisissez une catégorie racine pour cette sous-catégorie, ou effacez la sous-catégorie.", EntryCategory);
        if (EntryNotes.Text.Length > 2000)
            return ShowError("Les notes doivent contenir au plus 2 000 caractères.", EntryNotes);

        draft = new TransactionDraft(date, amount,
            EntryDirection.SelectedIndex == 0 ? TransactionDirection.Debit : TransactionDirection.Credit,
            label, payee, category, subcategory, mode, EntryNotes.Text);
        return true;
    }

    private bool ShowError(string message, Control field)
    {
        ErrorText.Text = message;
        field.BringIntoView();
        field.Focus();
        return false;
    }

    private static string? OptionalName(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static decimal ParseMagnitude(string text, Currency currency)
    {
        // Parse minor units directly: decimal.TryParse can silently round long fractional input.
        var input = text.AsSpan().Trim();
        const string invalidFormat = "Montant invalide : saisissez un nombre sans signe, avec une virgule décimale (exemple : 1 234,56).";
        if (input.IsEmpty)
            throw new ArgumentException(invalidFormat);
        var comma = input.IndexOf(',');
        var whole = comma < 0 ? input : input[..comma];
        var fraction = comma < 0 ? ReadOnlySpan<char>.Empty : input[(comma + 1)..];
        if (whole.IsEmpty || (comma >= 0 && fraction.IsEmpty))
            throw new ArgumentException(invalidFormat);

        long minor = 0;
        var digitsInGroup = 0;
        var grouped = false;
        try
        {
            foreach (var character in whole)
            {
                if (character is ' ' or '\u00a0' or '\u202f')
                {
                    if (digitsInGroup == 0 || (!grouped && digitsInGroup > 3) || (grouped && digitsInGroup != 3))
                        throw new ArgumentException(invalidFormat);
                    grouped = true;
                    digitsInGroup = 0;
                    continue;
                }
                if (character is < '0' or > '9')
                    throw new ArgumentException(invalidFormat);
                digitsInGroup++;
                minor = checked(minor * 10 + (character - '0'));
            }
            if (digitsInGroup == 0 || (grouped && digitsInGroup != 3))
                throw new ArgumentException(invalidFormat);
            for (var index = 0; index < fraction.Length; index++)
            {
                var character = fraction[index];
                if (character is < '0' or > '9')
                    throw new ArgumentException(invalidFormat);
                if (index < currency.DecimalPlaces)
                    minor = checked(minor * 10 + (character - '0'));
                else if (character != '0')
                    throw new ArgumentException($"Ce montant dépasse la précision de {currency.DecimalPlaces} décimale(s) de {currency.Code}. Aucun arrondi n’est effectué.");
            }
            for (var index = fraction.Length; index < currency.DecimalPlaces; index++)
                minor = checked(minor * 10);
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"Montant hors limites pour {currency.Code} : le nombre d’unités mineures ne doit pas dépasser {long.MaxValue}.");
        }
        return ExactMoney.FromMinorUnits(minor, currency);
    }
}
