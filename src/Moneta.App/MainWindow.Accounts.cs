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
            var action = archive ? L.Get("Accounts.Archiver") : L.Get("Accounts.Reactivating");
            var effect = archive ? L.Get("Accounts.TheRegisterAndItsLinksRemainSearchable") : L.Get("Accounts.ThisAccountBecomesAvailableAgainForEntry");
            if (ConfirmAccountChange(L.Format("Accounts.ConfirmAccountChange", action, summary.DisplayName, effect), action))
                model.SetAccountArchived(summary.Account.Id, archive);
        });
    }

    private void MoveAccount_Executed(object sender, ExecutedRoutedEventArgs e) =>
        ManageAccount((model, summary) => model.MoveAccount(summary.Account.Id, AccountStep(e.Parameter)));

    private void DeleteAccount_Executed(object sender, ExecutedRoutedEventArgs e) =>
        ManageAccount((model, summary) =>
        {
            model.ValidateAccountDeletion(summary.Account.Id);
            if (ConfirmAccountChange(L.Format("Accounts.Delete0TheRemovalOfTheBlankAccount", (summary.DisplayName)), L.Get("Accounts.RemoveTheBlankAccount")))
                model.DeleteEmptyAccount(summary.Account.Id);
        });

    private bool ConfirmAccountChange(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ManageAccount(Action<MainViewModel, AccountSummary> action)
    {
        if (_busy || _workspace.Model is not { IsDemo: false, SelectedAccount: { } summary } model) return;
        try { action(model, summary); }
        catch (Exception error) when (IsLedgerError(error)) { ShowLedgerError(error, L.Get("Accounts.AccountManagement")); }
        CommandManager.InvalidateRequerySuggested();
    }
}
