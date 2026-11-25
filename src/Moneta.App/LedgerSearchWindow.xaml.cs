using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class LedgerSearchWindow : Window
{
    private sealed record Choice<T>(T Value, string Label);

    private sealed record Result(LedgerSearchRow Row)
    {
        public string Date => Row.Date.ToString(L.DatePattern, DisplayCulture);
        public string Account => $"{Row.AccountOrdinal + 1}. {(Row.Archived ? L.Get("Search.ArchivedPrefix") : "")}{Row.AccountName}";
        public string Amount => $"{Row.SignedAmount.ToString($"N{Row.DecimalPlaces}", DisplayCulture)} {Row.CurrencyCode}";
        public string Category => Row.Subcategory.Length == 0 ? Row.Category : $"{Row.Category} / {Row.Subcategory}";
        public string Status => Row.Status switch { ReconciliationStatus.Marked => "P", ReconciliationStatus.Reconciled => "R", _ => "NP" };
        public string Kind => Row.TransferRole switch
        {
            LedgerTransferLeg.Fee => L.Get("LedgerSearchWindow.LinkedFee"),
            LedgerTransferLeg.Debit or LedgerTransferLeg.Credit => L.Get("Common.Transfer"),
            _ => Row.Direction == TransactionDirection.Credit ? L.Get("Common.Income2") : L.Get("Common.Expense")
        };
    }

    private static CultureInfo DisplayCulture => L.Culture;
    private readonly string _path;
    private readonly Guid _documentId;
    private readonly Func<LedgerSearchRow, Task<bool>> _navigate;
    private readonly Func<(Guid? Id, string? Name)> _registerAccount;
    private CancellationTokenSource? _request;
    private LedgerSearchCriteria? _applied;
    private LedgerSearchPage? _page;
    private int _requestedPage;
    private Guid? _selectedResultId;
    private Guid _accountId;
    private string _accountName = "";
    private bool _periodUpdating;
    private bool _navigating;
    private bool _refreshOnShow;
    private bool _closed;
    private bool _localizing;
    private CultureInfo _inputCulture = L.Culture;

    public LedgerSearchWindow(string path, FinancialDocument document, Func<LedgerSearchRow, Task<bool>> navigate,
        Func<(Guid? Id, string? Name)> registerAccount)
    {
        _path = path;
        _documentId = document.Id;
        _navigate = navigate;
        _registerAccount = registerAccount;
        InitializeComponent();
        RefreshChoices();
        UpdateCurrencies(document);
        PropertyChangedEventManager.AddHandler(L.Current, LanguageChanged, nameof(L.CultureName));
        Results.SelectionChanged += (_, _) => OpenResult.IsEnabled = Results.SelectedItem is Result;
        Closed += (_, _) => { _closed = true; CancelSearch(); };
    }

    private void RefreshChoices()
    {
        Bind(Kind, new Choice<LedgerSearchKind>[] { new(LedgerSearchKind.All, L.Get("LedgerSearchWindow.AllTypes")), new(LedgerSearchKind.Income, L.Get("Common.Income")), new(LedgerSearchKind.Expense, L.Get("Common.Expenses")), new(LedgerSearchKind.Transfers, L.Get("LedgerSearchWindow.TransfersAndFees")), new(LedgerSearchKind.Fees, L.Get("Common.TransferFees")) });
        Bind(Pointing, new Choice<LedgerSearchStatus>[] { new(LedgerSearchStatus.All, L.Get("LedgerSearchWindow.AllStates")), new(LedgerSearchStatus.Unmarked, "NP"), new(LedgerSearchStatus.Marked, "P"), new(LedgerSearchStatus.Reconciled, "R"),
            new(LedgerSearchStatus.Unmarked | LedgerSearchStatus.Marked, "NP + P"), new(LedgerSearchStatus.Marked | LedgerSearchStatus.Reconciled, "P + R") });
        Bind(SortOrder, new Choice<LedgerSearchSort>[] { new(LedgerSearchSort.Date, L.Get("Common.Date")), new(LedgerSearchSort.Amount, L.Get("Common.Amount")), new(LedgerSearchSort.Label, L.Get("Common.Description")),
            new(LedgerSearchSort.Account, L.Get("Common.Account")), new(LedgerSearchSort.Category, L.Get("Common.Category")), new(LedgerSearchSort.Payee, L.Get("Common.Payee")), new(LedgerSearchSort.Status, L.Get("Common.ClearedStatus")) });
    }

    private static void Bind<T>(ComboBox control, IEnumerable<Choice<T>> items)
    {
        var index = Math.Max(0, control.SelectedIndex);
        control.ItemsSource = items.ToArray();
        control.DisplayMemberPath = nameof(Choice<T>.Label);
        control.SelectedIndex = Math.Min(index, control.Items.Count - 1);
    }

    private static T Value<T>(ComboBox control) => ((Choice<T>)control.SelectedItem).Value;

    internal void Open(string? words, bool advanced, bool returning = false, bool all = false)
    {
        if (!returning)
        {
            if (!advanced) ResetFields();
            if (words is not null) Words.Text = words;
            if (all || !advanced) Scope.SelectedIndex = all ? 1 : 0;
            Advanced.IsExpanded = advanced;
        }
        if (!IsVisible) Show();
        Activate();
        if (returning && _page is not null)
            Results.Focus();
        else
        {
            Words.Focus();
            Words.SelectAll();
        }
        if (!returning) _ = ApplyAsync(useRegisterAccount: true);
        else if (_refreshOnShow) _ = RefreshAsync();
    }

    internal void UpdateCurrencies(FinancialDocument document)
    {
        string? selection = (CountryCurrency.SelectedItem as Choice<string?>)?.Value;
        Bind(CountryCurrency, new[] { new Choice<string?>(null, L.Get("Search.EachCurrency")) }
            .Concat(document.Currencies.Select(currency => new Choice<string?>(currency.Code, currency.Code))));
        if (selection is not null)
            CountryCurrency.SelectedItem = CountryCurrency.Items.Cast<Choice<string?>>().SingleOrDefault(item => item.Value == selection)
                ?? CountryCurrency.Items[0];
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        _localizing = _periodUpdating = true;
        try
        {
            RefreshChoices();
            var choices = CountryCurrency.Items.Cast<Choice<string?>>().ToArray();
            choices[0] = new(null, L.Get("Search.EachCurrency"));
            Bind(CountryCurrency, choices);
            foreach (var field in new[] { FromDate, ThroughDate })
            {
                string pattern = _inputCulture.Name == L.English ? "MM/dd/yyyy" : "dd/MM/yyyy";
                if (DateOnly.TryParseExact(field.Text.Trim(), pattern, _inputCulture, DateTimeStyles.None, out var date))
                    field.Text = date.ToString(L.DatePattern, L.Culture);
            }
            foreach (var field in new[] { ExactAmount, MinAmount, MaxAmount })
            {
                try
                {
                    if (LedgerSearchCriteria.ParseAmount(field.Text, _inputCulture) is { } amount)
                        field.Text = MoneyInput.FormatForEdit(amount);
                }
                catch (ArgumentException) { /* Keep malformed input available for correction. */ }
            }
            _inputCulture = L.Culture;
            if (_page is { } page && _applied is { } criteria)
            {
                _selectedResultId = (Results.SelectedItem as Result)?.Row.Id;
                var (id, name) = _registerAccount();
                if (id == criteria.AccountId && name is not null) _accountName = name;
                RenderPage(page, criteria);
            }
            else if (_request is not null)
                SearchStatus.Text = L.Get("LedgerSearchWindow.SearchingRecordedTransactions");
        }
        finally { _localizing = _periodUpdating = false; }
    }

    internal void RefreshAfterSave()
    {
        _refreshOnShow = true;
        CancelSearch();
        if (IsVisible && _applied is not null) _ = RefreshAsync();
    }

    private Task RefreshAsync() => _applied is { } criteria
        ? RunAsync(criteria, _requestedPage) : ApplyAsync();

    private LedgerSearchCriteria ReadCriteria() => new()
    {
        AccountId = Scope.SelectedIndex == 0 ? _accountId : null,
        IncludeArchived = Archives.IsChecked == true,
        Text = Words.Text.Trim(), Category = Category.Text.Trim(), Subcategory = Subcategory.Text.Trim(),
        Payee = Payee.Text.Trim(), PaymentMethod = Method.Text.Trim(), CurrencyCode = Value<string?>(CountryCurrency),
        From = ReadDate(FromDate.Text), Through = ReadDate(ThroughDate.Text),
        Exact = LedgerSearchCriteria.ParseAmount(ExactAmount.Text),
        Minimum = LedgerSearchCriteria.ParseAmount(MinAmount.Text), Maximum = LedgerSearchCriteria.ParseAmount(MaxAmount.Text),
        Kind = Value<LedgerSearchKind>(Kind), Status = Value<LedgerSearchStatus>(Pointing),
        Sort = Value<LedgerSearchSort>(SortOrder), Descending = Descending.IsChecked == true
    };

    private static DateOnly? ReadDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateOnly.TryParseExact(text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException(L.Get("LedgerSearchWindow.EnterTheDatesInMmDdYyyyFormat"));
    }

    private Task ApplyAsync(bool useRegisterAccount = false)
    {
        CancelSearch();
        try
        {
            if (useRegisterAccount)
            {
                var (id, name) = _registerAccount();
                _accountId = id ?? Guid.Empty;
                _accountName = name ?? L.Get("Common.NoAccountSelected");
            }
            var criteria = ReadCriteria();
            criteria.Validate();
            return RunAsync(criteria, 0);
        }
        catch (ArgumentException error)
        {
            _applied = null;
            _requestedPage = 0;
            _selectedResultId = null;
            _refreshOnShow = false;
            ResetPage();
            SearchStatus.Text = error.Message;
            return Task.CompletedTask;
        }
    }

    private async Task RunAsync(LedgerSearchCriteria criteria, int index)
    {
        CancelSearch();
        using var request = new CancellationTokenSource();
        _request = request;
        // A replacement request can arrive while the result grid is already empty.
        if (_page is not null) _selectedResultId = (Results.SelectedItem as Result)?.Row.Id;
        _requestedPage = index;
        var token = request.Token;
        _applied = criteria;
        _refreshOnShow = false;
        ResetPage();
        SearchStatus.Text = L.Get("LedgerSearchWindow.SearchingRecordedTransactions");
        try
        {
            var page = await Task.Run(() => MonetaFile.SearchOperations(_path, _documentId, criteria, index, token), token);
            if (_request != request) return;
            _page = page;
            _requestedPage = page.Index;
            RenderPage(page, criteria);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is DocumentException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (_request == request) SearchStatus.Text = error is DocumentException or ArgumentException ? error.Message : L.Get("LedgerSearchWindow.SearchInterruptedTryAgain");
        }
        finally { if (_request == request) _request = null; }
    }

    private void RenderPage(LedgerSearchPage page, LedgerSearchCriteria criteria)
    {
        var rows = page.Rows.Select(row => new Result(row)).ToArray();
        Results.ItemsSource = rows;
        if (_selectedResultId is { } id) Results.SelectedItem = rows.FirstOrDefault(row => row.Row.Id == id);
        Previous.IsEnabled = page.Index > 0;
        var pages = (page.Total + LedgerSearchCriteria.PageSize - 1) / LedgerSearchCriteria.PageSize;
        Next.IsEnabled = page.Index + 1 < pages;
        PageStatus.Text = pages == 0 ? "" : L.Format("LedgerSearchWindow.Page01", page.Index + 1, pages);
        var filtered = criteria.HasFilters;
        var scope = criteria.AccountId is null
            ? L.Get(criteria.IncludeArchived ? "LedgerSearchWindow.AllAccountsWithArchive" : "LedgerSearchWindow.ActiveAccounts")
            : _accountName;
        SearchStatus.Text = $"{scope} · " + (page.Total == 0
            ? L.Get(filtered ? "LedgerSearchWindow.NoResultsForAppliedFilters" : "LedgerSearchWindow.NoTransactionsRecorded")
            : L.Format("LedgerSearchWindow.0N0ResultS1", page.Total, filtered ? L.Get("Search.FiltersApplied") : ""));
    }

    private void ResetPage()
    {
        _page = null;
        Results.ItemsSource = null;
        Previous.IsEnabled = Next.IsEnabled = false;
        PageStatus.Text = "";
    }

    private void CancelSearch()
    {
        var pending = _request;
        _request = null;
        pending?.Cancel();
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FromDate is null || ThroughDate is null || Period.SelectedIndex == 4) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);
        (DateOnly?, DateOnly?) dates = Period.SelectedIndex switch
        {
            1 => (month, month.AddMonths(1).AddDays(-1)),
            2 => (month.AddMonths(-1), month.AddDays(-1)),
            3 => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
            _ => (null, null)
        };
        _periodUpdating = true;
        FromDate.Text = dates.Item1?.ToString(L.DatePattern, DisplayCulture) ?? "";
        ThroughDate.Text = dates.Item2?.ToString(L.DatePattern, DisplayCulture) ?? "";
        _periodUpdating = false;
    }

    private void Date_Edited(object sender, TextChangedEventArgs e)
    {
        if (!_periodUpdating) Period.SelectedIndex = 4;
    }

    private void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Archives is not null) Archives.IsEnabled = Scope.SelectedIndex == 1;
    }

    private async void Sort_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_localizing && _applied is { } criteria)
            await RunAsync(criteria with { Sort = Value<LedgerSearchSort>(SortOrder), Descending = Descending.IsChecked == true }, 0);
    }

    internal Task ClearAsync()
    {
        ResetFields();
        return ApplyAsync();
    }

    private void ResetFields()
    {
        Words.Clear();
        ExactAmount.Clear();
        MinAmount.Clear();
        MaxAmount.Clear();
        FromDate.Clear();
        ThroughDate.Clear();
        Period.SelectedIndex = Kind.SelectedIndex = Pointing.SelectedIndex = CountryCurrency.SelectedIndex = 0;
        Payee.Clear();
        Category.Clear();
        Subcategory.Clear();
        Method.Clear();
    }

    private async Task OpenSelectedAsync()
    {
        if (_navigating || Results.SelectedItem is not Result result) return;
        _navigating = true;
        IsEnabled = false;
        try
        {
            if (await _navigate(result.Row))
            {
                if (!_closed) Hide();
            }
            else SearchStatus.Text = L.Get("LedgerSearchWindow.TheTransactionIsNoLongerAvailableInThe");
        }
        finally { _navigating = false; IsEnabled = true; }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync(useRegisterAccount: true);
    private async void Clear_Click(object sender, RoutedEventArgs e) => await ClearAsync();
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_applied is { } criteria && _page is { Index: > 0 } page) await RunAsync(criteria, page.Index - 1);
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_applied is { } criteria && _page is { } page && Next.IsEnabled) await RunAsync(criteria, page.Index + 1);
    }
    private async void Words_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ApplyAsync(useRegisterAccount: true);
    }
    private async void Results_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await OpenSelectedAsync();
    }
    private async void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Results, source) is DataGridRow)
            await OpenSelectedAsync();
    }
    private async void OpenResult_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();
    private async void SearchWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.Shift) { e.Handled = true; await ClearAsync(); }
    }

    private void SearchWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SearchInputs is not null) SearchInputs.MaxHeight = Math.Clamp(ActualHeight * 0.40, 128, 300);
    }
}
