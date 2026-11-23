using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MainWindow
{
    private LedgerRow? SingleSelectedRow => TransactionsGrid.SelectedItems.Count == 1
        ? TransactionsGrid.SelectedItems[0] as LedgerRow : null;

    private void Pointing_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger;
        if (e.CanExecute && e.Parameter is LedgerRow target && !TransactionsGrid.SelectedItems.Contains(target))
            e.CanExecute = target.Transaction.Status != ReconciliationStatus.Reconciled;
        else if (e.CanExecute)
        {
            e.CanExecute = TransactionsGrid.SelectedItems.Count > 0;
            foreach (var item in TransactionsGrid.SelectedItems)
                if (item is not LedgerRow row || row.Transaction.Status == ReconciliationStatus.Reconciled) { e.CanExecute = false; break; }
        }
        e.Handled = true;
    }

    private void MarkingDate_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger && SingleSelectedRow?.Transaction.Status == ReconciliationStatus.Marked;
        e.Handled = true;
    }

    private void ReleaseLegacy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger && _workspace.Model is { CanReconcile: true } && SingleSelectedRow?.Transaction is
            { Status: ReconciliationStatus.Reconciled, ReconciliationId: null };
        e.Handled = true;
    }

    private void Reconcile_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger || (!_busy && _workspace.Model is
            { IsDemo: false, SelectedAccount.Account.IsArchived: true, ReconciliationSource: not null });
        e.Handled = true;
    }

    private void ShowMarkingDates_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && _workspace.Model is not null;
        e.Handled = true;
    }

    private void Mark_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyPointing(e.Parameter, true);
    private void Unmark_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyPointing(e.Parameter, false);
    private void TogglePointing_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyPointing(e.Parameter, null);

    private void ApplyPointing(object? parameter, bool? pointed)
    {
        if (!CanEditLedger || _workspace.Model is not { } model) return;
        var rows = parameter is LedgerRow target && !TransactionsGrid.SelectedItems.Contains(target)
            ? new[] { target } : TransactionsGrid.SelectedItems.OfType<LedgerRow>().ToArray();
        if (rows.Length == 0) return;
        var selected = rows.Select(row => new TransactionSelection(row.Transaction.Id, row.Transaction.Revision)).ToArray();
        try
        {
            if (pointed is { } state) model.SetPointing(selected, state); else model.TogglePointing(selected);
            // Page replacement uses new row objects. Restore the whole explicit selection, not just the anchor.
            var ids = selected.Select(value => value.Id).ToHashSet();
            TransactionsGrid.SelectedItems.Clear();
            foreach (var row in model.Rows)
                if (ids.Contains(row.Transaction.Id)) TransactionsGrid.SelectedItems.Add(row);
        }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "pointage interrompu"); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void MarkingDate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CanEditLedger && _workspace.Model is { } model && SingleSelectedRow is { } row)
            new MarkingDateWindow(model, new TransactionSelection(row.Transaction.Id, row.Transaction.Revision), row.Transaction.MarkedOn) { Owner = this }.ShowDialog();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowMarkingDates_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_workspace.Model is not { } model) return;
        model.ShowMarkingDates = !model.ShowMarkingDates;
        RegisterDateColumn.Header = model.DateColumnHeader;
    }

    private async void Reconcile_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!_busy && _workspace.Model is { IsDemo: false, SelectedAccount.Account.IsArchived: true } archived)
        {
            ShowReconciliation(archived);
            return;
        }
        if (!CanEditLedger) return;
        if (_workspace.HasUnsavedChanges)
        {
            if (MessageBox.Show(this,
                L.Get("Pointing.TheReconciliationUsesTheRecordedTransactionsFirstSave"),
                L.Get("Pointing.MonetaBeforeReconciliation"), MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes || !await SaveAsync()) return;
        }
        if (_workspace.Model is not { CanReconcile: true } model) return;
        ShowReconciliation(model);
    }

    private void ShowReconciliation(MainViewModel model)
    {
        try { new ReconciliationWindow(model) { Owner = this }.ShowDialog(); }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "rapprochement indisponible"); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void ReleaseLegacy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!CanEditLedger || _workspace.Model is not { } model || SingleSelectedRow is not { } row) return;
        if (!model.CanReconcile)
        {
            MessageBox.Show(this, L.Get("Pointing.FirstSaveTheFileAndAllItsDrafts"),
                L.Get("Pointing.MonetaPriorToReview"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var reason = new ReopenReasonWindow(L.Get("Pointing.ReconsiderAnInheritedReconciliation"),
            L.Get("Pointing.ThisCloseStateIsNotLinkedToAny")) { Owner = this };
        if (reason.ShowDialog() != true) return;
        if (MessageBox.Show(this, L.Get("Pointing.ConfirmTheReleaseOfThisInheritedStateTo"),
            L.Get("Pointing.MonetaConfirmReview"), MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { model.ReleaseLegacyReconciliation(new(row.Transaction.Id, row.Transaction.Revision), reason.Reason); }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, L.Get("Pointing.ReExaminationDiscontinued")); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void Register_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element &&
            ItemsControl.ContainerFromElement(TransactionsGrid, element) is DataGridRow row && !row.IsSelected)
        {
            TransactionsGrid.SelectedItems.Clear();
            row.IsSelected = true;
            row.Focus();
        }
    }

    private static bool IsLedgerError(Exception error) => error is DocumentException or ArgumentException
        or InvalidOperationException or OverflowException or System.IO.IOException or UnauthorizedAccessException;

    private void ShowLedgerError(Exception error, string action) =>
        MessageBox.Show(this, error.Message, "Moneta — " + action, MessageBoxButton.OK, MessageBoxImage.Warning);
}
