using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MainWindow : Window
{
    private readonly DocumentWorkspace _workspace = new();
    private bool _busy;
    private bool _allowClose;
    private bool _closingPrompt;
    private string? _operation;

    public MainWindow()
    {
        InitializeComponent();
        _workspace.Changed += (_, _) =>
        {
            if (Dispatcher.CheckAccess()) UpdateDocumentState();
            else Dispatcher.BeginInvoke(UpdateDocumentState);
        };
        App.Rates.Changed += Rates_Changed;
        Closed += (_, _) => App.Rates.Changed -= Rates_Changed;
        UpdateDocumentState();
    }

    private void UpdateDocumentState()
    {
        if (!ReferenceEquals(DataContext, _workspace.Model)) DataContext = _workspace.Model;
        var model = _workspace.Model;
        var caption = model is null ? "Aucun dossier ouvert" : model.IsDemo ? "Démonstration · en mémoire" : model.DocumentName;
        var state = _workspace.HasUnsavedChanges ? " · non enregistré" : _workspace.FilePath is not null ? " · enregistré" : "";
        Title = $"Moneta — {caption}{(_workspace.HasUnsavedChanges ? " *" : "")}";
        DocumentCaption.Text = _operation ?? caption + state;
        DocumentCaption.ToolTip = _workspace.FilePath ?? caption + state;
        AccountsSelector.ToolTip = "Choisir un compte du dossier affiché.";
        DevelopmentNotice.Text = model is null ? "Version de développement — ouvrez ou créez un dossier"
            : model.IsDemo ? "Version de développement — données fictives"
            : !model.PendingReconciliations.IsEmpty ? "Rapprochement en brouillon — Ctrl+S avant de modifier les opérations"
            : _workspace.HasUnsavedChanges ? "Brouillon — Ctrl+S pour enregistrer durablement"
            : "Développement — dossier local enregistré";
        RegisterDateColumn.Header = model?.DateColumnHeader ?? "Date";
        ApplyRateState();
        IsEnabled = !_busy;
        CommandManager.InvalidateRequerySuggested();
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
            AccountsSelector.SelectedItem is not AccountSummary selected ||
            ReferenceEquals(selected, model.SelectedAccount)) return;
        try { model.SelectedAccount = selected; }
        catch (Exception error) when (error is DocumentException or ArgumentException or InvalidOperationException or OverflowException)
        {
            // A failed binding setter must never leave one account's caption over another account's rows.
            AccountsSelector.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
            MessageBox.Show(this, error.Message, "Moneta — changement de compte interrompu", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, error is DocumentException ? error.Message : "L’opération n’a pas abouti. Les modifications en mémoire sont conservées. Vérifiez le fichier, son emplacement et les droits d’accès.", "Moneta — opération interrompue", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        Title = title + " — choisissez un nouveau fichier",
        Filter = "Dossier Moneta (*.moneta)|*.moneta",
        DefaultExt = ".moneta", AddExtension = true, OverwritePrompt = false,
        CheckPathExists = true, FileName = fileName
    };

    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        if (!_workspace.CanSave) return false;
        string? path = null;
        if (saveAs || _workspace.FilePath is null)
        {
            var dialog = Destination(saveAs ? "Enregistrer sous" : "Enregistrer le dossier");
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        return await RunIo(() => _workspace.Save(path), "Enregistrement…");
    }

    private async Task<bool> ConfirmReplacement()
    {
        if (!_workspace.HasUnsavedChanges) return true;
        var answer = MessageBox.Show(this, "Ce dossier contient des modifications non enregistrées. Voulez-vous les enregistrer avant de continuer ?", "Moneta — modifications en attente", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveAsync());
    }

    private async void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new NewDocumentWindow { Owner = this };
        if (dialog.ShowDialog() != true || !await ConfirmReplacement()) return;
        var name = dialog.FolderName;
        await RunIo(() => _workspace.New(name), "Nouveau dossier…");
    }

    private async void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Ouvrir un dossier financier", Filter = "Dossiers Moneta (*.moneta)|*.moneta|Fichiers BankPerfect (*.bp)|*.bp", CheckFileExists = true };
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
        }, "Vérification et ouverture…");
        if (!migration) return;
        if (MessageBox.Show(this, "Ce dossier utilise une ancienne version de schéma connue. Moneta peut créer une copie migrée et vérifiée. L’original restera inchangé. Continuer ?", "Moneta — migration sur copie", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var target = Destination("Copie migrée", "Dossier migré.moneta");
        if (target.ShowDialog(this) == true)
            await RunIo(() => _workspace.OpenMigratedCopy(dialog.FileName, target.FileName), "Migration sur une nouvelle copie…");
    }

    private async void ImportBankPerfect_Executed(object sender, ExecutedRoutedEventArgs e)
        => await ShowBankPerfectImportAsync();

    private async Task ShowBankPerfectImportAsync(string? source = null)
    {
        var dialog = new BankPerfectImportWindow(source) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result && await ConfirmReplacement())
            await RunIo(() => _workspace.Open(result.Destination), "Ouverture du dossier importé…");
    }

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync();
    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(true);
    private async void Demo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (await ConfirmReplacement()) await RunIo(_workspace.OpenDemo, "Ouverture des données fictives…");
    }
    private async void Backup_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_workspace.HasUnsavedChanges && !await SaveAsync()) return;
        var dialog = Destination("Sauvegarder le dossier", "Sauvegarde.moneta");
        if (dialog.ShowDialog(this) != true) return;
        if (await RunIo(() => _workspace.Backup(dialog.FileName), "Sauvegarde SQLite et vérification…"))
            MessageBox.Show(this, "Sauvegarde cohérente vérifiée. Conservez ensemble le fichier .moneta et son fichier .manifest.json. Testez une restauration avant de considérer cette copie comme votre unique sauvegarde.", "Moneta — sauvegarde", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void Restore_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var source = new OpenFileDialog { Title = "Choisir une sauvegarde Moneta avec son manifeste", Filter = "Sauvegarde Moneta (*.moneta)|*.moneta", CheckFileExists = true };
        if (source.ShowDialog(this) != true) return;
        var target = Destination("Restaurer vers un nouveau dossier", "Dossier restauré.moneta");
        if (target.ShowDialog(this) != true) return;
        if (await RunIo(() => DocumentWorkspace.Restore(source.FileName, target.FileName), "Restauration, réouverture et comparaison…"))
            MessageBox.Show(this, "La nouvelle copie a été rouverte et vérifiée. Le dossier actuellement affiché n’a pas été modifié. Utilisez Fichier → Ouvrir pour consulter le dossier restauré.", "Moneta — restauration vérifiée", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_busy || _closingPrompt) return;
        _closingPrompt = true;
        try
        {
            if (await ConfirmReplacement() && await RunIo(_workspace.Dispose, "Fermeture du dossier…"))
            {
                _allowClose = true;
                Close();
            }
        }
        finally { _closingPrompt = false; }
    }

    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void About_Executed(object sender, ExecutedRoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
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
            $"Retirer l’opération « {value.Label} » du {row.DateText} ?\n\n{(value.Status == ReconciliationStatus.Marked ? "Cette opération est pointée : le solde pointé sera recalculé.\n\n" : "")}La suppression sera enregistrée avec Ctrl+S. Une trace sera conservée dans le dossier ; aucune commande de récupération n’est encore disponible.",
            "Moneta — confirmer la suppression", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { model.DeleteTransaction(value.Id, value.Revision); }
        catch (Exception error) when (error is DocumentException or ArgumentException or InvalidOperationException or OverflowException)
        {
            MessageBox.Show(this, error.Message, "Moneta — suppression interrompue", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, error.Message, "Moneta — registre indisponible", MessageBoxButton.OK, MessageBoxImage.Warning);
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
