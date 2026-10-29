using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void About_Executed(object sender, ExecutedRoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void AddAccount_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) new DemoAccountWindow(model) { Owner = this }.ShowDialog();
    }
    private void Theme_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string mode) App.ChangeTheme(mode);
        UpdateThemeChecks();
    }
    private void ThemeMenu_Opened(object sender, RoutedEventArgs e) => UpdateThemeChecks();
    private void UpdateThemeChecks()
    {
        LightThemeItem.IsChecked = App.ThemeController.CurrentMode == "Light";
        DarkThemeItem.IsChecked = App.ThemeController.CurrentMode == "Dark";
    }
    private void Unavailable_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = false; e.Handled = true; }
    private void TransactionSelectionChanged(object sender, SelectionChangedEventArgs e) => DetailsText?.ScrollToHome();
}
