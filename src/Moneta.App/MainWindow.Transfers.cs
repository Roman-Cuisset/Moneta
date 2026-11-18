using System.Windows;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MainWindow
{
    private LedgerRow? TransferRow(object? parameter) => parameter as LedgerRow ?? SingleSelectedRow;

    private void NewTransfer_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = false;
        if (CanEditLedger && _workspace.Model is { } model)
        {
            var active = 0;
            foreach (var account in model.Accounts)
                if (!account.Account.IsArchived && ++active == 2) { e.CanExecute = true; break; }
        }
        e.Handled = true;
    }

    private void ReadTransfer_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // No storage reads in CanExecute, which WPF can query repeatedly during input/rendering.
        e.CanExecute = !_busy && _workspace.Model is { IsDemo: false } && TransferRow(e.Parameter)?.TransferId is not null;
        e.Handled = true;
    }

    private void DeleteTransfer_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanEditLedger && TransferRow(e.Parameter) is
            { TransferId: not null, Transaction.Status: not ReconciliationStatus.Reconciled };
        e.Handled = true;
    }

    private void NewTransfer_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!CanEditLedger || _workspace.Model is not { } model) return;
        try { new TransferWindow(model) { Owner = this }.ShowDialog(); }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "virement indisponible"); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenTransfer_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (TransferRow(e.Parameter)?.TransferId is { } id) OpenTransfer(id);
    }

    private void OpenTransfer(Guid id)
    {
        if (_busy || _workspace.Model is not { IsDemo: false } model) return;
        try
        {
            var details = model.GetTransfer(id);
            new TransferWindow(model, details, readOnly: !CanEditLedger || details.HasReconciledLeg) { Owner = this }.ShowDialog();
        }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "lecture du virement interrompue"); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void NavigateTransfer_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_busy || _workspace.Model is not { IsDemo: false } model || TransferRow(e.Parameter) is not { TransferId: { } id } row) return;
        try
        {
            model.NavigateToTransferCounterpart(id, row.Transaction.Id);
            if (model.SelectedRow is { } selected) TransactionsGrid.ScrollIntoView(selected);
            TransactionsGrid.Focus();
        }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "navigation du virement interrompue"); }
        CommandManager.InvalidateRequerySuggested();
    }

    private void DeleteTransfer_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (TransferRow(e.Parameter)?.TransferId is { } id) DeleteWholeTransfer(id);
    }

    private void DeleteWholeTransfer(Guid id)
    {
        if (!CanEditLedger || _workspace.Model is not { } model) return;
        try
        {
            var details = model.GetTransfer(id);
            if (model.HasArchivedTransferAccount(details))
                throw new InvalidOperationException("Réactivez les comptes du virement, frais compris, avant de le supprimer.");
            if (details.HasReconciledLeg)
                throw new InvalidOperationException("Un mouvement de ce virement est rapproché. Rouvrez le relevé concerné puis enregistrez avant de corriger ou supprimer le virement entier.");
            string Name(Guid accountId) => model.Accounts.First(account => account.Account.Id == accountId).DisplayName;
            var fee = details.Fee is { } value
                ? $"\nFrais : −{MoneyFormatter.Format(value.Amount, value.Currency)} sur {Name(value.AccountId)}" : string.Empty;
            var message = $"Supprimer le virement entier ?\n\n{ Name(details.Debit.AccountId) } : −{MoneyFormatter.Format(details.Debit.Amount, details.Debit.Currency)}\n{ Name(details.Credit.AccountId) } : +{MoneyFormatter.Format(details.Credit.Amount, details.Credit.Currency)}{fee}\n\nLes deux mouvements et les frais liés seront retirés ensemble. Les traces déjà enregistrées seront conservées. Ctrl+S reste nécessaire.";
            if (MessageBox.Show(this, message, "Moneta — supprimer le virement entier", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            model.DeleteTransfer(id, details.Value.Revision);
        }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "suppression du virement interrompue"); }
        CommandManager.InvalidateRequerySuggested();
    }
}
