using System.Windows;
using System.Windows.Input;
using Moneta.Core;

namespace Moneta.App;

public partial class MainWindow
{
    private LedgerSearchWindow? _searchWindow;
    private FinancialDocument? _searchSnapshot;
    private string? _searchPath;

    private bool CanSearchLedger => !_busy && _workspace.FilePath is not null && _workspace.SavedSnapshot is not null;

    private void UpdateRegisterSearch()
    {
        var path = _workspace.FilePath;
        var snapshot = _workspace.SavedSnapshot;
        if (_searchWindow is not null && (path != _searchPath || snapshot?.Id != _searchSnapshot?.Id))
            _searchWindow.Close();
        if (_searchWindow is not null && snapshot is not null && snapshot != _searchSnapshot)
        {
            _searchSnapshot = snapshot;
            _searchWindow.UpdateCurrencies(snapshot);
            _searchWindow.RefreshAfterSave();
        }
        QuickSearch.IsEnabled = CanSearchLedger;
        SearchContext.Visibility = _searchWindow is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Search_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = e.Command == WindowCommands.ClearSearch
            ? CanSearchLedger && (_searchWindow is not null || QuickSearch.Text.Length > 0)
            : CanSearchLedger;
        e.Handled = true;
    }

    private void Search_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == WindowCommands.Search && Equals(e.Parameter, "Focus"))
        {
            QuickSearch.Focus();
            QuickSearch.SelectAll();
            return;
        }
        OpenRegisterSearch(e.Command == WindowCommands.SearchFilters, all: Equals(e.Parameter, "All"));
    }

    private async void ClearSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        QuickSearch.Clear();
        if (_searchWindow is not null) await _searchWindow.ClearAsync();
    }

    private void QuickSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenRegisterSearch(advanced: false);
    }

    private void ReturnToResults_Click(object sender, RoutedEventArgs e)
    {
        if (CanSearchLedger) _searchWindow?.Open(null, null, null, false, returning: true);
    }

    private void OpenRegisterSearch(bool advanced, bool all = false)
    {
        if (!CanSearchLedger || _workspace.FilePath is not { } path || _workspace.SavedSnapshot is not { } snapshot) return;
        if (_searchWindow is null)
        {
            _searchPath = path;
            _searchSnapshot = snapshot;
            _searchWindow = new LedgerSearchWindow(path, snapshot, NavigateFromSearchAsync) { Owner = this };
            _searchWindow.Closed += (_, _) =>
            {
                _searchWindow = null;
                SearchContext.Visibility = Visibility.Collapsed;
                CommandManager.InvalidateRequerySuggested();
            };
        }
        var account = _workspace.Model?.SelectedAccount;
        _searchWindow.Open(account?.Account.Id, account?.DisplayName, advanced ? null : QuickSearch.Text, advanced, all: all);
        SearchContext.Visibility = Visibility.Visible;
    }

    private async Task<bool> NavigateFromSearchAsync(LedgerSearchRow row)
    {
        if (!CanSearchLedger || _workspace.Model is not { } model) return false;
        LedgerPage? page = null;
        if (!await RunIo(() => page = model.LocateOperation(row.AccountId, row.Id), "Localisation dans le registre…") ||
            _workspace.Model != model || page is null || !model.ShowLocatedOperation(row.AccountId, row.Id, page))
            return false;
        if (model.SelectedRow is { } selected) TransactionsGrid.ScrollIntoView(selected);
        _ = Dispatcher.BeginInvoke(() => { Activate(); TransactionsGrid.Focus(); });
        return true;
    }
}
