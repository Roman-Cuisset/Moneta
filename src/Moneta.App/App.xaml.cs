using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;

namespace Moneta.App;

public partial class App : Application
{
    public static ThemeController ThemeController { get; } = new();
    public static SettingsStore Settings { get; } = new();

    public static bool ChangeTheme(string mode) =>
        SaveSettings(Settings.Current with { Theme = mode });

    public static bool SaveSettings(LocalSettings settings, Window? owner = null)
    {
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

        MainWindow = new MainWindow
        {
            DataContext = new MainViewModel(DemoData.Create())
        };
        MainWindow.Show();
        if (settingsError is not null)
        {
            ShowSettingsError(settingsError, MainWindow);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeController.Dispose();
        base.OnExit(e);
    }
}
