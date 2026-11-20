using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class ReportsWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly DocumentWorkspace _workspace;
    private readonly MainViewModel _model;
    private readonly FinancialReportEngine _engine;
    private readonly ReportForecast _forecast;
    private readonly Func<Task<bool>> _save;
    private CancellationTokenSource? _operation;
    private RenderedReport? _report;
    private Guid? _predecessor;
    private bool _fromArchive;
    private bool _closed;

    public ReportsWindow(DocumentWorkspace workspace, FinancialReportEngine engine, ReportForecast forecast,
        RateProvider provider, Func<Task<bool>> save, int tab = 0)
    {
        _workspace = workspace;
        _model = workspace.Model!;
        _engine = engine;
        _forecast = forecast;
        _save = save;
        InitializeComponent();
        var snapshot = Saved;
        Target.ItemsSource = snapshot.Currencies;
        Target.SelectedItem = snapshot.Currencies.FirstOrDefault(item => item.Code == _model.DisplayCurrency?.Code) ?? snapshot.Currencies[0];
        Category.ItemsSource = new[] { new BudgetEditorWindow.Choice(Guid.Empty, "Toutes les catégories") }
            .Concat(BudgetEditorWindow.CategoryChoices(snapshot)).ToArray();
        Category.SelectedIndex = 0;
        Provider.SelectedIndex = provider == RateProvider.Cbr ? 1 : 0;
        Period.SelectedIndex = 0;
        SetPeriod(Today);
        Grouping.SelectedIndex = PlotMode.SelectedIndex = 0;
        PopulateAccounts(selectAll: true);
        BudgetMonth.Text = Today.ToString("MM/yyyy", French);
        UpdateBudgetRows([]);
        UpdateArchives();
        Views.SelectedIndex = Math.Clamp(tab, 0, Views.Items.Count - 1);
        Status.Text = "Choisissez le périmètre, puis générez le rapport. Les écritures enregistrées sont utilisées.";
        Loaded += (_, _) => Generate.Focus();
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
        UpdateActions();
    }

    private FinancialDocument Saved => _workspace.SavedSnapshot!;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private RateProvider SelectedProvider => Provider.SelectedIndex == 1 ? RateProvider.Cbr : RateProvider.Nbrb;
    private HistoricalRateAccess Access => Network.IsChecked == true ? HistoricalRateAccess.FetchMissing : HistoricalRateAccess.CacheOnly;

    private void PopulateAccounts(bool selectAll)
    {
        var selected = Accounts.SelectedItems.Cast<BudgetEditorWindow.Choice>().Select(item => item.Id).ToHashSet();
        var snapshot = Saved;
        var visible = snapshot.Accounts.Where(item => Archived.IsChecked == true || !item.IsArchived).Select(item => item.Id).ToHashSet();
        Accounts.ItemsSource = BudgetEditorWindow.AccountChoices(snapshot).Where(item => visible.Contains(item.Id)).ToArray();
        foreach (var item in Accounts.Items.Cast<BudgetEditorWindow.Choice>().Where(item => selectAll || selected.Contains(item.Id)))
            Accounts.SelectedItems.Add(item);
    }

    private void Archived_Changed(object sender, RoutedEventArgs e) { if (Accounts is not null) PopulateAccounts(false); }
    private void AllAccounts_Click(object sender, RoutedEventArgs e) => PopulateAccounts(true);
    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StartDate is null || EndDate is null) return;
        var date = DateOnly.TryParseExact(StartDate.Text, "dd/MM/yyyy", French, DateTimeStyles.None, out var choice) ? choice : Today;
        if (Period.SelectedIndex < 3) SetPeriod(date);
    }
    private void PreviousPeriod_Click(object sender, RoutedEventArgs e) => ShiftPeriod(-1);
    private void NextPeriod_Click(object sender, RoutedEventArgs e) => ShiftPeriod(1);
    private void ShiftPeriod(int step)
    {
        if (_operation is not null) return;
        try
        {
            var start = ScheduleEditorWindow.ReadDate(StartDate.Text);
            if (Period.SelectedIndex == 3)
            {
                var end = ScheduleEditorWindow.ReadDate(EndDate.Text);
                var length = end.DayNumber - start.DayNumber + 1;
                if (length < 1) throw new ArgumentException("Vérifiez l’ordre des dates de la période.");
                ShowPeriod(start.AddDays(step * length), end.AddDays(step * length));
            }
            else SetPeriod(start.AddMonths(step * (Period.SelectedIndex == 2 ? 12 : Period.SelectedIndex == 1 ? 3 : 1)));
        }
        catch (ArgumentException error) { Status.Text = error.Message; }
    }

    private void SetPeriod(DateOnly date)
    {
        var firstMonth = Period.SelectedIndex == 2 ? 1 : Period.SelectedIndex == 1 ? (date.Month - 1) / 3 * 3 + 1 : date.Month;
        var months = Period.SelectedIndex == 2 ? 12 : Period.SelectedIndex == 1 ? 3 : 1;
        var lastMonth = firstMonth + months - 1;
        ShowPeriod(new(date.Year, firstMonth, 1), new(date.Year, lastMonth, DateTime.DaysInMonth(date.Year, lastMonth)));
    }

    private void ShowPeriod(DateOnly start, DateOnly end)
    {
        StartDate.Text = start.ToString("dd/MM/yyyy", French);
        EndDate.Text = end.ToString("dd/MM/yyyy", French);
    }

    private ReportCriteria ReadCriteria()
    {
        var chosen = (BudgetEditorWindow.Choice)Category.SelectedItem;
        return new(ScheduleEditorWindow.ReadDate(StartDate.Text), ScheduleEditorWindow.ReadDate(EndDate.Text),
            Accounts.SelectedItems.Cast<BudgetEditorWindow.Choice>().Select(item => item.Id).Order().ToArray(),
            Archived.IsChecked == true, chosen.Id == Guid.Empty ? null : chosen.Id, (Currency)Target.SelectedItem, SelectedProvider);
    }

    private async void Generate_Click(object sender, RoutedEventArgs e) => await Run("Génération du rapport…", async token =>
        await GenerateReport(ReadCriteria(), Access, null, token));

    private async Task GenerateReport(ReportCriteria criteria, HistoricalRateAccess access, Guid? predecessor, CancellationToken token)
    {
        var snapshot = Saved;
        var path = _workspace.FilePath!;
        criteria = criteria with { ObservedThrough = Today };
        var drafts = DraftPostings();
        var data = await Task.Run(async () =>
        {
            var source = MonetaFile.ReadReport(path, snapshot, criteria, cancellationToken: token);
            var report = await _engine.GenerateAsync(criteria, source, access, token).ConfigureAwait(false);
            return RenderedReport.Create(report);
        }, token);
        token.ThrowIfCancellationRequested();
        ShowReport(data, predecessor, false);
        var month = new DateOnly(criteria.From.Year, criteria.From.Month, 1);
        BudgetMonth.Text = month.ToString("MM/yyyy", French);
        var budgets = await CalculateBudgets(month, access == HistoricalRateAccess.Refresh ? HistoricalRateAccess.FetchMissing : access, token, data.Content);
        var forecast = await ProjectForecast(snapshot, criteria, false, HistoricalRateAccess.CacheOnly, drafts, token);
        token.ThrowIfCancellationRequested();
        UpdateBudgetRows(budgets);
        ShowForecast(forecast);
        Status.Text = predecessor is null ? "Rapport généré à partir du dossier enregistré." : "Recalcul prêt. Figez les résultats pour conserver cette nouvelle version.";
    }

    private void ShowReport(RenderedReport value, Guid? predecessor, bool frozen)
    {
        _report = value;
        _fromArchive = frozen;
        _predecessor = predecessor;
        var criteria = value.Content.Criteria;
        var target = criteria.Target;
        var total = value.Total;
        var missingStock = value.Content.Balances.Count(row => !row.Converted.HasValue);
        ScopeText.Text = $"{criteria.From:dd/MM/yyyy} au {criteria.Through:dd/MM/yyyy} · {criteria.Accounts.Count} compte(s) · {target.Code} · {RateRules.ProviderName(criteria.Provider)}";
        var coverage = total.Eligible == 0 ? "Aucun flux éligible." :
            $"{total.Valued:N0}/{total.Eligible:N0} flux valorisés ({(decimal)total.Valued * 100 / total.Eligible:N1} %)." +
            (total.Missing == 0 ? "" : $" {total.Missing:N0} sans valeur, voir Flux et cours.");
        var complete = total.Complete && missingStock == 0;
        CoverageCard.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, complete ? "AccentSoftBrush" : "WarningSurfaceBrush");
        Coverage.SetResourceReference(ForegroundProperty, complete ? "TextBrush" : "WarningBrush");
        CoverageCard.Visibility = Visibility.Visible;
        Coverage.Text = (complete ? "Rapport complet. " : "Rapport incomplet · montants connus seulement. ") +
            coverage + (missingStock == 0 ? "" : $" {missingStock:N0} solde(s) sans conversion, voir Soldes à date.");
        IncomeText.Text = Money(total.Income, target);
        ExpenseText.Text = Money(total.Expense, target);
        NetText.Text = total.Complete ? Money(total.Net, target) : "Indisponible";
        TopCategories.Text = string.Join(Environment.NewLine, value.Groups[ReportGrouping.Category].Where(row => row.Total.Expense > 0)
            .OrderByDescending(row => row.Total.Expense).Take(4).Select(row => $"{row.Label} · {Money(row.Total.Expense, target)}"));
        if (TopCategories.Text.Length == 0) TopCategories.Text = "Aucune dépense valorisée sur la période.";
        BalanceSummary.Text = value.Consolidated is { } stock ? $"Solde consolidé au {criteria.Through:dd/MM/yyyy} · {Money(stock, target)}"
            : $"Solde consolidé indisponible au {criteria.Through:dd/MM/yyyy} · {missingStock} compte(s) sans valorisation.";
        BalanceSummary.Text += " Le filtre de catégorie porte uniquement sur les flux.";
        Balances.ItemsSource = value.Content.Balances.Select(line => new LineView(value.Content, line)).ToArray();
        ShowFlows();
        ShowGroups();
        ShowPlot();
        if (frozen)
        {
            Forecasts.ItemsSource = null;
            ForecastMonths.ItemsSource = null;
            ForecastSummary.Text = "La preuve fige les flux et les soldes. Les prévisions se calculent depuis le dossier enregistré.";
            UpcomingText.Text = "Rapport figé · les prévisions sont séparées.";
            BudgetAlert.Text = "Rapport figé · les budgets mensuels restent consultables séparément.";
        }
        UpdateActions();
    }

    private void ShowFlows()
    {
        if (_report is not { } view || Flows is null) return;
        Flows.ItemsSource = MissingOnly.IsChecked == true ? view.Missing : view.Lines;
    }
    private void Missing_Changed(object sender, RoutedEventArgs e) => ShowFlows();
    private void Grouping_Changed(object sender, SelectionChangedEventArgs e) => ShowGroups();
    private void ShowGroups()
    {
        if (_report is not { } view || Groups is null || Grouping.SelectedIndex < 0) return;
        Groups.ItemsSource = view.Groups[(ReportGrouping)Grouping.SelectedIndex].Select(row => new GroupView(row, view.Content.Criteria.Target)).ToArray();
    }
    private void Plot_Changed(object sender, SelectionChangedEventArgs e) => ShowPlot();
    private void ShowPlot()
    {
        if (_report is not { } view || Plots is null || PlotMode.SelectedIndex < 0) return;
        var mode = PlotMode.SelectedIndex;
        var groups = mode == 0 ? view.Donut : view.Groups[ReportGrouping.Month];
        Plots.SetData(groups, view.Content.Criteria.Target, mode);
        ChartNotice.Text = !view.Total.Complete
            ? "Rapport incomplet. Graphiques des montants connus uniquement. * Mois avec des flux non valorisés."
            : "Les graphiques reprennent les lignes arrondies des tableaux du rapport.";
    }

    private void Detail_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var label = ReferenceEquals(grid, Flows) ? FlowEvidence : BalanceEvidence;
        if (label is not null) label.Text = (grid.SelectedItem as LineView)?.Evidence ?? "Sélectionnez une ligne pour voir sa provenance.";
    }

    private async void Project_Click(object sender, RoutedEventArgs e) => await Run("Calcul de la prévision…", async token =>
    {
        var criteria = ReadCriteria();
        var snapshot = Saved;
        var drafts = DraftPostings();
        var estimate = Estimate.IsChecked == true;
        var access = Access;
        var rows = await ProjectForecast(snapshot, criteria, estimate, access, drafts, token);
        token.ThrowIfCancellationRequested();
        ShowForecast(rows);
        Status.Text = "Prévision recalculée. Les échéances et les écritures enregistrées sont distinctes.";
    });

    private ScheduleDecision[] DraftPostings() => _model.ScheduleHistory.Where(item => item.Outcome == ScheduleOutcome.Posted &&
        !_model.IsScheduleDecisionSaved(item)).ToArray();

    private Task<ForecastDisplay> ProjectForecast(FinancialDocument snapshot, ReportCriteria criteria, bool estimate,
        HistoricalRateAccess access, IReadOnlyList<ScheduleDecision> drafts, CancellationToken token) => Task.Run(async () =>
    {
        var rows = await _forecast.GenerateAsync(snapshot, criteria, Today, estimate, access, token, drafts).ConfigureAwait(false);
        var monthly = ReportForecast.Monthly(rows, snapshot.Currencies, criteria.Target, estimate);
        return new ForecastDisplay(criteria.Target, rows.Select(row => new ForecastView(row, criteria.Target)).ToArray(),
            monthly.Select(row => new ForecastMonthView(row)).ToArray());
    }, token);

    private void ShowForecast(ForecastDisplay value)
    {
        Forecasts.ItemsSource = value.Lines;
        ForecastMonths.ItemsSource = value.Months;
        ForecastSummary.Text = value.Lines.Length == 0 ? "Aucune échéance à venir dans le périmètre." :
            $"{value.Lines.Length:N0} ligne(s) à venir sur un horizon maximum de 365 jours · cible {value.Target.Code}. " +
            "Les estimations utilisent la date de calcul et restent hors réalisé.";
        UpcomingText.Text = $"Échéances à venir dans la période · {value.Lines.Length:N0} ligne(s), voir Prévisions.";
    }

    private void PreviousBudget_Click(object sender, RoutedEventArgs e) => ShiftBudget(-1);
    private void NextBudget_Click(object sender, RoutedEventArgs e) => ShiftBudget(1);
    private void ShiftBudget(int step)
    {
        if (_operation is not null) return;
        try
        {
            var month = BudgetEditorWindow.ReadMonth(BudgetMonth.Text).AddMonths(step);
            BudgetMonth.Text = month.ToString("MM/yyyy", French);
            UpdateBudgetRows([]);
        }
        catch (ArgumentException error) { Status.Text = error.Message; }
    }

    private async void Budgets_Click(object sender, RoutedEventArgs e) => await Run("Calcul du réalisé budgétaire…", async token =>
    {
        var values = await CalculateBudgets(BudgetEditorWindow.ReadMonth(BudgetMonth.Text), Access, token);
        token.ThrowIfCancellationRequested();
        UpdateBudgetRows(values);
        Status.Text = "Budgets calculés d’après les dépenses enregistrées jusqu’à aujourd’hui.";
    });

    private Task<IReadOnlyList<BudgetUsage>> CalculateBudgets(DateOnly month, HistoricalRateAccess access, CancellationToken token,
        ReportContent? shared = null)
    {
        var snapshot = Saved;
        var path = _workspace.FilePath!;
        var today = Today;
        var provider = SelectedProvider;
        var plans = _model.Budgets.Where(item => item.Month == month && item.Accounts.All(id => snapshot.Accounts.Any(account => account.Id == id))).ToArray();
        return Task.Run<IReadOnlyList<BudgetUsage>>(async () =>
        {
            var values = new List<BudgetUsage>();
            foreach (var currencyGroup in plans.GroupBy(item => item.CurrencyCode))
            {
                var currency = snapshot.Currencies.Single(item => item.Code == currencyGroup.Key);
                var criterion = new ReportCriteria(month, currencyGroup.First().End, currencyGroup.SelectMany(plan => plan.Accounts).Distinct().Order().ToArray(),
                    true, null, currency, provider, today);
                ReportContent report;
                if (shared is not null && shared.Criteria.CategoryId is null && shared.Criteria.Provider == criterion.Provider &&
                    shared.Criteria.Target == currency && shared.Criteria.From <= month && shared.Criteria.Through >= criterion.Through &&
                    !criterion.Accounts.Except(shared.Criteria.Accounts).Any())
                    report = shared with { Flows = shared.Flows.Where(row => row.Input.Date >= month && row.Input.Date <= criterion.Through).ToArray() };
                else
                {
                    var source = month > today ? new ReportSource([], []) : MonetaFile.ReadReport(path, snapshot, criterion, balances: false, cancellationToken: token);
                    report = await _engine.GenerateAsync(criterion, source, access, token).ConfigureAwait(false);
                }
                foreach (var plan in currencyGroup)
                {
                    var accounts = plan.Accounts.ToHashSet();
                    var slice = report with { Criteria = criterion with { Accounts = plan.Accounts },
                        Flows = report.Flows.Where(row => accounts.Contains(row.Subject.AccountId)).ToArray() };
                    values.Add(slice.Budget(plan, today));
                }
            }
            return values.AsReadOnly();
        }, token);
    }

    private void UpdateBudgetRows(IReadOnlyList<BudgetUsage> values)
    {
        var month = BudgetEditorWindow.ReadMonth(BudgetMonth.Text);
        var usages = values.ToDictionary(item => item.Plan.Id);
        var selected = (BudgetGrid.SelectedItem as BudgetView)?.Plan.Id;
        var rows = _model.Budgets.Where(plan => plan.Month == month).OrderBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(plan => new BudgetView(plan, _model.CurrencyRegistry.Get(plan.CurrencyCode), usages.GetValueOrDefault(plan.Id))).ToArray();
        BudgetGrid.ItemsSource = rows;
        BudgetGrid.SelectedItem = rows.FirstOrDefault(row => row.Plan.Id == selected);
        BudgetSummary.Text = rows.Length == 0 ? "Aucun budget pour ce mois." :
            $"{month:MMMM yyyy} · {rows.Length} budget(s). Les montants d’enveloppes de devises différentes restent séparés.";
        BudgetAlert.Text = values.Count == 0 ? "Budgets du mois · voir l’onglet Budgets." :
            $"Budgets de {month:MMMM yyyy} · {values.Count(item => item.Overrun)} dépassement(s) confirmé(s), {values.Count(item => item.Missing > 0)} budget(s) incomplet(s).";
        UpdateActions();
    }

    private void EditBudget_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var original = ReferenceEquals(sender, NewBudget) ? null : (BudgetGrid.SelectedItem as BudgetView)?.Plan;
            var duplicate = ReferenceEquals(sender, DuplicateBudget);
            var month = original?.Month ?? BudgetEditorWindow.ReadMonth(BudgetMonth.Text);
            if (duplicate) month = month.AddMonths(1);
            var editor = new BudgetEditorWindow(_model.CaptureDocument(), month, original, duplicate) { Owner = this };
            if (editor.ShowDialog() != true || editor.Result is not { } value) return;
            _model.PutBudget(value);
            BudgetMonth.Text = value.Month.ToString("MM/yyyy", French);
            UpdateBudgetRows([]);
            Status.Text = "Budget en brouillon. Ctrl+S enregistre le dossier.";
        }
        catch (ArgumentException error) { Status.Text = error.Message; }
    }

    private void DeleteBudget_Click(object sender, RoutedEventArgs e)
    {
        if (BudgetGrid.SelectedItem is not BudgetView row || MessageBox.Show(this, $"Supprimer le budget « {row.Plan.Name} » ?",
            "Budget mensuel", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _model.RemoveBudget(row.Plan.Id);
        UpdateBudgetRows([]);
        Status.Text = "Suppression en brouillon. Ctrl+S enregistre le dossier.";
    }

    private async void Freeze_Click(object sender, RoutedEventArgs e) => await Run("Vérification de la preuve du rapport…", async token =>
    {
        if (_report is not { } view || _fromArchive) return;
        var label = string.IsNullOrWhiteSpace(ReportName.Text) ? $"Rapport du {view.Content.Criteria.From:dd/MM/yyyy} au {view.Content.Criteria.Through:dd/MM/yyyy}" : ReportName.Text;
        var version = await Task.Run(() => FrozenReport.Create(label, view.Content, _predecessor), token);
        token.ThrowIfCancellationRequested();
        _model.AddFrozenReport(version);
        _fromArchive = true;
        UpdateArchives(version.Id);
        ArchiveSummary.Text = view.Total.Complete && view.Content.Balances.All(row => row.Converted.HasValue)
            ? "Preuve figée en brouillon." : "Rapport incomplet figé avec les raisons d’absence, en brouillon.";
        Status.Text = "Ctrl+S enregistre le rapport et son empreinte dans le dossier.";
    });

    private async void Replay_Click(object sender, RoutedEventArgs e) => await Run("Rejeu du rapport figé…", async token =>
    {
        if (ArchiveGrid.SelectedItem is not ArchiveView archive) return;
        var stored = await LoadArchive(archive.Value, token);
        var view = await Task.Run(() => RenderedReport.Create(stored), token);
        token.ThrowIfCancellationRequested();
        ShowReport(view, archive.Value.Id, true);
        Views.SelectedIndex = 0;
        Status.Text = "Rapport reproduit hors ligne depuis sa preuve.";
    });

    private async void Recalculate_Click(object sender, RoutedEventArgs e) => await Run("Recalcul depuis le dossier enregistré…", async token =>
    {
        if (ArchiveGrid.SelectedItem is not ArchiveView archive) return;
        var stored = await LoadArchive(archive.Value, token);
        var access = Network.IsChecked == true ? HistoricalRateAccess.Refresh : HistoricalRateAccess.CacheOnly;
        ReportName.Text = archive.Value.Name;
        await GenerateReport(stored.Criteria, access, archive.Value.Id, token);
        Views.SelectedIndex = 7;
    });

    private Task<ReportContent> LoadArchive(FrozenReport entry, CancellationToken token)
    {
        var snapshot = Saved;
        var path = _workspace.FilePath!;
        return Task.Run(() => entry.DraftPayload is { } draft ? entry.Reproduce(draft) : MonetaFile.ReadFrozenReport(path, snapshot.Id, entry.Id, token), token);
    }

    private void UpdateArchives(Guid? select = null)
    {
        var labels = _model.FrozenReports.ToDictionary(item => item.Id, item => item.Name);
        var rows = _model.FrozenReports.OrderByDescending(item => item.CapturedUtc).ThenBy(item => item.Id).Select(item =>
            new ArchiveView(item, item.PredecessorId is { } parent ? labels[parent] : "—")).ToArray();
        ArchiveGrid.ItemsSource = rows;
        if (select is not null) ArchiveGrid.SelectedItem = rows.FirstOrDefault(row => row.Value.Id == select);
        if (rows.Length == 0) ArchiveSummary.Text = "Aucun rapport figé.";
        UpdateActions();
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await Run("Enregistrement du dossier…", async token =>
    {
        if (await _save())
        {
            UpdateArchives((ArchiveGrid.SelectedItem as ArchiveView)?.Value.Id);
            Status.Text = "Dossier enregistré.";
        }
        else Status.Text = "Enregistrement non confirmé.";
    }, cancellable: false);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control || !SaveDraft.IsEnabled) return;
        e.Handled = true;
        Save_Click(SaveDraft, new RoutedEventArgs());
    }
    private void BudgetSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void ArchiveSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void CancelRun_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async Task Run(string message, Func<CancellationToken, Task> work, bool cancellable = true)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        UpdateActions();
        CancelRun.IsEnabled = cancellable;
        Status.Text = message;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "Calcul annulé."; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or DocumentException or RateException or OverflowException or JsonException)
        { if (!_closed) Status.Text = error.Message; }
        finally
        {
            _operation = null;
            if (!_closed) { CancelRun.IsEnabled = false; UpdateActions(); }
        }
    }

    private void UpdateActions()
    {
        if (Freeze is null || BudgetGrid is null || ArchiveGrid is null || SaveDraft is null) return;
        var ready = _operation is null;
        Generate.IsEnabled = Project.IsEnabled = BudgetCalculate.IsEnabled = NewBudget.IsEnabled = ready;
        EditBudget.IsEnabled = DuplicateBudget.IsEnabled = DeleteBudget.IsEnabled = ready && BudgetGrid.SelectedItem is not null;
        Replay.IsEnabled = Recalculate.IsEnabled = ready && ArchiveGrid.SelectedItem is not null;
        Freeze.IsEnabled = ready && _report is not null && !_fromArchive;
        SaveDraft.IsEnabled = ready && _workspace.HasUnsavedChanges;
    }

    private static string Money(decimal amount, Currency currency) => $"{amount.ToString($"N{currency.DecimalPlaces}", French)} {currency.Code}";

    private sealed record LineView(ReportContent Content, ReportLine Line)
    {
        private HistoricalQuotation? Quote => Content.QuoteFor(Line);
        public DateOnly Date => Line.Input.Date;
        public string Account => Line.Subject.Account;
        public string Category => Line.Subject.Subcategory is { } sub ? $"{Line.Subject.Category} › {sub}" : Line.Subject.Category ?? "Sans catégorie";
        public string Kind => Line.Input.FlowKind switch { HistoricalFlowKind.Income => "Recette", HistoricalFlowKind.Fee => "Frais", _ => "Dépense" };
        public decimal NativeAmount => Line.Input.NativeAmount;
        public decimal? Value => Line.Converted;
        public string Native => $"{NativeAmount.ToString("G29", French)} {Line.Input.SourceCode}";
        public string Display => Value is { } value ? Money(value, Content.Criteria.Target) : "Indisponible";
        public string Status => Quote is { } quote ? $"{RateRules.ProviderName(quote.Provider)} · {quote.EffectiveDate:dd/MM/yyyy}"
            : Value.HasValue ? "Même devise" : $"{RateRules.ProviderName(Content.Criteria.Provider)} · {Line.Detail}";
        public string Evidence => Quote is { } quote
            ? $"{Status} · demande {Date:dd/MM/yyyy} · " + string.Join(" · ", new[] { quote.Source, quote.Target }.OfType<OfficialRateQuote>()
                .Select(item => $"{item.Nominal} {item.CurrencyCode} = {item.OfficialValue.ToString(French)} {quote.ReferenceCurrency}")) +
                $" · {Line.Input.SourceCode}{(quote.Source is not null && quote.Target is not null ? " → " + quote.ReferenceCurrency : "")} → {Content.Criteria.Target.Code} · arrondi au pair"
            : Status;
    }

    private sealed record GroupView(ReportGroup Row, Currency Target)
    {
        public string Label => Row.Label;
        public ReportTotal Total => Row.Total;
        public string Income => Money(Total.Income, Target);
        public string Expense => Money(Total.Expense, Target);
        public string Net => Total.Complete ? Money(Total.Net, Target) : "Indisponible";
        public string Coverage => $"{Total.Valued:N0}/{Total.Eligible:N0}";
    }

    private sealed record ForecastView(ForecastRow Row, Currency Target)
    {
        public DateOnly Date => Row.Date;
        public string Label => Row.Label;
        public string Account => Row.Account;
        public decimal NativeAmount => Row.Native;
        public string Native => Row.Status == ForecastValuation.Transfer
            ? $"{Row.Native.ToString("G29", French)} {Row.Currency} → {Row.TransferCredit?.ToString("G29", French) ?? "à confirmer"} {Row.TransferCurrency}"
            : $"{(Row.IsIncome ? "" : "−")}{Row.Native.ToString("G29", French)} {Row.Currency}";
        public string Value => Row.Value is { } amount ? Money(Row.IsIncome ? amount : -amount, Target) : "—";
        public string Status => Row.Status switch
        {
            ForecastValuation.Native => "Prévu · même devise",
            ForecastValuation.Estimated => $"Estimation · {RateRules.ProviderName(Row.Provider!.Value)} · effet {Row.Effective:dd/MM/yyyy}",
            _ => Row.Detail ?? "Conversion indisponible"
        };
    }

    private sealed record ForecastMonthView(ForecastMonth Row)
    {
        public DateOnly Month => Row.Month;
        public string Income => Money(Row.Income, Row.Currency);
        public string Expense => Money(Row.Expense, Row.Currency);
        public string Coverage => Row.IncludesEstimates
            ? Row.Missing == 0 ? "Estimation consolidée" : $"Estimation partielle · {Row.Missing} sans conversion"
            : "Montants dans la devise native";
    }

    private sealed record ForecastDisplay(Currency Target, ForecastView[] Lines, ForecastMonthView[] Months);

    private sealed record BudgetView(BudgetPlan Plan, Currency Currency, BudgetUsage? Usage)
    {
        public string Planned => Money(ExactMoney.FromMinorUnits(Plan.PlannedMinor, Currency), Currency);
        public string Known => Usage is { HasPastDates: true } value ? Money(value.Known, Currency) : "—";
        public string Difference => Usage?.Difference is { } value ? Money(value, Currency) : "—";
        public string Percent => Usage?.Percent is { } value ? $"{(Usage.Missing > 0 ? "≥ " : "")}{value:N1} %" : "—";
        public double Progress => (double)Math.Clamp(Usage?.Percent ?? 0m, 0m, 100m);
        public Visibility ProgressVisibility => Usage?.Percent is null ? Visibility.Collapsed : Visibility.Visible;
        public string Status => Usage switch
        {
            null => "Réalisé à calculer",
            { HasPastDates: false } => "Mois futur",
            { Missing: > 0, Overrun: true } => $"Dépassé · {Usage.Missing} sans valeur",
            { Missing: > 0 } => $"Incomplet · {Usage.Missing} sans valeur",
            { Overrun: true } => "Dépassé",
            _ => "Dans le budget"
        };
    }

    private sealed record ArchiveView(FrozenReport Value, string Parent)
    {
        public DateTime Captured => Value.CapturedUtc.LocalDateTime;
        public string Status => Value.DraftPayload is null ? "Enregistré" : "Brouillon";
    }

    private sealed record RenderedReport(ReportContent Content, ReportTotal Total, decimal? Consolidated,
        Dictionary<ReportGrouping, IReadOnlyList<ReportGroup>> Groups, IReadOnlyList<ReportGroup> Donut,
        LineView[] Lines, LineView[] Missing)
    {
        public static RenderedReport Create(ReportContent content)
        {
            var total = content.Totals();
            var groups = Enum.GetValues<ReportGrouping>().ToDictionary(mode => mode, content.Group);
            var donut = content.ExpenseDistribution();
            var lines = content.Flows.Select(row => new LineView(content, row)).ToArray();
            return new(content, total, content.ConsolidatedBalance(), groups, donut, lines, lines.Where(row => !row.Value.HasValue).ToArray());
        }
    }
}
