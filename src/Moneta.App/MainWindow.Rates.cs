using System.Windows;
using System.Windows.Input;

namespace Moneta.App;

public partial class MainWindow
{
    private void Rates_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyRateState();
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private void ApplyRateState()
    {
        if (_workspace.Model is not { } model) return;
        var state = App.Rates.State;
        model.UpdateRateDisplay(state.Rates, state.Provider, state.Date, state.FromCache, state.Message);
    }

    private void ShowRates_Executed(object sender, ExecutedRoutedEventArgs e) =>
        new RatesWindow(App.Rates, _workspace.Model?.DisplayCurrencies) { Owner = this }.ShowDialog();

    private void RefreshRates_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && !App.Rates.State.IsRefreshing;
        e.Handled = true;
    }

    private async void RefreshRates_Executed(object sender, ExecutedRoutedEventArgs e) => await App.Rates.RefreshAsync();
}
