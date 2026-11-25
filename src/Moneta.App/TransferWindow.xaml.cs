using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class TransferWindow : Window
{
    private const string DefaultRateSource = "Montants saisis par l’utilisateur";
    private static CultureInfo DisplayCulture => L.Culture;
    private readonly MainViewModel _model;
    private readonly TransferDetails? _editing;
    private readonly bool _readOnly;
    private bool _ready;
    private bool _updating;
    private bool _submitting;
    private bool _sameCurrency;
    private bool _exchangeDateFollowsTransfer;
    private string _independentCredit = string.Empty;

    public TransferWindow(MainViewModel model, TransferDetails? editing = null, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.IsDemo)
            throw new InvalidOperationException(L.Get("TransferWindow.TransfersAreNotAvailableInTheDemonstration"));
        var archived = editing is not null && model.HasArchivedTransferAccount(editing);
        var consultation = editing is not null && (readOnly || archived || editing.HasReconciledLeg || !model.PendingReconciliations.IsEmpty);
        if (!consultation && !model.PendingReconciliations.IsEmpty)
            throw new InvalidOperationException(L.Get("TransferWindow.RecordTheReconciliationOrReopeningCtrlSBefore"));
        if (editing?.Value.IsDeleted == true)
            throw new ArgumentException(L.Get("TransferWindow.ACancelledTransferCannotBeChanged"), nameof(editing));
        if (readOnly && editing is null)
            throw new ArgumentException(L.Get("TransferWindow.ConsultationRequiresAnExistingTransfer"), nameof(editing));

        _model = model;
        _editing = editing;
        _readOnly = consultation;
        var accounts = model.Accounts.Where(item => consultation || !item.Account.IsArchived).ToArray();
        if (accounts.Length < 2)
            throw new InvalidOperationException(L.Get("TransferWindow.CreateAtLeastTwoActiveAccountsInThe"));

        InitializeComponent();
        Title = consultation ? L.Get("TransferWindow.SeeTheTransfer") : editing is null ? L.Get("TransferWindow.NewTransfer") : L.Get("TransferWindow.ChangeTheTransfer");
        Heading.Text = Title;
        SourceAccount.ItemsSource = accounts;
        DestinationAccount.ItemsSource = accounts;
        FeeAccount.ItemsSource = accounts;
        SourceAccount.SelectedItem = accounts.FirstOrDefault(item => item.Account.Id == (editing?.Debit.AccountId ?? model.SelectedAccountId)) ?? accounts[0];
        DestinationAccount.SelectedItem = editing is null
            ? accounts.First(item => item != SourceAccount.SelectedItem)
            : accounts.Single(item => item.Account.Id == editing.Credit.AccountId);
        FeeAccount.SelectedItem = editing?.Fee is { } fee
            ? accounts.Single(item => item.Account.Id == fee.AccountId) : null;
        SourceAccount.IsEnabled = DestinationAccount.IsEnabled = editing is null;
        EntryDate.Text = FormatDate(editing?.Value.Date ?? DateOnly.FromDateTime(DateTime.Today));
        ExchangeDate.Text = FormatDate(editing?.Value.ExchangeDate ?? editing?.Value.Date ?? DateOnly.FromDateTime(DateTime.Today));
        _exchangeDateFollowsTransfer = editing is null;
        RateSource.Text = editing?.Value.RateSource ?? DefaultRateSource;
        DebitAmount.Text = editing is null ? string.Empty : FormatAmount(editing.Debit);
        CreditAmount.Text = editing is null ? string.Empty : FormatAmount(editing.Credit);
        _independentCredit = CreditAmount.Text;
        HasFee.IsChecked = editing?.Fee is not null;
        HasFee.IsEnabled = editing?.Fee is null;
        FeeAmount.Text = editing?.Fee is { } existingFee ? FormatAmount(existingFee) : string.Empty;
        EntryReference.Text = editing?.Value.Reference ?? string.Empty;
        EntryNotes.Text = editing?.Value.Notes ?? string.Empty;
        AccountNotice.Text = editing is null
            ? L.Get("TransferWindow.TwoSeparateTransactionsDebitAndCreditTheirNp")
            : L.Get("TransferWindow.SourceAccountsRecipientAndExistingFixedFeesTo");
        _ready = true;
        RefreshAccounts();
        RefreshFee();
        if (consultation)
        {
            DraftNotice.Text = L.Get("TransferWindow.ConsultationNoMovementIsChangedFromThisWindow");
            SourceAccount.IsEnabled = DestinationAccount.IsEnabled = FeeAccount.IsEnabled = HasFee.IsEnabled = false;
            foreach (var field in new[] { EntryDate, DebitAmount, CreditAmount, ExchangeDate, RateSource, FeeAmount, EntryReference, EntryNotes })
                field.IsReadOnly = true;
            ConfirmTransfer.IsEnabled = false;
            CancelTransfer.Content = "Fermer";
            ErrorText.Text = archived ? L.Get("TransferWindow.ReactivateTheTransferAccountsIncludingFeesToFind")
                : editing!.HasReconciledLeg ? ReconciledMessage
                : !model.PendingReconciliations.IsEmpty
                    ? L.Get("TransferWindow.ConsultationAloneSaveTheReconciliationOrItsReopening")
                    : L.Get("TransferWindow.ConsultationAloneTheChangeInTheTransferIs");
        }
        Loaded += (_, _) =>
        {
            if (editing is null) SourceAccount.Focus();
            else { EntryDate.Focus(); EntryDate.SelectAll(); }
        };
    }

    private static string ReconciledMessage => L.Get("TransferWindow.ReconciledTransfer");

    private static string FormatDate(DateOnly date) => date.ToString(L.DatePattern, DisplayCulture);
    private static string FormatAmount(Transaction value) => MoneyInput.FormatForEdit(value.Amount, value.Currency.DecimalPlaces);
    private static bool TryDate(string text, out DateOnly date) => DateOnly.TryParseExact(text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out date) && date != default;

    private void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var source = (SourceAccount.SelectedItem as AccountSummary)?.Account;
        var destination = (DestinationAccount.SelectedItem as AccountSummary)?.Account;
        var same = source is not null && destination is not null && source.Currency.Code == destination.Currency.Code;
        _updating = true;
        try
        {
            if (same && !_sameCurrency) _independentCredit = CreditAmount.Text;
            if (!same && _sameCurrency) CreditAmount.Text = _independentCredit;
            _sameCurrency = same;
            CreditAmount.IsReadOnly = same;
            if (same) CreditAmount.Text = DebitAmount.Text;
            DebitCurrency.Text = source?.Currency.Code ?? L.Get("Common.SourceCurrency");
            CreditCurrency.Text = destination?.Currency.Code ?? L.Get("TransferWindow.DestinationCurrency");
            ExchangeFields.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
            AmountHint.Text = same
                ? L.Get("TransferWindow.TheSameCurrencyTheCreditCopyExactlyThe")
                : L.Get("TransferWindow.DifferentCurrenciesFreelyEnterBothNativeAmountsWithout");
            if (source is not null && destination is not null)
                AmountHint.Text += L.Format("TransferWindow.Accuracy0123DecimalsWithoutRounding", (source.Currency.Code), (source.Currency.DecimalPlaces), (destination.Currency.Code), (destination.Currency.DecimalPlaces));
        }
        finally { _updating = false; }
        RefreshPreview();
    }

    private void Amount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (_sameCurrency && ReferenceEquals(sender, DebitAmount))
        {
            _updating = true;
            try { CreditAmount.Text = DebitAmount.Text; }
            finally { _updating = false; }
        }
        else if (!_sameCurrency && ReferenceEquals(sender, CreditAmount))
            _independentCredit = CreditAmount.Text;
        RefreshPreview();
    }

    private void Date_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (_exchangeDateFollowsTransfer)
        {
            _updating = true;
            try { ExchangeDate.Text = EntryDate.Text; }
            finally { _updating = false; }
        }
        RefreshPreview();
    }

    private void ExchangeDate_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        _exchangeDateFollowsTransfer = string.IsNullOrWhiteSpace(ExchangeDate.Text);
        RefreshPreview();
    }

    private void Preview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_updating) RefreshPreview();
    }

    private void Fee_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) RefreshFee();
    }

    private void FeeAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) RefreshFee();
    }

    private void RefreshFee()
    {
        var included = HasFee.IsChecked == true;
        FeeFields.IsEnabled = included;
        FeeAccount.IsEnabled = included && _editing?.Fee is null;
        var account = (FeeAccount.SelectedItem as AccountSummary)?.Account;
        FeeCurrency.Text = account?.Currency.Code ?? L.Get("TransferWindow.FeeAccountCurrency");
        FeeHint.Text = _editing?.Fee is not null
            ? L.Get("TransferWindow.ExistingFeesFixedAccountAndRetainedTransactionPlease")
            : L.Get("TransferWindow.OptionalFeesStrictlyPositiveToTheAdditionChoose");
        if (included && account is not null)
            FeeHint.Text += L.Format("TransferWindow.Accuracy0DecimalPlaceSWithoutRoundingExpenses", (account.Currency.DecimalPlaces));
    }

    private void RefreshPreview()
    {
        if (SourceAccount.SelectedItem is not AccountSummary { Account: var source } ||
            DestinationAccount.SelectedItem is not AccountSummary { Account: var destination })
        {
            RateDescription.Text = L.Get("TransferWindow.ChooseBothAccountsToViewTheExchangeReport");
            return;
        }
        if (source.Id == destination.Id)
        {
            RateDescription.Text = L.Get("TransferWindow.TheSourceAndRecipientAccountsMustBeSeparate");
            return;
        }
        try
        {
            var debit = MoneyInput.ParseLocalized(DebitAmount.Text, source.Currency);
            var credit = MoneyInput.ParseLocalized(CreditAmount.Text, destination.Currency);
            if (debit <= 0 || credit <= 0)
            {
                RateDescription.Text = L.Get("TransferWindow.BothAmountsMustBeStrictlyPositiveToDisplay");
                return;
            }
            if (!TryDate(EntryDate.Text, out var date))
            {
                RateDescription.Text = L.Get("TransferWindow.EnterAValidTransferDateMmDdYyyy");
                return;
            }
            DateOnly? exchangeDate = null;
            if (!_sameCurrency)
            {
                if (string.IsNullOrWhiteSpace(ExchangeDate.Text)) exchangeDate = date;
                else if (TryDate(ExchangeDate.Text, out var entered)) exchangeDate = entered;
                else
                {
                    RateDescription.Text = L.Get("TransferWindow.EnterAValidCurrencyExchangeDateMmDd");
                    return;
                }
            }
            RateDescription.Text = TransferRules.FormatEffectiveRate(debit, source.Currency, credit, destination.Currency)
                + (_sameCurrency ? L.Get("TransferWindow.SeparateCostsExcludedFromTheReport")
                    : L.Format("TransferWindow.DateOfExchange0SourceDeclaredNotVerified", (FormatDate(exchangeDate!.Value)), (ReadRateSource())));
        }
        catch (ArgumentException error)
        {
            RateDescription.Text = L.Get("TransferWindow.PendingReview") + error.Message;
        }
    }

    private string ReadRateSource() => string.IsNullOrWhiteSpace(RateSource.Text) ? DefaultRateSource : RateSource.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || _readOnly) return;
        _submitting = true;
        ConfirmTransfer.IsEnabled = false;
        ErrorText.Text = string.Empty;
        var staged = false;
        try
        {
            if (!_model.PendingReconciliations.IsEmpty)
                throw new InvalidOperationException(L.Get("TransferWindow.RecordTheReconciliationOrReopeningCtrlSBefore"));
            var current = _editing is null ? null : _model.GetTransfer(_editing.Value.Id);
            if (current?.HasReconciledLeg == true) throw new InvalidOperationException(ReconciledMessage);
            if (!TryBuildDraft(out var draft)) return;
            if (current is not null && ChangesMarkedFinancialData(current, draft)
                && MessageBox.Show(this,
                    L.Get("TransferWindow.APClearedLegWillChangeDateOr"),
                    L.Get("TransferWindow.MonetaCorrectAClearedTransfer"), MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (_editing is null) _model.CreateTransfer(draft);
            else _model.EditTransfer(_editing.Value.Id, _editing.Value.Revision, draft);
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
                ConfirmTransfer.IsEnabled = true;
            }
        }
    }

    private static bool ChangesMarkedFinancialData(TransferDetails current, TransferDraft draft) =>
        current.Debit.Status == ReconciliationStatus.Marked && (current.Debit.Date != draft.Date || current.Debit.Amount != draft.DebitedAmount)
        || current.Credit.Status == ReconciliationStatus.Marked && (current.Credit.Date != draft.Date || current.Credit.Amount != draft.CreditedAmount)
        || current.Fee is { Status: ReconciliationStatus.Marked } fee && (fee.Date != draft.Date || fee.Amount != draft.FeeAmount);

    private bool TryBuildDraft(out TransferDraft draft)
    {
        draft = null!;
        if (SourceAccount.SelectedItem is not AccountSummary { Account: var source })
            return ShowError(L.Get("TransferWindow.ChooseAnActiveSourceAccount"), SourceAccount);
        if (DestinationAccount.SelectedItem is not AccountSummary { Account: var destination })
            return ShowError(L.Get("TransferWindow.ChooseAnActiveRecipientAccount"), DestinationAccount);
        if (source.Id == destination.Id)
            return ShowError(L.Get("TransferWindow.TheSourceAndRecipientAccountsMustBeSeparate"), DestinationAccount);
        if (!TryDate(EntryDate.Text, out var date))
            return ShowError(L.Get("TransferWindow.InvalidDateEnterADateInMmDd"), EntryDate);
        if (!TryAmount(DebitAmount, source.Currency, false, out var debit)) return false;
        if (!TryAmount(CreditAmount, destination.Currency, false, out var credit)) return false;
        if (_sameCurrency && debit != credit)
            return ShowError(L.Get("TransferWindow.InTheSameCurrencyTheCreditMustBe"), DebitAmount);
        DateOnly? exchangeDate = null;
        if (!_sameCurrency)
        {
            if (string.IsNullOrWhiteSpace(ExchangeDate.Text)) exchangeDate = date;
            else if (TryDate(ExchangeDate.Text, out var entered)) exchangeDate = entered;
            else return ShowError(L.Get("TransferWindow.InvalidExchangeDateUseMmDdYyyy"), ExchangeDate);
        }
        Guid? feeAccountId = null;
        decimal feeAmount = 0;
        if (HasFee.IsChecked == true)
        {
            if (FeeAccount.SelectedItem is not AccountSummary { Account: var feeAccount })
                return ShowError(L.Get("TransferWindow.ChooseExplicitlyTheActiveAccountChargedForFees"), FeeAccount);
            feeAccountId = feeAccount.Id;
            if (!TryAmount(FeeAmount, feeAccount.Currency, _editing?.Fee is not null, out feeAmount)) return false;
        }
        if (EntryReference.Text.Length > 120)
            return ShowError(L.Get("TransferWindow.TheReferenceShallNotExceed120Characters"), EntryReference);
        if (EntryNotes.Text.Length > 2000)
            return ShowError(L.Get("TransferWindow.NotesMustNotExceed2000Characters"), EntryNotes);
        draft = new TransferDraft(source.Id, destination.Id, date, debit, credit,
            EntryReference.Text.Trim(), EntryNotes.Text, exchangeDate, _sameCurrency ? null : ReadRateSource(), feeAccountId, feeAmount);
        return true;
    }

    private bool TryAmount(TextBox field, Currency currency, bool allowZero, out decimal amount)
    {
        amount = 0;
        try
        {
            amount = MoneyInput.ParseLocalized(field.Text, currency);
            if (amount == 0 && !allowZero)
                return ShowError(L.Get("TransferWindow.EnterAStrictlyPositiveAmountZeroIsAllowed"), field);
            return true;
        }
        catch (ArgumentException error) { return ShowError(error.Message, field); }
    }

    private bool ShowError(string message, Control field)
    {
        ErrorText.Text = message;
        field.BringIntoView();
        field.Focus();
        if (field is TextBox text) text.SelectAll();
        return false;
    }
}
