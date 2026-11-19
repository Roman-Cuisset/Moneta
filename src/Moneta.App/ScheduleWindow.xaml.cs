using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class ScheduleWindow : Window
{
    private const int PageSize = 100;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _model;
    private readonly Func<Task<bool>> _save;
    private readonly Dictionary<Guid, Account> _accounts;
    private int _page;
    private int _historyPage;
    private bool _busy;

    private sealed record UpcomingRow(ScheduleOccurrence Occurrence, string Date, string Label, string Kind,
        string Accounts, string Amount, string Frequency, string State);
    private sealed record PlanRow(SchedulePlan Plan, string Next, string Label, string Kind,
        string Accounts, string Amount, string Frequency, string State);
    private sealed record HistoryRow(string Original, string Date, string Label, string Accounts, string Amount, string State);

    public ScheduleWindow(MainViewModel model, Func<Task<bool>> save)
    {
        _model = model;
        _save = save;
        _accounts = model.Accounts.ToDictionary(item => item.Account.Id, item => item.Account);
        InitializeComponent();
        Refresh();
        Loaded += (_, _) => Filter.Focus();
    }

    private void Refresh()
    {
        RefreshOccurrences();
        RefreshPlans();
        RefreshHistory();
        UpdateActions();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly Later(DateOnly date, int days) =>
        DateOnly.FromDayNumber(Math.Clamp(date.DayNumber + days, 0, DateOnly.MaxValue.DayNumber));

    private (DateOnly From, DateOnly Through) Range()
    {
        var today = Today;
        return Filter.SelectedIndex switch
        {
            1 => (Later(today, 1), Later(today, 365)),
            2 => (DateOnly.MinValue, Later(today, -1)),
            3 => (today, today),
            4 => (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
            _ => (DateOnly.MinValue, Later(today, 365))
        };
    }

    private void RefreshOccurrences()
    {
        var (from, through) = Range();
        var calendar = _model.ScheduleCalendar();
        var total = calendar.Count(from, through);
        _page = ClampPage(_page, total);
        var plans = _model.Schedules.ToDictionary(plan => plan.Id);
        var today = Today;
        Occurrences.ItemsSource = calendar.Between(from, through).Skip(_page * PageSize).Take(PageSize).Select(item =>
            new UpcomingRow(item, Date(item.Date), item.Terms.Label, Kind(item.Terms), Accounts(item.Terms), Amount(item.Terms),
                Frequency(plans[item.PlanId].Rule), IsArchived(item.Terms) ? "Compte archivé"
                    : item.IsDeferred ? (item.Date < today ? "Report · en retard" : "Reportée")
                    : item.Date < today ? "En retard" : item.Date == today ? "Aujourd’hui" : "À venir")).ToArray();
        if (Occurrences.Items.Count > 0) Occurrences.SelectedIndex = 0;
        PreviousPage.IsEnabled = _page > 0;
        NextPage.IsEnabled = (_page + 1L) * PageSize < total;
        var due = calendar.Count(DateOnly.MinValue, today);
        PageText.Text = PageCaption(_page, total) + $" · {due:N0} à traiter à ce jour";
    }

    private void RefreshPlans()
    {
        var deferred = _model.ScheduleHistory.GroupBy(item => (item.PlanId, item.Number))
            .Select(group => group.MaxBy(item => item.Step)!).Where(item => item.Outcome == ScheduleOutcome.Deferred)
            .ToLookup(item => item.PlanId);
        var selected = (Plans.SelectedItem as PlanRow)?.Plan.Id;
        Plans.ItemsSource = _model.Schedules.Where(plan => PlanFilter.SelectedIndex switch
        {
            1 => plan.Suspended && !plan.Retired,
            2 => plan.Retired,
            _ => !plan.Retired
        }).Select(plan =>
        {
            var next = deferred[plan.Id].Select(item => (DateOnly?)item.Date).Append(plan.Next).Min();
            return new PlanRow(plan, next is { } date ? Date(date) : "—", plan.Terms.Label, Kind(plan.Terms),
                Accounts(plan.Terms), Amount(plan.Terms), Frequency(plan.Rule), plan.Retired ? "Supprimée" :
                plan.Suspended ? "Suspendue" : next is null ? "Terminée" : "Active");
        }).ToArray();
        if (Plans.Items.Count > 0)
            Plans.SelectedItem = Plans.Items.Cast<PlanRow>().FirstOrDefault(row => row.Plan.Id == selected) ?? Plans.Items[0];
    }

    private void RefreshHistory()
    {
        var decisions = _model.ScheduleHistory.Where(item => HistoryFilter.SelectedIndex != 1 ||
            item.Outcome == ScheduleOutcome.Posted && _model.IsScheduleDecisionSaved(item)).ToArray();
        _historyPage = ClampPage(_historyPage, decisions.Length);
        History.ItemsSource = decisions.OrderByDescending(item => item.RecordedUtc).ThenBy(item => item.PlanId)
            .ThenByDescending(item => item.Number).ThenByDescending(item => item.Step).Skip(_historyPage * PageSize).Take(PageSize)
            .Select(item => new HistoryRow(Date(item.OriginalDate), Date(item.Date), item.Terms.Label, Accounts(item.Terms), Amount(item.Terms),
                _model.IsScheduleDecisionSaved(item) ? item.Outcome switch
                {
                    ScheduleOutcome.Posted => "Comptabilisée",
                    ScheduleOutcome.Deferred => "Reportée",
                    _ => "Ignorée"
                } : item.Outcome == ScheduleOutcome.Posted ? "Écriture en brouillon" : "Décision en brouillon")).ToArray();
        PreviousHistory.IsEnabled = _historyPage > 0;
        NextHistory.IsEnabled = (_historyPage + 1L) * PageSize < decisions.Length;
        HistoryPageText.Text = PageCaption(_historyPage, decisions.Length);
    }

    private static int ClampPage(int index, long total) => total == 0 ? 0 : (int)Math.Min(index, (total - 1) / PageSize);
    private static string PageCaption(int page, long total) => total == 0 ? "Aucun résultat" :
        $"{(long)page * PageSize + 1:N0}–{Math.Min((page + 1L) * PageSize, total):N0} sur {total:N0}";
    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", French);
    private static string Kind(ScheduleTerms terms) => terms.Kind switch { ScheduleKind.Expense => "Dépense", ScheduleKind.Income => "Recette", _ => "Virement" };
    private string Accounts(ScheduleTerms terms) => _accounts[terms.AccountId].Name +
        (terms.DestinationId is { } destination ? " → " + _accounts[destination].Name : "");
    private bool IsArchived(ScheduleTerms terms) => _accounts.Values.Any(account => account.IsArchived && terms.Uses(account.Id));

    private string Amount(ScheduleTerms terms)
    {
        string Format(long minor, Guid id)
        {
            var currency = _accounts[id].Currency;
            return $"{ExactMoney.FromMinorUnits(minor, currency).ToString($"N{currency.DecimalPlaces}", French)} {currency.Code}";
        }
        var source = Format(terms.AmountMinor, terms.AccountId);
        if (terms.DestinationId is not { } destination) return terms.Kind == ScheduleKind.Expense ? "−" + source : source;
        return source + " → " + (terms.DestinationMinor is { } received ? Format(received, destination) :
            _accounts[destination].Currency.Code == _accounts[terms.AccountId].Currency.Code ? Format(terms.AmountMinor, destination) : "à confirmer");
    }

    private static string Frequency(ScheduleRule rule)
    {
        if (rule.Unit == ScheduleUnit.Once) return "Une fois";
        var unit = rule.Unit switch { ScheduleUnit.Day => "j", ScheduleUnit.Week => "sem.", ScheduleUnit.Month => "mois", _ => "an(s)" };
        return $"{rule.Interval} {unit}" + (rule.EndOfMonth ? " · dernier jour" : "");
    }

    private void UpdateActions()
    {
        if (SaveDraft is null || Edit is null || Post is null) return;
        var canAct = Occurrences.SelectedItem is UpcomingRow { Occurrence.Actionable: true } && !_busy;
        Post.IsEnabled = Defer.IsEnabled = Skip.IsEnabled = canAct;
        var active = Plans.SelectedItem as PlanRow;
        Edit.IsEnabled = Suspend.IsEnabled = Retire.IsEnabled = active is { Plan.Retired: false } && !_busy;
        Suspend.Content = active?.Plan.Suspended == true ? "_Reprendre" : "_Suspendre";
        SaveDraft.IsEnabled = !_busy && _model.IsDirty;
        NewPlan.IsEnabled = !_busy;
    }

    private void Apply(Action action, string message)
    {
        try
        {
            action();
            Refresh();
            Status.Text = message;
        }
        catch (Exception error) when (error is DocumentException or ArgumentException or InvalidOperationException or OverflowException)
        {
            Status.Text = error.Message;
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var edit = new ScheduleEditorWindow(_model) { Owner = this };
        if (edit.ShowDialog() != true) return;
        Apply(() => _model.AddSchedule(edit.Terms, edit.Rule, edit.Date, edit.Until), "Programmation en brouillon. Ctrl+S l’enregistre.");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Plans.SelectedItem is not PlanRow row) return;
        var edit = new ScheduleEditorWindow(_model, row.Plan) { Owner = this };
        if (edit.ShowDialog() != true) return;
        Apply(() => _model.EditSchedule(row.Plan.Id, edit.Terms, edit.Rule, edit.Date, edit.Until), "Échéances futures mises à jour dans le brouillon.");
    }

    private void Suspend_Click(object sender, RoutedEventArgs e)
    {
        if (Plans.SelectedItem is PlanRow row)
            Apply(() => _model.SuspendSchedule(row.Plan.Id, !row.Plan.Suspended), "État de la programmation mis à jour dans le brouillon.");
    }

    private void Retire_Click(object sender, RoutedEventArgs e)
    {
        if (Plans.SelectedItem is not PlanRow row || MessageBox.Show(this,
            $"Supprimer la programmation « {row.Label} » et ses échéances futures ? Les écritures et les décisions historiques sont conservées.",
            "Échéancier", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Apply(() => _model.RetireSchedule(row.Plan.Id), "Suppression de la programmation en brouillon.");
    }

    private void Post_Click(object sender, RoutedEventArgs e)
    {
        if (Occurrences.SelectedItem is not UpcomingRow row) return;
        var archived = _accounts.Values.FirstOrDefault(account => account.IsArchived && row.Occurrence.Terms.Uses(account.Id));
        if (archived is not null)
        {
            Status.Text = $"Le compte « {archived.Name} » est archivé. Réactivez-le avant de comptabiliser.";
            return;
        }
        var edit = new ScheduleEditorWindow(_model, occurrence: row.Occurrence) { Owner = this };
        if (edit.ShowDialog() != true) return;
        Apply(() => _model.PostScheduleOccurrence(row.Occurrence, edit.Terms, edit.Date, edit.RateProvenance),
            "Écriture préparée. Ctrl+S enregistre l’écriture et sa décision ensemble.");
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (Occurrences.SelectedItem is not UpcomingRow row || MessageBox.Show(this,
            $"Ignorer « {row.Label} » prévue le {row.Date} ? La décision sera conservée dans l’historique.", "Échéancier",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Apply(() => _model.SkipScheduleOccurrence(row.Occurrence), "Occurrence ignorée dans le brouillon.");
    }

    private void Defer_Click(object sender, RoutedEventArgs e)
    {
        if (Occurrences.SelectedItem is not UpcomingRow row) return;
        var dialog = new ScheduleReportWindow(Later(row.Occurrence.Date, 1)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Apply(() => _model.DeferScheduleOccurrence(row.Occurrence, dialog.Date), "Report préparé. Les échéances suivantes restent disponibles.");
    }

    private async Task SaveAsync()
    {
        if (_busy || !_model.IsDirty) return;
        _busy = true;
        IsEnabled = false;
        try
        {
            if (await _save())
            {
                Refresh();
                Status.Text = "Dossier enregistré. Les décisions de l’échéancier sont confirmées.";
            }
        }
        finally
        {
            _busy = false;
            IsEnabled = true;
            UpdateActions();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        await SaveAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { _page = 0; if (IsLoaded) RefreshOccurrences(); }
    private void PlanFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RefreshPlans(); }
    private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) { _historyPage = 0; if (IsLoaded) RefreshHistory(); }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void Previous_Click(object sender, RoutedEventArgs e) { _page--; RefreshOccurrences(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _page++; RefreshOccurrences(); }
    private void PreviousHistory_Click(object sender, RoutedEventArgs e) { _historyPage--; RefreshHistory(); }
    private void NextHistory_Click(object sender, RoutedEventArgs e) { _historyPage++; RefreshHistory(); }
}

internal sealed class ScheduleReportWindow : Window
{
    public DateOnly Date { get; private set; }

    public ScheduleReportWindow(DateOnly suggested)
    {
        Title = "Reporter l’occurrence";
        Width = 365;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(18) };
        var date = new TextBox { Text = suggested.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")), MaxLength = 10 };
        System.Windows.Automation.AutomationProperties.SetName(date, "Nouvelle date du report");
        panel.Children.Add(new Label { Content = "Nouvelle date (jj/mm/aaaa)", Target = date });
        panel.Children.Add(date);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) };
        panel.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var apply = new Button { Content = "Reporter", IsDefault = true };
        apply.Click += (_, _) =>
        {
            try { Date = ScheduleEditorWindow.ReadDate(date.Text); DialogResult = true; }
            catch (ArgumentException invalid) { error.Text = invalid.Message; }
        };
        buttons.Children.Add(apply);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { date.Focus(); date.SelectAll(); };
    }
}
