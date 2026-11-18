using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class SettingsWindow : Window
{
    private readonly Action? _history;

    public SettingsWindow(string? backupInformation = null, Action? history = null)
    {
        _history = history;
        InitializeComponent();
        var settings = App.Settings.Current;
        ThemeChoice.SelectedIndex = settings.Theme == "Dark" ? 1 : 0;
        CheckUpdates.IsChecked = settings.CheckUpdatesAtStartup;
        RefreshRates.IsChecked = settings.RefreshRatesAtStartup;
        RateSourceChoice.SelectedIndex = settings.RateSource == "CBR" ? 1 : 0;
        var backups = settings.Backups;
        AutomaticBackup.IsChecked = backups.Enabled;
        BackupAfterSave.IsChecked = backups.AfterSave;
        BackupDaily.IsChecked = backups.Daily;
        BackupInterval.Text = backups.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        BackupRetention.Text = backups.VersionsToKeep.ToString(CultureInfo.InvariantCulture);
        BackupDestination.Text = backups.DirectoryPath ?? "";
        BackupDefaultPath.Text = "Par défaut : " + new BackupPolicy().Destination;
        BackupInformation.Text = backupInformation ?? "";
        BackupHistory.IsEnabled = _history is not null;
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

        if (!int.TryParse(BackupInterval.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(BackupRetention.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var retention))
        {
            ReportBackupInput("Saisissez des nombres entiers pour l’intervalle et la rétention.");
            return;
        }
        var backups = new BackupPolicy
        {
            Enabled = AutomaticBackup.IsChecked == true,
            AfterSave = BackupAfterSave.IsChecked == true,
            Daily = BackupDaily.IsChecked == true,
            IntervalMinutes = minutes,
            VersionsToKeep = retention,
            DirectoryPath = string.IsNullOrWhiteSpace(BackupDestination.Text) ? null : BackupDestination.Text.Trim()
        };
        try { backups.Validate(); }
        catch (ArgumentException error) { ReportBackupInput(error.Message); return; }
        var settings = App.Settings.Current with
        {
            Theme = theme,
            CheckUpdatesAtStartup = CheckUpdates.IsChecked == true,
            RefreshRatesAtStartup = RefreshRates.IsChecked == true,
            RateSource = rateSource,
            Backups = backups
        };
        if (App.SaveSettings(settings, this))
        {
            DialogResult = true;
        }
    }

    private void ReportBackupInput(string error)
    {
        BackupValidation.Text = error;
        if (BackupValidation.Parent is FrameworkElement element)
            element.BringIntoView();
    }

    private void BrowseBackups_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Destination des sauvegardes Moneta" };
        if (picker.ShowDialog(this) == true) BackupDestination.Text = picker.FolderName;
    }

    private void BackupHistory_Click(object sender, RoutedEventArgs e) => _history?.Invoke();
}
