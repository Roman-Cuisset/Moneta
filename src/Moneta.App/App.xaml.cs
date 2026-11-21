using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;
using Moneta.Updates;

namespace Moneta.App;

public partial class App : Application
{
    private Mutex? _running;
    public static ThemeController ThemeController { get; } = new();
    public static SettingsStore Settings { get; } = new();
    private static readonly OfficialRateClient[] RateClients =
        [new(RateProvider.Nbrb), new(RateProvider.Cbr)];
    private static readonly SqliteRateCache RateCache = new();
    public static ExchangeRateSession Rates { get; } = new(RateCache, RateClients);
    public static HistoricalValuation HistoricalFx { get; } = new(new HistoricalRateResolver(RateCache, RateClients));
    public static RateProvider SelectedRateProvider => Settings.Current.RateSource == "CBR" ? RateProvider.Cbr : RateProvider.Nbrb;

    public static bool ChangeTheme(string mode) =>
        SaveSettings(Settings.Current with { Theme = mode });

    public static bool SaveSettings(LocalSettings settings, Window? owner = null)
    {
        var previousSource = Settings.Current.RateSource;
        try
        {
            Settings.Save(settings);
        }
        catch (SettingsException ex)
        {
            ShowSettingsError(ex.Message, owner);
            return false;
        }

        ThemeController.Apply(settings.Theme);
        if (previousSource != settings.RateSource)
            _ = Rates.SelectAsync(SelectedRateProvider, Rates.State.Date);
        return true;
    }

    private static void ShowSettingsError(string message, Window? owner = null)
    {
        owner ??= Current?.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, "Préférences locales — Moneta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(owner, message, "Préférences locales — Moneta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public App()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _running = new Mutex(false, UpdateChannel.RunningMutex);
        string? settingsError = null;
        try
        {
            Settings.Load();
        }
        catch (SettingsException ex)
        {
            settingsError = ex.Message;
        }

        ThemeController.Apply(Settings.Current.Theme);
        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
        ((MainWindow)MainWindow).InitializeUpdates(e.Args);
        _ = Rates.InitializeAsync(SelectedRateProvider, Settings.Current.RefreshRatesAtStartup);
        if (settingsError is not null)
        {
            ShowSettingsError(settingsError, MainWindow);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Rates.Dispose();
        foreach (var client in RateClients) client.Dispose();
        ThemeController.Dispose();
        _running?.Dispose();
        base.OnExit(e);
    }
}
