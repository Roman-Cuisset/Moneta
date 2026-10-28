using System.Windows;

namespace Moneta.App;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
