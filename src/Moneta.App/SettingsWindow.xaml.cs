using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class SettingsWindow : Window
{
    private readonly Action? _history;
    private readonly Func<string>? _backupInformation;

    public SettingsWindow(Func<string>? backupInformation = null, Action? history = null)
    {
        _history = history;
        _backupInformation = backupInformation;
        InitializeComponent();
        var settings = App.Settings.Current;
        LanguageChoice.SelectedIndex = settings.Language == L.English ? 1 : 0;
        Closed += (_, _) => App.ApplyLanguage(App.Settings.Current.Language);
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
        BackupHistory.IsEnabled = _history is not null;
        RefreshLanguageLabels();
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
            LanguageChoice.SelectedItem is not ComboBoxItem { Tag: string language } ||
            RateSourceChoice.SelectedItem is not ComboBoxItem { Tag: string rateSource })
        {
            return;
        }

        if (!int.TryParse(BackupInterval.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(BackupRetention.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var retention))
        {
            ReportBackupInput(L.Get("SettingsWindow.EnterIntegerNumbersForIntervalAndRetention"));
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
            Language = language,
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

    private void LanguageChoice_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (IsLoaded && LanguageChoice.SelectedItem is ComboBoxItem { Tag: string language })
        {
            App.ApplyLanguage(language);
            RefreshLanguageLabels();
        }
    }

    private void RefreshLanguageLabels()
    {
        BackupDefaultPath.Text = L.Get("SettingsWindow.ByDefault") + new BackupPolicy().Destination;
        BackupInformation.Text = _backupInformation?.Invoke() ?? "";
        var rates = App.Rates.State.Rates;
        RatesInformation.Text = rates is null ? L.Get("SettingsWindow.NoApplicableExchangeRateLoaded")
            : L.Format("SettingsWindow.LastRateLoaded0EffectOf1Mm", Moneta.Core.RateRules.ProviderName(rates.Provider),
                rates.EffectiveDate, rates.RetrievedUtc.ToLocalTime());
    }

    private void ReportBackupInput(string error)
    {
        BackupValidation.Text = error;
        if (BackupValidation.Parent is FrameworkElement element)
            element.BringIntoView();
    }

    private void BrowseBackups_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = L.Get("SettingsWindow.DestinationOfMonetaBackups") };
        if (picker.ShowDialog(this) == true) BackupDestination.Text = picker.FolderName;
    }

    private void BackupHistory_Click(object sender, RoutedEventArgs e) => _history?.Invoke();
}
