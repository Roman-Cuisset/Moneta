using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MainWindow
{
    private void ManageAccount_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && _workspace.Model is { IsDemo: false, SelectedAccount: { } summary } model &&
            (e.Command == WindowCommands.ArchiveAccount ? !summary.Account.IsArchived :
             e.Command == WindowCommands.ReactivateAccount ? summary.Account.IsArchived :
             e.Command != WindowCommands.MoveAccount || model.CanMoveAccount(summary.Account.Id, AccountStep(e.Parameter)));
        e.Handled = true;
    }

    private static int AccountStep(object? parameter) => Equals(parameter, "Up") ? -1 : 1;

    private void AccountList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject hit && ItemsControl.ContainerFromElement(AccountList, hit) is ListBoxItem item)
            item.SetCurrentValue(ListBoxItem.IsSelectedProperty, true);
    }

    private void AccountProperties_Executed(object sender, ExecutedRoutedEventArgs e) =>
        ManageAccount((model, summary) => new AccountWindow(model, summary.Account) { Owner = this }.ShowDialog());

    private void SetAccountArchived_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var archive = e.Command == WindowCommands.ArchiveAccount;
        ManageAccount((model, summary) =>
        {
            var action = archive ? "Archiver" : "Réactiver";
            var effect = archive ? "Le registre et ses liens restent consultables." : "Ce compte redevient disponible pour la saisie.";
            if (ConfirmAccountChange($"{action} « {summary.DisplayName} » ?\n\n{effect}\nCtrl+S enregistre ce changement.", action))
                model.SetAccountArchived(summary.Account.Id, archive);
        });
    }

    private void MoveAccount_Executed(object sender, ExecutedRoutedEventArgs e) =>
        ManageAccount((model, summary) => model.MoveAccount(summary.Account.Id, AccountStep(e.Parameter)));

    private void DeleteAccount_Executed(object sender, ExecutedRoutedEventArgs e) =>
        ManageAccount((model, summary) =>
        {
            model.ValidateAccountDeletion(summary.Account.Id);
            if (ConfirmAccountChange($"Supprimer « {summary.DisplayName} » ?\n\nLa suppression du compte vierge devient définitive après Ctrl+S.", "Supprimer le compte vierge"))
                model.DeleteEmptyAccount(summary.Account.Id);
        });

    private bool ConfirmAccountChange(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ArchivedSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _workspace.Model is not { } model) return;
        try
        {
            if (model.FindArchivedOperation(ArchivedQuery.Text) && model.SelectedRow is { } row)
                TransactionsGrid.ScrollIntoView(row);
            else
                MessageBox.Show(this, "Aucune opération correspondante dans ce compte.", "Recherche dans le registre");
        }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "recherche dans le registre"); }
    }

    private void ManageAccount(Action<MainViewModel, AccountSummary> action)
    {
        if (_busy || _workspace.Model is not { IsDemo: false, SelectedAccount: { } summary } model) return;
        try { action(model, summary); }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, "gestion du compte"); }
        CommandManager.InvalidateRequerySuggested();
    }
}
