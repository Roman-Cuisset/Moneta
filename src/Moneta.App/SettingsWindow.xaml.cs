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
        RateSourceChoice.SelectedIndex = settings.RateSource == "CBR" ? 1 : 0;
        var rates = App.Rates.State.Rates;
        RatesInformation.Text = rates is null ? "Aucun cours applicable chargé."
            : $"Dernier cours chargé : {Moneta.Core.RateRules.ProviderName(rates.Provider)}, effet du {rates.EffectiveDate:dd/MM/yyyy}, récupération {rates.RetrievedUtc.ToLocalTime():dd/MM/yyyy HH:mm}.";
        SettingsPath.Text = App.Settings.FilePath;
        if (App.Settings.LoadError is { } error)
        {
            LoadWarning.Text = error;
            LoadWarning.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeChoice.SelectedItem is not ComboBoxItem { Tag: string theme } ||
            RateSourceChoice.SelectedItem is not ComboBoxItem { Tag: string rateSource })
        {
            return;
        }

        var settings = App.Settings.Current with
        {
            Theme = theme,
            CheckUpdatesAtStartup = CheckUpdates.IsChecked == true,
            RefreshRatesAtStartup = RefreshRates.IsChecked == true,
            RateSource = rateSource
        };
        if (App.SaveSettings(settings, this))
        {
            DialogResult = true;
        }
    }
}
