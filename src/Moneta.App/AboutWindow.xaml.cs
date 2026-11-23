using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Moneta.App;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/Roman-Cuisset/Moneta";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = L.Format("AboutWindow.VersionFormat", MainWindow.ProductVersion.ToString(3));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        if (!File.Exists(path))
        {
            ShowOpenError(L.Get("AboutWindow.LicenseFileNotFound"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            ShowOpenError(L.Get("AboutWindow.OpenLinkError"));
        }
    }

    private void OpenNotices_Click(object sender, RoutedEventArgs e) =>
        OpenPath(Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices"), "AboutWindow.NoticesFolderNotFound");

    private void OpenRepository_Click(object sender, RoutedEventArgs e) => OpenExternal(RepositoryUrl);

    private void OpenPath(string path, string missingKey)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            ShowOpenError(L.Get(missingKey));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            ShowOpenError(L.Get("AboutWindow.OpenLinkError"));
        }
    }

    private void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            ShowOpenError(L.Get("AboutWindow.OpenLinkError"));
        }
    }

    private void ShowOpenError(string message) =>
        MessageBox.Show(this, message, L.Get("Common.AboutMoneta"), MessageBoxButton.OK, MessageBoxImage.Warning);
}
