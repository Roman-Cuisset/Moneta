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
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
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
            throw new InvalidOperationException("Enregistrez d’abord le dossier et sélectionnez un compte actif pour ouvrir le rapprochement.");
        _model = model;
        _source = model.ReconciliationSource
            ?? throw new InvalidOperationException("Ce dossier ne permet pas la lecture des rapprochements.");
        _account = model.SelectedAccount?.Account
            ?? throw new InvalidOperationException("Sélectionnez un compte actif.");
        InitializeComponent();
        AccountHeading.Text = $"Rapprochement bancaire · {model.SelectedAccount!.DisplayName}";
        StatementDate.Text = DateOnly.FromDateTime(DateTime.Today).ToString("dd/MM/yyyy", French);
        _ready = true;
        if (historyOnly)
        {
            StatementTab.Visibility = Visibility.Collapsed;
            HistoryTab.Header = "Historique";
            ReconciliationTabs.SelectedItem = HistoryTab;
            ReconciliationNotice.Text = "Compte archivé. Réactivez-le et enregistrez pour reprendre les rapprochements.";
            LoadHistory(0);
            return;
        }
        LoadCandidates(0, _selectedIds);
        LoadHistory(0);
        Loaded += (_, _) => { StatementDate.Focus(); StatementDate.SelectAll(); };
    }

    private string Money(decimal value) => $"{value.ToString($"N{_account.Currency.DecimalPlaces}", French)} {_account.Currency.Code}";

    private void EnsureAccount()
    {
        if (_model.SelectedAccountId != _account.Id)
            throw new InvalidOperationException("Le compte sélectionné a changé. Fermez cette fenêtre puis recommencez sur le compte voulu.");
    }

    private DateOnly ReadDate()
    {
        if (!DateOnly.TryParseExact(StatementDate.Text.Trim(), "dd/MM/yyyy", French, DateTimeStyles.None, out var date)
            || date == default || date > DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("La date du relevé doit être valide, au format jj/mm/aaaa, et au plus tard aujourd’hui.");
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
                : $"Validation bloquée : {page.LegacyReconciledCount.ToString("N0", French)} opération(s) R héritée(s), sans relevé attesté. Fermez cette fenêtre et libérez explicitement chaque rapprochement hérité depuis le registre, avec un motif, puis enregistrez (Ctrl+S). Aucun relevé ni date ne sera inventé.";
            CandidatePageText.Text = Range(page.Offset, page.Transactions.Count, page.TotalCount, "opérations");
            ErrorText.Text = string.Empty;
        }
        catch (Exception error)
        {
            _candidatesValid = false;
            ErrorText.Text = "Lecture impossible. Vos champs et votre sélection sont conservés. Si la date exclut une opération sélectionnée, rétablissez-la ou utilisez « Vider la sélection ».\n" + error.Message;
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
                throw new InvalidOperationException("Le dernier relevé actif est introuvable dans l’historique. Actualisez le dossier avant de recommencer.");
            offset += PageSize;
        }
    }

    private static string Range(int offset, int count, long total, string noun) => total == 0
        ? $"0 {noun}"
        : $"{(offset + 1L).ToString("N0", French)}–{(offset + (long)count).ToString("N0", French)} / {total.ToString("N0", French)} {noun}";

    private ReconciliationRequest ReadRequest()
    {
        EnsureAccount();
        var date = ReadDate();
        if (!_candidatesValid || _candidatePage is null || _loadedDate != date)
            throw new InvalidOperationException("Actualisez les opérations pour cette date avant de valider.");
        if (!_model.CanReconcile)
            throw new InvalidOperationException("Le dossier doit être enregistré et sans autre brouillon avant le rapprochement.");
        if (_candidatePage.LegacyReconciledCount != 0)
            throw new InvalidOperationException("Libérez d’abord les rapprochements hérités depuis le registre, avec un motif, puis enregistrez.");
        if (_previousStatementDate is { } previous && date <= previous)
            throw new ArgumentException($"La date doit être postérieure au dernier relevé actif ({previous.ToString("dd/MM/yyyy", French)}).");
        var number = StatementNumber.Text.Trim();
        if (number.Length > 120)
            throw new ArgumentException("Le numéro de relevé ne doit pas dépasser 120 caractères.");
        var expected = MoneyInput.ParseFrench(StatementClosingBalance.Text, _account.Currency, allowNegative: true);
        var calculated = checked(_candidatePage.OpeningBalance + _candidatePage.SelectedNet);
        if (expected != calculated)
            throw new ArgumentException("L’écart doit être exactement nul pour valider le relevé.");
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
        ReconciliationSelectedCount.Text = $"Sélection : {_selectedIds.Count.ToString("N0", French)} opération(s)";
        ReconciliationSelectedNet.Text = _candidatesValid && _candidatePage is { } current ? "Net sélectionné : " + Money(current.SelectedNet) : "Net sélectionné : non actualisé";
        ReconciliationGap.Text = "Écart : non calculé";
        if (!_candidatesValid || _candidatePage is null)
            return;
        try
        {
            var expected = MoneyInput.ParseFrench(StatementClosingBalance.Text, _account.Currency, allowNegative: true);
            var calculated = checked(_candidatePage.OpeningBalance + _candidatePage.SelectedNet);
            ReconciliationGap.Text = "Écart : " + Money(checked(expected - calculated));
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
        ReconciliationGap.ToolTip = "Solde final du relevé − (ouverture + net sélectionné).";
    }

    private void StatementDate_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _submitting)
            return;
        _candidatesValid = false;
        // Wait for a complete date, but invalidate the old preview immediately.
        if (DateOnly.TryParseExact(StatementDate.Text.Trim(), "dd/MM/yyyy", French, DateTimeStyles.None, out var date)
            && date != default && date <= DateOnly.FromDateTime(DateTime.Today))
            LoadCandidates(0, _selectedIds);
        else
        {
            ErrorText.Text = "Saisissez une date de relevé valide, au format jj/mm/aaaa, au plus tard aujourd’hui.";
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
            ErrorText.Text = "Le relevé n’a pas été validé. Vos champs et votre sélection sont conservés.\n" + error.Message;
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
            HistoryPageText.Text = Range(page.Offset, page.Sessions.Count, page.TotalCount, "relevés");
            HistoryPrevious.IsEnabled = page.Offset > 0;
            HistoryNext.IsEnabled = page.Offset <= int.MaxValue - PageSize && page.Offset + (long)PageSize < page.TotalCount;
            if (rows.Length > 0) ReconciliationHistory.SelectedItem = rows[0];
        }
        catch (Exception error)
        {
            ErrorText.Text = "L’historique n’a pas pu être lu. Vos champs sont conservés.\n" + error.Message;
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
                    throw new InvalidOperationException("Ce relevé appartient à un autre compte.");
                _details = details;
                ReconciliationEntries.ItemsSource = details.Entries.Select(entry => new CandidateRow(entry.Original, false, _account.Currency)).ToArray();
                ReconciliationEvents.ItemsSource = details.Events.Select(value => new EventRow(value)).ToArray();
                if (ReconciliationEntries.Items.Count > 0) ReconciliationEntries.SelectedIndex = 0;
            }
            catch (Exception error)
            {
                ErrorText.Text = "Les détails du relevé n’ont pas pu être lus.\n" + error.Message;
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
            var reasonWindow = new ReopenReasonWindow("Rouvrir le relevé bancaire",
                "Le relevé et ses instantanés resteront dans l’historique. Ses opérations retrouveront leur pointage d’origine, sans modification financière. Le motif sera conservé. Cette action prépare un brouillon : Ctrl+S pour enregistrer.") { Owner = this };
            reasonWindow.ReopenReason.Text = _reopenReason;
            if (reasonWindow.ShowDialog() != true)
                return;
            _reopenReason = reasonWindow.Reason;
            if (MessageBox.Show(this,
                $"Rouvrir le relevé du {session.StatementDate.ToString("dd/MM/yyyy", French)} ({session.StatementNumber}) ?\n\nMotif : {reasonWindow.Reason}\n\nL’historique est conservé. La réouverture sera au brouillon jusqu’à Ctrl+S.",
                "Confirmer la réouverture", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            _model.ReopenReconciliation(session.Id, session.Revision, reasonWindow.Reason);
            staged = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = "Le relevé n’a pas été rouvert. Vos champs sont conservés.\n" + error.Message;
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
        EntrySnapshotText.Text = $"Opération {value.Id} · compte {value.AccountId} · révision {value.Revision}\n"
            + $"{row.DateText} · {value.Label} · {value.Direction} · magnitude {value.Amount.ToString(CultureInfo.InvariantCulture)} {value.Currency.Code} ({value.Currency.DecimalPlaces} décimales) · {row.StatusText} · pointé le {row.MarkedOnText}\n"
            + $"Tiers : {value.Payee.Name} ({value.PayeeId}) · catégorie : {value.Category.Name} / {value.Category.Subcategory} ({value.CategoryId} / {value.SubcategoryId})\n"
            + $"Paiement : {value.PaymentMode} ({value.PaymentMethodId}) · rapprochement d’origine : {value.ReconciliationId} · supprimée : {value.IsDeleted}\n"
            + $"Créée : {value.CreatedUtc:O} · modifiée : {value.ModifiedUtc:O}\n"
            + (value.Valuation is { } valuation ? $"Valorisation : {valuation.Amount.ToString(CultureInfo.InvariantCulture)} {valuation.Currency.Code}, date {valuation.RateDate:yyyy-MM-dd}, source {valuation.Source}\n" : string.Empty)
            + $"Notes : {value.Details}";
    }

    public sealed class CandidateRow
    {
        public Transaction Original { get; }
        public bool Selected { get; }
        public string DateText => Original.Date.ToString("dd/MM/yyyy", French);
        public string MarkedOnText => Original.MarkedOn?.ToString("dd/MM/yyyy", French) ?? "—";
        public string StatusText => Original.Status switch { ReconciliationStatus.Unmarked => "NP", ReconciliationStatus.Marked => "P", _ => "R" };
        public string AmountText { get; }
        public CandidateRow(Transaction original, bool selected, Currency currency)
        {
            Original = original;
            Selected = selected;
            AmountText = $"{FinancialRules.SignedAmount(original).ToString($"N{currency.DecimalPlaces}", French)} {currency.Code}";
        }
    }

    public sealed class HistoryRow
    {
        public ReconciliationSession Session { get; }
        public string DateText => Session.StatementDate.ToString("dd/MM/yyyy", French);
        public string StateText => Session.State == ReconciliationSessionState.Validated ? "Validé" : "Rouvert";
        public string OpeningText { get; }
        public string ClosingText { get; }
        public string ValidatedText => Session.ValidatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", French);
        public string ReopenedText => Session.ReopenedUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", French) ?? "—";
        public HistoryRow(ReconciliationSession session, Currency currency)
        {
            Session = session;
            OpeningText = $"{session.OpeningBalance.ToString($"N{currency.DecimalPlaces}", French)} {currency.Code}";
            ClosingText = $"{session.ExpectedClosingBalance.ToString($"N{currency.DecimalPlaces}", French)} {currency.Code}";
        }
    }

    public sealed class EventRow
    {
        public ReconciliationEvent Event { get; }
        public string OccurredText => Event.OccurredUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", French);
        public string KindText => Event.Kind switch { ReconciliationEventKind.Validated => "Validation", ReconciliationEventKind.Reopened => "Réouverture", _ => "Libération héritée" };
        public EventRow(ReconciliationEvent value) => Event = value;
    }
}
