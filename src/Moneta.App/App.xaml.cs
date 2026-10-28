using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;

namespace Moneta.App;

public partial class App : Application
{
    public static ThemeController ThemeController { get; } = new();

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
        ThemeController.Initialize();
        base.OnStartup(e);

        MainWindow = new MainWindow
        {
            DataContext = new MainViewModel(DemoData.Create())
        };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeController.Dispose();
        base.OnExit(e);
    }
}
