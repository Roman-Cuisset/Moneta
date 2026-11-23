using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class TransactionWindow : Window
{
    private static CultureInfo DisplayCulture => L.Culture;
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
            throw new InvalidOperationException(L.Get("TransactionWindow.SeizureOfTransactionsIsNotAvailableInThe"));
        var account = model.SelectedAccount?.Account
            ?? throw new InvalidOperationException(L.Get("TransactionWindow.SelectAnAccountToEnterATransaction"));
        if (account.IsArchived)
            throw new InvalidOperationException(L.Get("TransactionWindow.AnArchivedAccountCannotReceiveChanges"));
        if (editing is not null && (editing.AccountId != account.Id || editing.IsDeleted))
            throw new ArgumentException(L.Get("TransactionWindow.TheTransactionToBeModifiedMustBelongTo"), nameof(editing));
        if (editing?.Valuation is not null)
            throw new ArgumentException(L.Get("TransactionWindow.TheModificationOfAValuationTransactionIsNot"), nameof(editing));

        _model = model;
        _accountId = account.Id;
        _currency = account.Currency;
        _editingId = editing?.Id;
        _editingRevision = editing?.Revision ?? 0;
        var document = model.CaptureDocument();
        _categories = document.Categories;

        InitializeComponent();
        Title = editing is null ? L.Get("Common.NewTransaction") : L.Get("TransactionWindow.ChangeTheTransaction");
        Heading.Text = Title;
        TransactionAccount.Text = account.Name;
        NativeCurrency.Text = _currency.Code;
        AmountHint.Text = L.Format("TransactionWindow.AmountIn0WithoutSignTheMeaningIndicates", (_currency.Code))
            + L.Format("TransactionWindow.Accuracy0DecimalsWithoutRoundingZeroIsAllowed", (_currency.DecimalPlaces));
        EntryDate.Text = (editing?.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToString(L.DatePattern, DisplayCulture);
        EntryAmount.Text = (editing?.Amount ?? 0m).ToString($"F{_currency.DecimalPlaces}", DisplayCulture);
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
                throw new InvalidOperationException(L.Get("ReconciliationWindow.TheSelectedAccountHasChangedCloseThisWindow"));
            if (_editingId is { } id)
            {
                var previous = _model.Rows.FirstOrDefault(row => row.Transaction.Id == id)?.Transaction;
                if (previous is { Status: ReconciliationStatus.Marked } &&
                    (previous.Date != draft.Date || previous.Amount != draft.Amount || previous.Direction != draft.Direction) &&
                    MessageBox.Show(this,
                        L.Get("TransactionWindow.ThisTransactionIsMarkedItsDateAmountOr"),
                        L.Get("TransactionWindow.MonetaCorrectAClearedTransaction"), MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                _model.EditTransaction(id, _editingRevision, draft);
            }
            else
                _model.AddTransaction(draft);
            staged = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = L.Get("Common.TheDraftCouldNotBeValidatedYourFields") + error.Message;
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
        if (!DateOnly.TryParseExact(EntryDate.Text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date)
            || date == default)
            return ShowError(L.Get("TransactionWindow.InvalidDateEnterADateInMmDd"), EntryDate);
        if (EntryDirection.SelectedIndex is not (0 or 1))
            return ShowError(L.Get("TransactionWindow.ChooseExpenditureOrRecipeToIndicateTheMeaning"), EntryDirection);

        decimal amount;
        try
        {
            amount = MoneyInput.ParseLocalized(EntryAmount.Text, _currency);
        }
        catch (ArgumentException error)
        {
            return ShowError(error.Message, EntryAmount);
        }

        var label = EntryLabel.Text.Trim();
        if (string.IsNullOrWhiteSpace(label) || label.Length > 240)
            return ShowError(L.Get("TransactionWindow.TheWordingIsMandatoryAndMustContainNo"), EntryLabel);
        var mode = OptionalName(EntryPaymentMode.Text);
        var payee = OptionalName(EntryPayee.Text);
        var category = OptionalName(EntryCategory.Text);
        var subcategory = OptionalName(EntrySubcategory.Text);
        if (mode?.Length > 80)
            return ShowError(L.Get("TransactionWindow.TheMethodOfPaymentMustContainNoMore"), EntryPaymentMode);
        if (payee?.Length > 120)
            return ShowError(L.Get("TransactionWindow.ThePayeeMustContainNoMoreThan120"), EntryPayee);
        if (category?.Length > 120)
            return ShowError(L.Get("TransactionWindow.TheCategoryMustContainNoMoreThan120"), EntryCategory);
        if (subcategory?.Length > 120)
            return ShowError(L.Get("TransactionWindow.TheSubcategoryMustContainNoMoreThan120"), EntrySubcategory);
        if (subcategory is not null && category is null)
            return ShowError(L.Get("TransactionWindow.EnterARootCategoryForThisSubcategoryOr"), EntryCategory);
        if (EntryNotes.Text.Length > 2000)
            return ShowError(L.Get("TransactionWindow.NotesMustContainAMaximumOf2000"), EntryNotes);

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

}
