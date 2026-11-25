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
        EntryAmount.Text = MoneyInput.FormatForEdit(editing?.Amount ?? 0m, _currency.DecimalPlaces);
        EntryDirection.SelectedIndex = editing?.Direction switch
        {
            null or TransactionDirection.Debit => 0,
            TransactionDirection.Credit => 1,
            _ => -1
        };
        EntryLabel.Text = editing?.Label ?? string.Empty;
        EntryNotes.Text = editing?.Details ?? string.Empty;
        EntryPaymentMode.ItemsSource = TransactionEntry.Names(document.PaymentMethods.Select(item => item.Name));
        EntryPayee.ItemsSource = TransactionEntry.Names(document.Payees.Select(item => item.Name));
        EntryCategory.ItemsSource = TransactionEntry.Names(_categories.Where(item => item.ParentId is null).Select(item => item.Name));
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
        var typedName = EntrySubcategory.Text;
        EntrySubcategory.ItemsSource = TransactionEntry.Subcategories(_categories, categoryName);
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
            var draft = new TransactionEntry(EntryDate.Text, EntryDirection.SelectedIndex, EntryAmount.Text,
                EntryLabel.Text, EntryPaymentMode.Text, EntryPayee.Text, EntryCategory.Text, EntrySubcategory.Text,
                EntryNotes.Text).CreateDraft(_currency, DisplayCulture);
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
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or DocumentException or OverflowException)
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

}
