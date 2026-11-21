using System.Windows;

namespace Moneta.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Alpha {MainWindow.ProductVersion.ToString(3)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
