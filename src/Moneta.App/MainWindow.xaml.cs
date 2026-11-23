using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class MainWindow : Window
{
    private readonly DocumentWorkspace _workspace = new();
    private bool _busy;
    private bool _allowClose;
    private bool _closingPrompt;
    private string? _operation;

    public MainWindow()
        : this(new BackupLibrary(), () => App.Settings.Current.Backups)
    {
    }

    internal MainWindow(BackupLibrary backupLibrary, Func<BackupPolicy> backupPolicy)
    {
        _backupLibrary = backupLibrary;
        _readBackupPolicy = backupPolicy;
        InitializeComponent();
        InitializeBackups();
        _workspace.Changed += (_, _) =>
        {
            if (Dispatcher.CheckAccess()) UpdateDocumentState();
            else Dispatcher.BeginInvoke(UpdateDocumentState);
        };
        App.Rates.Changed += Rates_Changed;
        Closed += (_, _) => App.Rates.Changed -= Rates_Changed;
        PropertyChangedEventManager.AddHandler(L.Current, LanguageChanged, nameof(L.CultureName));
        UpdateDocumentState();
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        ApplyRateState();
        _workspace.Model?.RefreshDisplayLanguage();
        RefreshDocumentLabels();
        RefreshBackupUi();
        RefreshScheduleReminder();
    }

    private void UpdateDocumentState()
    {
        UpdateRegisterSearch();
        if (!ReferenceEquals(DataContext, _workspace.Model)) DataContext = _workspace.Model;
        RefreshDocumentLabels();
        ApplyRateState();
        IsEnabled = !_busy && !_stopBackupRequests;
        UpdateBackupContext();
        RefreshScheduleReminder();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshDocumentLabels()
    {
        var model = _workspace.Model;
        var caption = model is null ? L.Get("MainWindow.NoDocumentOpen") : model.IsDemo ? L.Get("MainWindow.DemonstrationInMemory") : model.DocumentName;
        var state = _workspace.HasUnsavedChanges ? L.Get("MainWindow.NotRegistered") : _workspace.FilePath is not null ? L.Get("MainWindow.Registered") : "";
        Title = $"Moneta — {caption}{(_workspace.HasUnsavedChanges ? " *" : "")}";
        DocumentCaption.Text = _operation ?? caption + state;
        DocumentCaption.ToolTip = _workspace.FilePath ?? caption + state;
        AccountsSelector.ToolTip = L.Get("MainWindow.ChooseAnAccountOfTheDocumentDisplayed");
        DevelopmentNotice.Text = model is null ? L.Get("MainWindow.DevelopmentVersionOpenOrCreateADocument")
            : model.IsDemo ? L.Get("MainWindow.DevelopmentVersionFictitiousData")
            : !model.PendingReconciliations.IsEmpty ? L.Get("MainWindow.DraftApproachCtrlSBeforeChangingTransactions")
            : _workspace.HasUnsavedChanges ? L.Get("MainWindow.DraftCtrlSForLongTermRecording")
            : L.Get("MainWindow.DevelopmentLocalDocumentRegistered");
        RegisterDateColumn.Header = model?.DateColumnHeader ?? L.Get("Common.Date");
    }

    private void Available_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = !_busy; e.Handled = true; }
    private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = !_busy && _workspace.CanSave; e.Handled = true; }
    private void AddAccount_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = !_busy && _workspace.Model is not null; e.Handled = true; }
    private void Backup_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = !_busy && _workspace.CanSave; e.Handled = true; }

    private bool CanEditLedger => !_busy && _workspace.Model is
        { IsDemo: false, SelectedAccount.Account.IsArchived: false, PendingReconciliations.IsEmpty: true };
    private void NewTransaction_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger;
        e.Handled = true;
    }
    private void SelectedTransaction_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger && SingleSelectedRow is { Transaction.Status: not ReconciliationStatus.Reconciled };
        e.Handled = true;
    }

    private void AccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || _workspace.Model is not { } model ||
            sender is not System.Windows.Controls.Primitives.Selector selector || selector.SelectedItem is not AccountSummary selected ||
            ReferenceEquals(selected, model.SelectedAccount)) return;
        try { model.SelectedAccount = selected; }
        catch (Exception error) when (error is DocumentException or ArgumentException or InvalidOperationException or OverflowException)
        {
            // A failed binding setter must never leave one account's caption over another account's rows.
            selector.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.UpdateTarget();
            MessageBox.Show(this, error.Message, L.Get("MainWindow.MonetaAccountChangeInterrupted"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task<bool> RunIo(Action action, string operation)
    {
        _busy = true;
        _operation = operation;
        UpdateDocumentState();
        Cursor = Cursors.Wait;
        try
        {
            await Task.Run(action);
            return true;
        }
        catch (Exception error) when (error is DocumentException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, error is DocumentException ? error.Message : L.Get("MainWindow.TheTransactionDidNotSucceedMemoryChangesAre"), L.Get("MainWindow.MonetaTransactionInterrupted"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _busy = false;
            _operation = null;
            Cursor = null;
            UpdateDocumentState();
        }
    }

    private static SaveFileDialog Destination(string title, string fileName = "Mes comptes.moneta") => new()
    {
        Title = title + L.Get("MainWindow.ChooseANewFile"),
        Filter = L.Get("Common.DocumentMonetaMonetaMoneta"),
        DefaultExt = ".moneta", AddExtension = true, OverwritePrompt = false,
        CheckPathExists = true, FileName = fileName
    };

    private async Task<bool> SaveAsync(bool saveAs = false, Window? dialogOwner = null)
    {
        if (!_workspace.CanSave) return false;
        string? path = null;
        if (saveAs || _workspace.FilePath is null)
        {
            var dialog = Destination(saveAs ? L.Get("Common.SaveAs") : L.Get("MainWindow.SaveDocument"));
            if (dialog.ShowDialog(dialogOwner ?? this) != true) return false;
            path = dialog.FileName;
        }
        var saved = await RunIo(() => _workspace.Save(path), "Enregistrement…");
        if (saved) _ = RequestBackup(BackupKind.AfterSave);
        return saved;
    }

    private async Task<bool> ConfirmReplacement()
    {
        if (!_workspace.HasUnsavedChanges) return true;
        var answer = MessageBox.Show(this, L.Get("MainWindow.ThisDocumentContainsUnrecordedChangesDoYouWant"), L.Get("MainWindow.MonetaPendingChanges"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveAsync());
    }

    private async void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new NewDocumentWindow { Owner = this };
        if (dialog.ShowDialog() != true || !await ConfirmReplacement()) return;
        var name = dialog.FolderName;
        await RunIo(() => _workspace.New(name), L.Get("MainWindow.NewDocument"));
    }

    private async void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L.Get("MainWindow.OpenAFinancialDocument"), Filter = L.Get("MainWindow.MonetaDocumentsMonetaMonetaBankperfectFilesBpBp"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetExtension(dialog.FileName), ".bp", StringComparison.OrdinalIgnoreCase))
        {
            await ShowBankPerfectImportAsync(dialog.FileName);
            return;
        }
        if (string.Equals(dialog.FileName, _workspace.FilePath, StringComparison.OrdinalIgnoreCase) || !await ConfirmReplacement()) return;
        // Inspect the candidate without closing or merging the current folder.
        var migration = false;
        await RunIo(() =>
        {
            try { _workspace.Open(dialog.FileName); }
            catch (DocumentException error) when (error.Error == DocumentError.MigrationRequired) { migration = true; }
        }, L.Get("MainWindow.CheckingAndOpening"));
        if (!migration) return;
        if (MessageBox.Show(this, L.Get("MainWindow.MonetaCanOpenAMigratedAndVerifiedCopy"), L.Get("MainWindow.MonetaMigrationByCopy"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var target = Destination(L.Get("MainWindow.MigratedCopy"), L.Get("MainWindow.FileMigratedMoneta"));
        if (target.ShowDialog(this) == true)
            await RunIo(() => _workspace.OpenMigratedCopy(dialog.FileName, target.FileName), L.Get("MainWindow.MigrationOnANewCopy"));
    }

    private async void ImportBankPerfect_Executed(object sender, ExecutedRoutedEventArgs e)
        => await ShowBankPerfectImportAsync();

    private async Task ShowBankPerfectImportAsync(string? source = null)
    {
        var dialog = new BankPerfectImportWindow(source) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result && await ConfirmReplacement())
            await RunIo(() => _workspace.Open(result.Destination), L.Get("MainWindow.OpeningOfTheImportedFile"));
    }

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync();
    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(true);
    private async void Demo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (await ConfirmReplacement()) await RunIo(_workspace.OpenDemo, L.Get("MainWindow.OpeningOfFictitiousData"));
    }
    private async void Backup_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_workspace.HasUnsavedChanges && !await SaveAsync()) return;
        var dialog = Destination(L.Get("Common.SaveDocument"), L.Get("MainWindow.BackupMoneta"));
        if (dialog.ShowDialog(this) != true) return;
        await _backupWork;
        if (await RunIo(() => _workspace.Backup(dialog.FileName), L.Get("MainWindow.SqliteBackupAndVerification")))
            MessageBox.Show(this, L.Get("MainWindow.CheckedConsistentBackupKeepTogetherTheMonetaFile"), L.Get("MainWindow.MonetaBackup"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void Restore_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var source = new OpenFileDialog { Title = L.Get("MainWindow.ChooseAMonetaBackupWithItsManifest"), Filter = L.Get("MainWindow.MonetaBackupMonetaMoneta"), CheckFileExists = true };
        if (source.ShowDialog(this) != true) return;
        var target = Destination(L.Get("MainWindow.RestoreToANewDocument"), L.Get("Common.DocumentRestoredMoneta"));
        if (target.ShowDialog(this) != true) return;
        if (await RunIo(() => DocumentWorkspace.Restore(source.FileName, target.FileName), L.Get("MainWindow.RestorationReopeningAndComparison")))
            MessageBox.Show(this, L.Get("MainWindow.TheNewCopyHasBeenReopenedAndVerified"), L.Get("MainWindow.MonetaVerifiedRestoration"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_busy || _closingPrompt) return;
        _closingPrompt = true;
        var update = _pendingUpdate;
        var authorized = false;
        try
        {
            if (!await ConfirmReplacement()) return;
            _stopBackupRequests = true;
            _backupTimer.Stop();
            // Once the close decision is made, no new draft can be entered while waiting.
            IsEnabled = false;
            RefreshBackupUi();
            await _backupWork;
            ReportBackupFailuresOnExit();
            if ((update is null || StartUpdateHost(update)) && await RunIo(_workspace.Dispose, L.Get("MainWindow.ClosingTheDocument")))
            {
                authorized = update is not null && AuthorizeUpdate(update);
                _allowClose = true;
                Close();
            }
            else
            {
                _stopBackupRequests = false;
                _backupTimer.Start();
                UpdateDocumentState();
            }
        }
        finally
        {
            _pendingUpdate = null;
            if (!authorized) update?.Clean();
            _closingPrompt = false;
        }
    }

    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void About_Executed(object sender, ExecutedRoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var window = new SettingsWindow(DescribeBackup, _workspace.SavedSnapshot is null ? null : ShowBackupHistory) { Owner = this };
        if (window.ShowDialog() == true) UpdateBackupContext();
    }
    private void AddAccount_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) new AccountWindow(model) { Owner = this }.ShowDialog();
    }
    private void NewTransaction_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CanEditLedger && _workspace.Model is { } model)
            new TransactionWindow(model) { Owner = this }.ShowDialog();
        CommandManager.InvalidateRequerySuggested();
    }
    private void EditTransaction_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CanEditLedger && _workspace.Model is { } model &&
            SingleSelectedRow is { Transaction.Status: not ReconciliationStatus.Reconciled } row)
        {
            if (row.TransferId is { } id) OpenTransfer(id);
            else new TransactionWindow(model, row.Transaction) { Owner = this }.ShowDialog();
        }
        CommandManager.InvalidateRequerySuggested();
    }
    private void DeleteTransaction_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!CanEditLedger || _workspace.Model is not { } model ||
            SingleSelectedRow is not { Transaction.Status: not ReconciliationStatus.Reconciled } row) return;
        if (row.TransferId is { } transferId) { DeleteWholeTransfer(transferId); return; }
        var value = row.Transaction;
        if (MessageBox.Show(this,
            L.Format("MainWindow.Remove0From12TheDeletionWill", (value.Label), (row.DateText), ((value.Status == ReconciliationStatus.Marked ? "Cette opération est pointée : le solde pointé sera recalculé.\n\n" : ""))),
            L.Get("MainWindow.MonetaConfirmDeletion"), MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { model.DeleteTransaction(value.Id, value.Revision); }
        catch (Exception error) when (error is DocumentException or ArgumentException or InvalidOperationException or OverflowException)
        {
            MessageBox.Show(this, error.Message, L.Get("MainWindow.MonetaDiscontinuedDeletion"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        CommandManager.InvalidateRequerySuggested();
    }
    private void Transaction_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        while (origin is not null && origin is not DataGridRow)
        {
            if (origin is Button) return;
            origin = origin is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(origin) : LogicalTreeHelper.GetParent(origin);
        }
        if (e.OriginalSource is DependencyObject element &&
            ItemsControl.ContainerFromElement(TransactionsGrid, element) is DataGridRow &&
            WindowCommands.EditTransaction.CanExecute(null, TransactionsGrid))
        {
            WindowCommands.EditTransaction.Execute(null, TransactionsGrid);
            e.Handled = true;
        }
    }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ChangePage(next: false);
    private void NextPage_Click(object sender, RoutedEventArgs e) => ChangePage(next: true);
    private void ChangePage(bool next)
    {
        if (_busy || _workspace.Model is not { } model) return;
        try
        {
            if (next) model.NextPage(); else model.PreviousPage();
            if (TransactionsGrid.Items.Count > 0) TransactionsGrid.ScrollIntoView(TransactionsGrid.Items[0]);
        }
        catch (Exception error) when (error is DocumentException or InvalidOperationException or OverflowException)
        {
            MessageBox.Show(this, error.Message, L.Get("MainWindow.MonetaRegisterUnavailable"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Theme_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string mode) App.ChangeTheme(mode);
        UpdateThemeChecks();
    }
    private void ThemeMenu_Opened(object sender, RoutedEventArgs e) => UpdateThemeChecks();
    private void UpdateThemeChecks()
    {
        LightThemeItem.IsChecked = App.ThemeController.CurrentMode == "Light";
        DarkThemeItem.IsChecked = App.ThemeController.CurrentMode == "Dark";
    }
    private void Unavailable_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = false; e.Handled = true; }
    private void TransactionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_busy && e.Source == TransactionsGrid && _workspace.Model is { } model)
        {
            if (TransactionsGrid.SelectedItems.Count == 1 &&
                TransactionsGrid.CurrentItem is LedgerRow current && model.Rows.Contains(current) &&
                TransactionsGrid.SelectedItems.Contains(current))
                model.SelectedRow = current;
            else if (TransactionsGrid.SelectedItem is LedgerRow row && model.Rows.Contains(row))
                model.SelectedRow = row;
            else if (model.SelectedRow is { } removed && e.RemovedItems.Contains(removed))
                model.SelectedRow = null;
            // ItemsSource replacement removes old row objects. It must not clear the newly anchored selection.
        }
        DetailsText?.ScrollToHome();
        CommandManager.InvalidateRequerySuggested();
    }
}
