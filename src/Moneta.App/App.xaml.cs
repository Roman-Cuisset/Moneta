using System.Globalization;
using System.Windows;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;
using Moneta.Core;
using Moneta.Infrastructure.ExchangeRates;
using Moneta.Updates;

namespace Moneta.App;

public partial class App : Application
{
    private Mutex? _running;
    private FileActivation? _fileActivation;
    public static ThemeController ThemeController { get; } = new();
    public static SettingsStore Settings { get; } = new();
    private static readonly OfficialRateClient[] RateClients =
        [new(RateProvider.Nbrb), new(RateProvider.Cbr)];
    private static readonly SqliteRateCache RateCache = new();
    public static ExchangeRateSession Rates { get; } = CreateRateSession();
    internal static ExchangeRateSession CreateRateSession() => new(RateCache, RateClients);
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
        ApplyLanguage(settings.Language);
        if (previousSource != settings.RateSource)
            _ = Rates.SelectAsync(SelectedRateProvider, Rates.Today);
        return true;
    }

    private static void ShowSettingsError(string message, Window? owner = null)
    {
        owner ??= Current?.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, L.Get("App.LocalPreferencesMoneta"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(owner, message, L.Get("App.LocalPreferencesMoneta"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(L.ResolveLanguage(language));
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        L.SetLanguage(culture.Name);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        string? settingsError = null;
        try
        {
            Settings.Load();
        }
        catch (SettingsException ex)
        {
            settingsError = ex.Message;
        }

        ApplyLanguage(Settings.Current.Language);
        base.OnStartup(e);

        var activation = _fileActivation = new FileActivation();
        var path = e.Args.Length == 1 && !e.Args[0].StartsWith("--", StringComparison.Ordinal) ? e.Args[0] : string.Empty;
        if (!activation.IsPrimary)
        {
            if (!await activation.ForwardAsync(path))
                MessageBox.Show(L.Get("FileActivation.HandoffFailed"), L.Get("FileActivation.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _running = new Mutex(false, UpdateChannel.RunningMutex);
        ThemeController.Apply(Settings.Current.Theme);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        activation.Listen(request => Dispatcher.InvokeAsync(() => window.QueueActivation(request)).Task);
        if (path.Length > 0) window.QueueActivation(path);
        window.InitializeUpdates(e.Args);
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
        _fileActivation?.Dispose();
        _running?.Dispose();
        base.OnExit(e);
    }
}
