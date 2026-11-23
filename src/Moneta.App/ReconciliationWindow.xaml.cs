using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class ReconciliationWindow : Window
{
    private const int PageSize = 100;
    private static CultureInfo DisplayCulture => L.Culture;
    private readonly MainViewModel _model;
    private readonly IReconciliationSource _source;
    private readonly Account _account;
    private HashSet<Guid> _selectedIds = new();
    private ReconciliationCandidatePage? _candidatePage;
    private ReconciliationHistoryPage? _historyPage;
    private ReconciliationDetails? _details;
    private DateOnly _loadedDate;
    private Guid? _latestActiveSessionId;
    private DateOnly? _previousStatementDate;
    private bool _ready;
    private string _reopenReason = string.Empty;
    private bool _candidatesValid;
    private bool _submitting;

    public ReconciliationWindow(MainViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var historyOnly = model.SelectedAccount?.Account.IsArchived == true;
        if (!model.CanReconcile && !historyOnly)
            throw new InvalidOperationException(L.Get("ReconciliationWindow.FirstSaveTheDocumentAndSelectAnActive"));
        _model = model;
        _source = model.ReconciliationSource
            ?? throw new InvalidOperationException(L.Get("ReconciliationWindow.ThisFileDoesNotAllowTheReadingOf"));
        _account = model.SelectedAccount?.Account
            ?? throw new InvalidOperationException(L.Get("ReconciliationWindow.SelectAnActiveAccount"));
        InitializeComponent();
        AccountHeading.Text = L.Format("ReconciliationWindow.BankReconciliation0", (model.SelectedAccount!.DisplayName));
        StatementDate.Text = DateOnly.FromDateTime(DateTime.Today).ToString(L.DatePattern, DisplayCulture);
        _ready = true;
        if (historyOnly)
        {
            StatementTab.Visibility = Visibility.Collapsed;
            HistoryTab.Header = L.Get("ReconciliationWindow.History");
            ReconciliationTabs.SelectedItem = HistoryTab;
            ReconciliationNotice.Text = L.Get("ReconciliationWindow.ArchivedAccountReactivateItAndSaveToResume");
            LoadHistory(0);
            return;
        }
        LoadCandidates(0, _selectedIds);
        LoadHistory(0);
        Loaded += (_, _) => { StatementDate.Focus(); StatementDate.SelectAll(); };
    }

    private string Money(decimal value) => $"{value.ToString($"N{_account.Currency.DecimalPlaces}", DisplayCulture)} {_account.Currency.Code}";

    private void EnsureAccount()
    {
        if (_model.SelectedAccountId != _account.Id)
            throw new InvalidOperationException(L.Get("ReconciliationWindow.TheSelectedAccountHasChangedCloseThisWindow"));
    }

    private DateOnly ReadDate()
    {
        if (!DateOnly.TryParseExact(StatementDate.Text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date)
            || date == default || date > DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException(L.Get("ReconciliationWindow.TheDateOfTheSurveyMustBeValid"));
        return date;
    }

    private void LoadCandidates(int offset, HashSet<Guid> selection)
    {
        if (_submitting)
            return;
        try
        {
            EnsureAccount();
            var date = ReadDate();
            // The source checks the entire selection, not only the visible hundred rows.
            var page = _source.ReadReconciliationCandidates(_account, date, selection, offset, PageSize);
            DateOnly? previousDate = _previousStatementDate;
            if (page.PreviousSessionId != _latestActiveSessionId || (page.PreviousSessionId is not null && previousDate is null))
                previousDate = page.PreviousSessionId is { } previous ? ReadPreviousStatementDate(previous) : null;
            var highlighted = ReconciliationCandidates.SelectedItems.Cast<CandidateRow>().Select(row => row.Original.Id).ToHashSet();
            var rows = page.Transactions.Select(value => new CandidateRow(value, selection.Contains(value.Id), _account.Currency)).ToArray();
            _selectedIds = selection;
            _candidatePage = page;
            _loadedDate = date;
            _latestActiveSessionId = page.PreviousSessionId;
            _previousStatementDate = previousDate;
            _candidatesValid = true;
            ReconciliationCandidates.ItemsSource = rows;
            foreach (var row in rows)
                if (highlighted.Contains(row.Original.Id)) ReconciliationCandidates.SelectedItems.Add(row);
            StatementOpeningBalance.Text = Money(page.OpeningBalance);
            LegacyWarning.Text = page.LegacyReconciledCount == 0 ? string.Empty
                : L.Format("ReconciliationWindow.LockedValidation0TransactionSRInheritedWithout", (page.LegacyReconciledCount.ToString("N0", DisplayCulture)));
            CandidatePageText.Text = Range(page.Offset, page.Transactions.Count, page.TotalCount, L.Get("ReconciliationWindow.Transactions"));
            ErrorText.Text = string.Empty;
        }
        catch (Exception error)
        {
            _candidatesValid = false;
            ErrorText.Text = L.Get("ReconciliationWindow.ReadingImpossibleYourFieldsAndSelectionAreKept") + error.Message;
        }
        UpdateSummary();
        UpdateReopenButton();
    }

    private DateOnly ReadPreviousStatementDate(Guid sessionId)
    {
        // Only statement metadata is needed here; do not load the previous statement's
        // potentially large collection of immutable operation snapshots for a preview.
        var cached = _historyPage?.Sessions.FirstOrDefault(session => session.Id == sessionId);
        if (cached is not null)
            return cached.StatementDate;
        for (var offset = 0; ;)
        {
            var page = _source.ReadReconciliationHistory(_account.Id, offset, PageSize);
            var session = page.Sessions.FirstOrDefault(value => value.Id == sessionId);
            if (session is not null)
                return session.StatementDate;
            if (page.Sessions.Count == 0 || offset + (long)PageSize >= page.TotalCount || offset > int.MaxValue - PageSize)
                throw new InvalidOperationException(L.Get("ReconciliationWindow.TheLastActiveRecordIsNotFoundIn"));
            offset += PageSize;
        }
    }

    private static string Range(int offset, int count, long total, string noun) => total == 0
        ? $"0 {noun}"
        : $"{(offset + 1L).ToString("N0", DisplayCulture)}–{(offset + (long)count).ToString("N0", DisplayCulture)} / {total.ToString("N0", DisplayCulture)} {noun}";

    private ReconciliationRequest ReadRequest()
    {
        EnsureAccount();
        var date = ReadDate();
        if (!_candidatesValid || _candidatePage is null || _loadedDate != date)
            throw new InvalidOperationException(L.Get("ReconciliationWindow.RefreshTheTransactionsForThisDateBeforeValidating"));
        if (!_model.CanReconcile)
            throw new InvalidOperationException(L.Get("ReconciliationWindow.TheRecordMustBeRecordedAndWithoutFurther"));
        if (_candidatePage.LegacyReconciledCount != 0)
            throw new InvalidOperationException(L.Get("ReconciliationWindow.FirstReleaseTheReconciliationsInheritedFromTheRegister"));
        if (_previousStatementDate is { } previous && date <= previous)
            throw new ArgumentException(L.Format("ReconciliationWindow.TheDateMustBeAfterTheLastActive", (previous.ToString(L.DatePattern, DisplayCulture))));
        var number = StatementNumber.Text.Trim();
        if (number.Length > 120)
            throw new ArgumentException(L.Get("ReconciliationWindow.TheReadingNumberShallNotExceed120Characters"));
        var expected = MoneyInput.ParseLocalized(StatementClosingBalance.Text, _account.Currency, allowNegative: true);
        var calculated = checked(_candidatePage.OpeningBalance + _candidatePage.SelectedNet);
        if (expected != calculated)
            throw new ArgumentException(L.Get("ReconciliationWindow.TheDeviationShallBeExactlyZeroToValidate"));
        return new ReconciliationRequest(date, expected, number);
    }

    private void UpdateSummary()
    {
        if (!_ready)
            return;
        ValidateReconciliation.IsEnabled = false;
        CandidatesPrevious.IsEnabled = !_submitting && _candidatesValid && _candidatePage?.Offset > 0;
        CandidatesNext.IsEnabled = !_submitting && _candidatesValid && _candidatePage is { } page
            && page.Offset <= int.MaxValue - PageSize && page.Offset + (long)PageSize < page.TotalCount;
        ReconciliationSelectedCount.Text = L.Format("ReconciliationWindow.Selection0TransactionS", (_selectedIds.Count.ToString("N0", DisplayCulture)));
        ReconciliationSelectedNet.Text = _candidatesValid && _candidatePage is { } current ? L.Get("ReconciliationWindow.SelectedNet") + Money(current.SelectedNet) : L.Get("ReconciliationWindow.NetSelectedNotUpdated");
        ReconciliationGap.Text = L.Get("ReconciliationWindow.VarianceNotCalculated");
        if (!_candidatesValid || _candidatePage is null)
            return;
        try
        {
            var expected = MoneyInput.ParseLocalized(StatementClosingBalance.Text, _account.Currency, allowNegative: true);
            var calculated = checked(_candidatePage.OpeningBalance + _candidatePage.SelectedNet);
            ReconciliationGap.Text = L.Get("ReconciliationWindow.Variance") + Money(checked(expected - calculated));
            ReadRequest();
            ValidateReconciliation.IsEnabled = !_submitting;
        }
        catch (Exception error)
        {
            // Keep incomplete input editable; the gap also explains why validation is unavailable.
            ReconciliationGap.ToolTip = error.Message;
            ReconciliationGap.Text += "\n" + error.Message;
            return;
        }
        ReconciliationGap.ToolTip = L.Get("ReconciliationWindow.FinalBalanceOfStatementOpeningNetSelected");
    }

    private void StatementDate_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _submitting)
            return;
        _candidatesValid = false;
        // Wait for a complete date, but invalidate the old preview immediately.
        if (DateOnly.TryParseExact(StatementDate.Text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date)
            && date != default && date <= DateOnly.FromDateTime(DateTime.Today))
            LoadCandidates(0, _selectedIds);
        else
        {
            ErrorText.Text = L.Get("ReconciliationWindow.EnterAValidStatementDateInMmDd");
            UpdateSummary();
        }
    }

    private void ClosingBalance_Changed(object sender, TextChangedEventArgs e) => UpdateSummary();
    private void CandidatesPrevious_Click(object sender, RoutedEventArgs e) => LoadCandidates(Math.Max(0, (_candidatePage?.Offset ?? 0) - PageSize), _selectedIds);
    private void CandidatesNext_Click(object sender, RoutedEventArgs e) => LoadCandidates(checked((_candidatePage?.Offset ?? 0) + PageSize), _selectedIds);
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => LoadCandidates(0, new HashSet<Guid>());
    private void RefreshCandidates_Click(object sender, RoutedEventArgs e) => LoadCandidates(_candidatePage?.Offset ?? 0, _selectedIds);

    private void ToggleRows(IEnumerable<CandidateRow> rows)
    {
        if (_submitting || !_candidatesValid)
            return;
        var values = rows.ToArray();
        if (values.Length == 0)
            return;
        var select = values.Any(row => !_selectedIds.Contains(row.Original.Id));
        var proposed = new HashSet<Guid>(_selectedIds);
        foreach (var row in values)
            if (select) proposed.Add(row.Original.Id); else proposed.Remove(row.Original.Id);
        LoadCandidates(_candidatePage?.Offset ?? 0, proposed);
    }

    private void CandidateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: CandidateRow row } checkBox)
            return;
        // TogglePattern changes IsChecked without raising Click. Handle the state
        // transition so mouse, keyboard and assistive technologies share one path.
        if (_ready && !_submitting && _candidatesValid &&
            (checkBox.IsChecked == true) != _selectedIds.Contains(row.Original.Id))
            ToggleRows(new[] { row });
        // Initial bindings and failed reads must not invert or misrepresent selection.
        checkBox.SetCurrentValue(CheckBox.IsCheckedProperty, _selectedIds.Contains(row.Original.Id));
        e.Handled = true;
    }

    private void Candidates_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || IsInteractiveChild(e.OriginalSource as DependencyObject))
            return;
        ToggleRows(ReconciliationCandidates.SelectedItems.Cast<CandidateRow>());
        e.Handled = true;
    }

    private void Candidates_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveChild(e.OriginalSource as DependencyObject))
            return;
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not CandidateRow candidate)
            return;
        ToggleRows(ReconciliationCandidates.SelectedItems.Contains(candidate)
            ? ReconciliationCandidates.SelectedItems.Cast<CandidateRow>() : new[] { candidate });
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value is not null)
        {
            if (value is T match) return match;
            value = value is Visual ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
        }
        return null;
    }

    private static bool IsInteractiveChild(DependencyObject? value) => FindParent<TextBoxBase>(value) is not null
        || FindParent<PasswordBox>(value) is not null || FindParent<ComboBox>(value) is not null
        || FindParent<ButtonBase>(value) is not null || FindParent<ScrollBar>(value) is not null;

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting)
            return;
        try
        {
            var request = ReadRequest();
            _submitting = true;
            UpdateSummary();
            _model.StageReconciliation(request, _selectedIds.ToArray());
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = L.Get("ReconciliationWindow.TheSurveyHasNotBeenValidatedYourFields") + error.Message;
            _submitting = false;
            UpdateSummary();
        }
    }

    private void LoadHistory(int offset)
    {
        if (_submitting)
            return;
        try
        {
            EnsureAccount();
            var page = _source.ReadReconciliationHistory(_account.Id, offset, PageSize);
            var rows = page.Sessions.Select(session => new HistoryRow(session, _account.Currency)).ToArray();
            _historyPage = page;
            ReconciliationHistory.ItemsSource = rows;
            HistoryPageText.Text = Range(page.Offset, page.Sessions.Count, page.TotalCount, L.Get("ReconciliationWindow.Readings"));
            HistoryPrevious.IsEnabled = page.Offset > 0;
            HistoryNext.IsEnabled = page.Offset <= int.MaxValue - PageSize && page.Offset + (long)PageSize < page.TotalCount;
            if (rows.Length > 0) ReconciliationHistory.SelectedItem = rows[0];
        }
        catch (Exception error)
        {
            ErrorText.Text = L.Get("ReconciliationWindow.HistoryCouldNotBeReadYourFieldsAre") + error.Message;
        }
    }

    private void HistoryPrevious_Click(object sender, RoutedEventArgs e) => LoadHistory(Math.Max(0, (_historyPage?.Offset ?? 0) - PageSize));
    private void HistoryNext_Click(object sender, RoutedEventArgs e) => LoadHistory(checked((_historyPage?.Offset ?? 0) + PageSize));

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ReconciliationHistory))
            return;
        _details = null;
        ReconciliationEntries.ItemsSource = null;
        ReconciliationEvents.ItemsSource = null;
        EntrySnapshotText.Text = string.Empty;
        if (ReconciliationHistory.SelectedItem is HistoryRow row)
        {
            try
            {
                EnsureAccount();
                var details = _source.ReadReconciliation(row.Session.Id);
                if (details.Session.AccountId != _account.Id)
                    throw new InvalidOperationException(L.Get("ReconciliationWindow.ThisStatementBelongsToAnotherAccount"));
                _details = details;
                ReconciliationEntries.ItemsSource = details.Entries.Select(entry => new CandidateRow(entry.Original, false, _account.Currency)).ToArray();
                ReconciliationEvents.ItemsSource = details.Events.Select(value => new EventRow(value)).ToArray();
                if (ReconciliationEntries.Items.Count > 0) ReconciliationEntries.SelectedIndex = 0;
            }
            catch (Exception error)
            {
                ErrorText.Text = L.Get("ReconciliationWindow.TheDetailsOfTheSurveyCouldNotBe") + error.Message;
            }
        }
        UpdateReopenButton();
    }

    private void UpdateReopenButton() => ReopenReconciliation.IsEnabled = !_submitting && _model.CanReconcile
        && _details?.Session is { State: ReconciliationSessionState.Validated } session && session.Id == _latestActiveSessionId;

    private void Reopen_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || _details?.Session is not { State: ReconciliationSessionState.Validated } session
            || session.Id != _latestActiveSessionId)
            return;
        _submitting = true;
        UpdateSummary();
        UpdateReopenButton();
        var staged = false;
        try
        {
            EnsureAccount();
            var reasonWindow = new ReopenReasonWindow(L.Get("ReconciliationWindow.ReopenTheBankStatement"),
                L.Get("ReconciliationWindow.TheSurveyAndItsSnapshotsWillRemainIn")) { Owner = this };
            reasonWindow.ReopenReason.Text = _reopenReason;
            if (reasonWindow.ShowDialog() != true)
                return;
            _reopenReason = reasonWindow.Reason;
            if (MessageBox.Show(this,
                L.Format("ReconciliationWindow.ReopenTheStatementOf01Reason2", (session.StatementDate.ToString(L.DatePattern, DisplayCulture)), (session.StatementNumber), (reasonWindow.Reason)),
                L.Get("ReconciliationWindow.ConfirmReopening"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            _model.ReopenReconciliation(session.Id, session.Revision, reasonWindow.Reason);
            staged = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = L.Get("ReconciliationWindow.TheSurveyHasNotBeenReopenedYourFields") + error.Message;
        }
        finally
        {
            if (!staged)
            {
                _submitting = false;
                UpdateSummary();
                UpdateReopenButton();
            }
        }
    }

    private void Entry_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ReconciliationEntries))
            return;
        if (ReconciliationEntries.SelectedItem is not CandidateRow row)
        {
            EntrySnapshotText.Text = string.Empty;
            return;
        }
        var value = row.Original;
        EntrySnapshotText.Text = L.Format("ReconciliationWindow.Transaction0Account1Revision2", (value.Id), (value.AccountId), (value.Revision))
            + L.Format("ReconciliationWindow.012Magnitude345Decimals", (row.DateText), (value.Label), (value.Direction), (value.Amount.ToString(CultureInfo.InvariantCulture)), (value.Currency.Code), (value.Currency.DecimalPlaces), (row.StatusText), (row.MarkedOnText))
            + L.Format("ReconciliationWindow.Payee01Category2345", (value.Payee.Name), (value.PayeeId), (value.Category.Name), (value.Category.Subcategory), (value.CategoryId), (value.SubcategoryId))
            + L.Format("ReconciliationWindow.Payment01OriginalReconciliation2Deleted3", (value.PaymentMode), (value.PaymentMethodId), (value.ReconciliationId), (value.IsDeleted))
            + L.Format("ReconciliationWindow.Created0OModified1O", (value.CreatedUtc), (value.ModifiedUtc))
            + (value.Valuation is { } valuation ? L.Format("Reconciliation.ValuationDetail", valuation.Amount.ToString(CultureInfo.InvariantCulture),
                valuation.Currency.Code, valuation.RateDate, valuation.Source) : string.Empty)
            + L.Format("ReconciliationWindow.Notes0", (value.Details));
    }

    public sealed class CandidateRow
    {
        public Transaction Original { get; }
        public bool Selected { get; }
        public string DateText => Original.Date.ToString(L.DatePattern, DisplayCulture);
        public string MarkedOnText => Original.MarkedOn?.ToString(L.DatePattern, DisplayCulture) ?? "—";
        public string StatusText => Original.Status switch { ReconciliationStatus.Unmarked => "NP", ReconciliationStatus.Marked => "P", _ => "R" };
        public string AmountText { get; }
        public CandidateRow(Transaction original, bool selected, Currency currency)
        {
            Original = original;
            Selected = selected;
            AmountText = $"{FinancialRules.SignedAmount(original).ToString($"N{currency.DecimalPlaces}", DisplayCulture)} {currency.Code}";
        }
    }

    public sealed class HistoryRow
    {
        public ReconciliationSession Session { get; }
        public string DateText => Session.StatementDate.ToString(L.DatePattern, DisplayCulture);
        public string StateText => Session.State == ReconciliationSessionState.Validated ? L.Get("ReconciliationWindow.Validated") : "Rouvert";
        public string OpeningText { get; }
        public string ClosingText { get; }
        public string ValidatedText => Session.ValidatedUtc.ToLocalTime().ToString(L.DatePattern + " HH:mm:ss", DisplayCulture);
        public string ReopenedText => Session.ReopenedUtc?.ToLocalTime().ToString(L.DatePattern + " HH:mm:ss", DisplayCulture) ?? "—";
        public HistoryRow(ReconciliationSession session, Currency currency)
        {
            Session = session;
            OpeningText = $"{session.OpeningBalance.ToString($"N{currency.DecimalPlaces}", DisplayCulture)} {currency.Code}";
            ClosingText = $"{session.ExpectedClosingBalance.ToString($"N{currency.DecimalPlaces}", DisplayCulture)} {currency.Code}";
        }
    }

    public sealed class EventRow
    {
        public ReconciliationEvent Event { get; }
        public string OccurredText => Event.OccurredUtc.ToLocalTime().ToString(L.DatePattern + " HH:mm:ss", DisplayCulture);
        public string KindText => Event.Kind switch { ReconciliationEventKind.Validated => L.Get("Reconciliation.Validated"), ReconciliationEventKind.Reopened => L.Get("ReconciliationWindow.Reopening"), _ => L.Get("ReconciliationWindow.InheritedRelease") };
        public EventRow(ReconciliationEvent value) => Event = value;
    }
}
