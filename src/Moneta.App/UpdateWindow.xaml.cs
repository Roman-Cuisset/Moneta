using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using Moneta.Updates;

namespace Moneta.App;

public partial class UpdateWindow : Window
{
    private readonly UpdateClient _client;
    private readonly UpdateOffer _offer;
    private readonly Version _installed;
    private CancellationTokenSource? _download;
    public UpdateStage? Stage { get; private set; }

    public UpdateWindow(UpdateClient client, UpdateOffer offer, Version installed)
    {
        _client = client;
        _offer = offer;
        _installed = installed;
        InitializeComponent();
        Versions.Text = $"Version installée : {installed.ToString(3)} · Nouvelle version : {offer.Manifest.Version}";
        Notes.Text = offer.Manifest.ReleaseNotes;
        Install.IsEnabled = UpdateChannel.SupportsInstall;
        if (!Install.IsEnabled) Status.Text = "L’installation intégrée est disponible depuis Moneta installé sur Windows x64.";
        Loaded += (_, _) => Later.Focus();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_download is not null || !UpdateChannel.SupportsInstall) return;
        using var cancellation = new CancellationTokenSource();
        _download = cancellation;
        Install.IsEnabled = false;
        Later.Content = "_Annuler le téléchargement";
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        Status.Text = "Téléchargement et vérification…";
        try
        {
            Stage = await _client.DownloadAsync(_offer, _installed,
                new Progress<int>(value => DownloadProgress.Value = value), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Le téléchargement a été annulé ou le délai a expiré.";
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Status.Text = $"Mise à jour non préparée. {error.Message}";
        }
        finally
        {
            _download = null;
            Later.Content = "_Plus tard";
            Install.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }

        if (cancellation.IsCancellationRequested)
        {
            Stage?.Clean();
            Stage = null;
            Close();
        }
        else if (Stage is not null) DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_download is not null)
        {
            e.Cancel = true;
            _download.Cancel();
        }
        base.OnClosing(e);
    }
}
