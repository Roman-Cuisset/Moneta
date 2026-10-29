using System.Windows;
using System.Windows.Controls;

namespace Moneta.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var settings = App.Settings.Current;
        ThemeChoice.SelectedIndex = settings.Theme == "Dark" ? 1 : 0;
        CheckUpdates.IsChecked = settings.CheckUpdatesAtStartup;
        RefreshRates.IsChecked = settings.RefreshRatesAtStartup;
        SettingsPath.Text = App.Settings.FilePath;
        if (App.Settings.LoadError is { } error)
        {
            LoadWarning.Text = error;
            LoadWarning.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeChoice.SelectedItem is not ComboBoxItem { Tag: string theme })
        {
            return;
        }

        var settings = App.Settings.Current with
        {
            Theme = theme,
            CheckUpdatesAtStartup = CheckUpdates.IsChecked == true,
            RefreshRatesAtStartup = RefreshRates.IsChecked == true
        };
        if (App.SaveSettings(settings, this))
        {
            DialogResult = true;
        }
    }
}
