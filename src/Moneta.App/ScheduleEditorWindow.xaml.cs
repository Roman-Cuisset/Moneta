using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class ScheduleEditorWindow : Window
{
    private sealed record Choice<T>(T Value, string Name);
    private sealed record AccountChoice(Account? Account, string Name);
    private static CultureInfo DisplayCulture => L.Culture;
    private readonly Dictionary<Guid, Account> _accounts;
    private readonly bool _posting;

    public ScheduleTerms Terms { get; private set; } = null!;
    public ScheduleRule Rule { get; private set; } = new(ScheduleUnit.Once);
    public DateOnly Date { get; private set; }
    public DateOnly? Until { get; private set; }
    public string? RateProvenance { get; private set; }

    public ScheduleEditorWindow(MainViewModel model, SchedulePlan? plan = null, ScheduleOccurrence? occurrence = null)
    {
        _posting = occurrence is not null;
        _accounts = model.Accounts.ToDictionary(item => item.Account.Id, item => item.Account);
        InitializeComponent();
        Heading.Text = _posting ? "Cette occurrence · confirmation avant Ctrl+S" :
            plan is null ? L.Get("ScheduleEditorWindow.NewSchedule") : L.Get("ScheduleEditorWindow.EditUpcomingTransactions");
        var terms = occurrence?.Terms ?? plan?.Terms;
        Kind.ItemsSource = new Choice<ScheduleKind>[] { new(ScheduleKind.Expense, L.Get("Common.Expense")), new(ScheduleKind.Income, L.Get("Common.Income2")), new(ScheduleKind.Transfer, L.Get("Common.Transfer")) };
        Unit.ItemsSource = new Choice<ScheduleUnit>[] { new(ScheduleUnit.Once, L.Get("ScheduleEditorWindow.OnlyOnce")), new(ScheduleUnit.Day, L.Get("ScheduleEditorWindow.EveryDay")),
            new(ScheduleUnit.Week, L.Get("ScheduleEditorWindow.EveryNWeeks")), new(ScheduleUnit.Month, L.Get("ScheduleEditorWindow.EveryNMonths")), new(ScheduleUnit.Year, L.Get("ScheduleEditorWindow.EveryNYears")) };
        Kind.SelectedIndex = (int)(terms?.Kind ?? ScheduleKind.Expense);
        Unit.SelectedIndex = (int)(plan?.Rule.Unit ?? ScheduleUnit.Month);
        Interval.Text = (plan?.Rule.Interval ?? 1).ToString(DisplayCulture);
        LastDay.IsChecked = plan?.Rule.EndOfMonth == true;
        Date = occurrence?.Date ?? plan?.Next ?? DateOnly.FromDateTime(DateTime.Today);
        Start.Text = Date.ToString(L.DatePattern, DisplayCulture);
        End.Text = plan?.End?.ToString(L.DatePattern, DisplayCulture) ?? "";
        var options = model.Accounts.Where(item => !item.Account.IsArchived || terms?.Uses(item.Account.Id) == true)
            .Select(item => new AccountChoice(item.Account, item.DisplayName)).ToArray();
        Source.ItemsSource = Destination.ItemsSource = options;
        FeeAccount.ItemsSource = new[] { new AccountChoice(null, L.Get("ScheduleEditorWindow.FreeOfCharge")) }.Concat(options).ToArray();
        Source.SelectedItem = options.FirstOrDefault(item => item.Account?.Id == (terms?.AccountId ?? model.SelectedAccountId)) ?? options.FirstOrDefault();
        Destination.SelectedItem = options.FirstOrDefault(item => item.Account?.Id == terms?.DestinationId && terms?.DestinationId is not null);
        FeeAccount.SelectedIndex = 0;
        if (terms?.FeeAccountId is { } fee) FeeAccount.SelectedItem = options.Single(item => item.Account?.Id == fee);
        if (terms is not null)
        {
            LabelText.Text = terms.Label;
            Notes.Text = terms.Notes;
            Payee.Text = terms.Payee ?? "";
            Method.Text = terms.Method ?? "";
            Category.Text = terms.Category ?? "";
            Subcategory.Text = terms.Subcategory ?? "";
            Amount.Text = Format(terms.AmountMinor, _accounts[terms.AccountId].Currency);
            if (terms.DestinationId is { } target)
            {
                var currency = _accounts[target].Currency;
                if (terms.DestinationMinor is { } received) Received.Text = Format(received, currency);
                else if (_posting && currency.Code == _accounts[terms.AccountId].Currency.Code)
                    Received.Text = Format(terms.AmountMinor, currency);
            }
            if (terms.FeeAccountId is { } feeId) Fee.Text = Format(terms.FeeMinor, _accounts[feeId].Currency);
        }
        if (_posting)
        {
            Kind.IsEnabled = Source.IsEnabled = Destination.IsEnabled = false;
            EndFields.Visibility = FrequencyFields.Visibility = Visibility.Collapsed;
            Accept.Content = L.Get("ScheduleEditorWindow.StageTransaction");
            StartLabel.Content = L.Get("ScheduleEditorWindow.DateToBeRecordedMmDdYyyy");
        }
        else if (plan is not null) StartLabel.Content = L.Get("ScheduleEditorWindow.NextDateMmDdYyyy");
        RefreshFields();
        Loaded += (_, _) => { LabelText.Focus(); LabelText.SelectAll(); };
    }

    private void PresentationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RateNotice is not null && FrequencyFields is not null) RefreshFields();
    }

    private void RefreshFields()
    {
        var transfer = (Kind.SelectedItem as Choice<ScheduleKind>)?.Value == ScheduleKind.Transfer;
        OrdinaryFields.Visibility = transfer ? Visibility.Collapsed : Visibility.Visible;
        TransferFields.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
        var source = Selected(Source)?.Currency.Code;
        var destination = Selected(Destination)?.Currency.Code;
        var cross = source is not null && destination is not null && source != destination;
        SourceLabel.Content = source is null ? L.Get("Common.Amount") : L.Format("ScheduleEditorWindow.Amount0", (source));
        ReceivedLabel.Content = destination is null ? L.Get("ScheduleEditorWindow.AmountReceived") : L.Format("ScheduleEditorWindow.AmountReceived0", (destination));
        FeeLabel.Content = Selected(FeeAccount) is { } fee ? L.Format("ScheduleEditorWindow.FeeAmount0", (fee.Currency.Code)) : L.Get("ScheduleEditorWindow.FeeAmount");
        Fee.IsEnabled = Selected(FeeAccount) is not null;
        RateNotice.Text = cross
            ? (_posting ? L.Get("ScheduleEditorWindow.ConfirmTheTwoActualAmountsTheExactReport")
                : L.Get("ScheduleEditorWindow.TheAmountReceivedMayRemainEmptyUntilConfirmation"))
            : L.Get("ScheduleEditorWindow.ForTwoAccountsInTheSameCurrencyThe");
        RateFields.Visibility = _posting && cross ? Visibility.Visible : Visibility.Collapsed;
        var unit = (Unit.SelectedItem as Choice<ScheduleUnit>)?.Value ?? ScheduleUnit.Once;
        Interval.IsEnabled = unit != ScheduleUnit.Once;
        LastDay.IsEnabled = unit is ScheduleUnit.Month or ScheduleUnit.Year;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = Selected(Source) ?? throw new ArgumentException(L.Get("ScheduleEditorWindow.ChooseTheAccount"));
            var kind = ((Choice<ScheduleKind>)Kind.SelectedItem).Value;
            var date = ReadDate(Start.Text);
            var end = _posting || string.IsNullOrWhiteSpace(End.Text) ? (DateOnly?)null : ReadDate(End.Text);
            if (end < date) throw new ArgumentException(L.Get("ScheduleEditorWindow.TheEndDateMustBeAfterOrEqual"));
            var unit = ((Choice<ScheduleUnit>)Unit.SelectedItem).Value;
            var interval = 1;
            if (!_posting && unit != ScheduleUnit.Once && !int.TryParse(Interval.Text, NumberStyles.None, DisplayCulture, out interval))
                throw new ArgumentException(L.Get("ScheduleEditorWindow.EnterAnEntireIntervalBetween1And366"));
            var rule = new ScheduleRule(unit, interval, LastDay.IsEnabled && LastDay.IsChecked == true);
            rule.Validate();
            var amount = Parse(Amount.Text, source);
            var terms = kind == ScheduleKind.Transfer ? ReadTransfer(source, amount)
                : new ScheduleTerms(kind, source.Id, amount, LabelText.Text.Trim(), Notes.Text, Optional(Payee), Optional(Category), Optional(Subcategory), Optional(Method));
            terms.Validate(_accounts, active: _posting, posting: _posting);
            Terms = terms;
            Date = date;
            Until = end;
            Rule = rule;
            RateProvenance = RateFields.Visibility == Visibility.Visible ? Optional(RateSource) : null;
            DialogResult = true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            ErrorText.Text = error.Message;
        }
    }

    private ScheduleTerms ReadTransfer(Account source, long amount)
    {
        var destination = Selected(Destination) ?? throw new ArgumentException(L.Get("ScheduleEditorWindow.ChooseTheRecipientAccount"));
        var fee = Selected(FeeAccount);
        long? received = string.IsNullOrWhiteSpace(Received.Text) ? null : Parse(Received.Text, destination);
        return new(ScheduleKind.Transfer, source.Id, amount, LabelText.Text.Trim(), Notes.Text,
            DestinationId: destination.Id, DestinationMinor: received, FeeAccountId: fee?.Id,
            FeeMinor: fee is null ? 0L : Parse(Fee.Text, fee));
    }

    private static Account? Selected(ComboBox control) => (control.SelectedItem as AccountChoice)?.Account;
    private static long Parse(string value, Account account) => ExactMoney.ToMinorUnits(MoneyInput.ParseLocalized(value, account.Currency), account.Currency);
    private static string Format(long value, Currency currency) => MoneyInput.FormatForEdit(ExactMoney.FromMinorUnits(value, currency), currency.DecimalPlaces);
    private static string? Optional(TextBox control) => string.IsNullOrWhiteSpace(control.Text) ? null : control.Text.Trim();
    internal static DateOnly ReadDate(string value) => DateOnly.TryParseExact(value.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date) && date != default
        ? date : throw new ArgumentException(L.Get("ScheduleEditorWindow.EnterADateInMmDdYyyyFormat"));
}
