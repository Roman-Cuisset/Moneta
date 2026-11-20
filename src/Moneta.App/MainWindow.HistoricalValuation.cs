using System.Windows.Input;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class MainWindow
{
    private void HistoricalFx_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        var id = e.Command == WindowCommands.HistoricalFlow
            ? _workspace.Model?.SelectedRow?.Transaction.AccountId : _workspace.Model?.SelectedAccountId;
        e.CanExecute = !_busy && id is { } account && _workspace.SavedSnapshot?.Accounts.Any(item => item.Id == account) == true
            && (e.Command != WindowCommands.HistoricalFlow || _workspace.Model!.PendingChanges.All(item =>
                item.Value.Id != _workspace.Model.SelectedRow!.Transaction.Id || item.ExpectedRevision != 0));
        e.Handled = true;
    }

    private void HistoricalFx_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var snapshot = _workspace.SavedSnapshot;
        var path = _workspace.FilePath;
        var model = _workspace.Model;
        Func<CancellationToken, HistoricalInput>? flow = null;
        Func<DateOnly, CancellationToken, HistoricalInput>? balance = null;
        if (snapshot is not null && path is not null)
        {
            if (e.Command == WindowCommands.HistoricalFlow && model?.SelectedRow?.Transaction.Id is { } operation)
                flow = token => MonetaFile.ReadHistoricalFlow(path, snapshot, operation, token).Input();
            else if (e.Command == WindowCommands.HistoricalBalance && model?.SelectedAccountId is { } account)
                balance = (date, token) => MonetaFile.ReadHistoricalBalance(path, snapshot, account, date, token).Input();
        }
        new HistoricalValuationWindow(App.HistoricalFx, model?.DisplayCurrencies ?? new CurrencyRegistry().Currencies,
            App.SelectedRateProvider, flow, balance) { Owner = this }.ShowDialog();
    }
}
