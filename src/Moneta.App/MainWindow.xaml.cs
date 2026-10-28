using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Moneta.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Close();

    private void About_Executed(object sender, ExecutedRoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Content: string mode } })
            App.ThemeController.Apply(mode);
    }

    private void TransactionSelectionChanged(object sender, SelectionChangedEventArgs e)
        => DetailsText?.ScrollToHome();
}
