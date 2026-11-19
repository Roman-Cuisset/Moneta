using System.Windows.Input;

namespace Moneta.App;

public partial class MainWindow
{
    private void Schedule_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && !_stopBackupRequests && _workspace.Model is { IsDemo: false };
        e.Handled = true;
    }

    private void Schedule_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_busy || _workspace.Model is not { IsDemo: false } model) return;
        ScheduleWindow? window = null;
        window = new ScheduleWindow(model, () => SaveAsync(dialogOwner: window)) { Owner = this };
        window.ShowDialog();
        RefreshScheduleReminder();
    }

    private void RefreshScheduleReminder()
    {
        if (_busy) return;
        var due = _workspace.Model?.ScheduleCalendar().Count(DateOnly.MinValue, DateOnly.FromDateTime(DateTime.Today)) ?? 0;
        ScheduleReminder.Content = due switch
        {
            0 => "Échéancier",
            1 => "1 échéance à traiter",
            _ => $"{due:N0} échéances à traiter"
        };
    }
}
