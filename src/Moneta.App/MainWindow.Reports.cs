using System.Globalization;
using System.Windows.Input;
using Moneta.Core;

namespace Moneta.App;

public partial class MainWindow
{
    private void Reports_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && _workspace.SavedSnapshot is not null;
        e.Handled = true;
    }

    private void Reports_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_workspace.SavedSnapshot is null) return;
        var tab = e.Command == WindowCommands.Budgets ? 6 : e.Command == WindowCommands.CategoryReports ? 2
            : int.TryParse(e.Parameter as string, CultureInfo.InvariantCulture, out var index) ? index : 0;
        ReportsWindow? window = null;
        window = new ReportsWindow(_workspace, new FinancialReportEngine(App.HistoricalFx), new ReportForecast(App.HistoricalFx),
            App.SelectedRateProvider, () => SaveAsync(dialogOwner: window), tab) { Owner = this };
        window.ShowDialog();
    }
}
