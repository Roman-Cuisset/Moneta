using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Moneta.Updates;

namespace Moneta.App;

public partial class MainWindow
{
    internal static Version ProductVersion { get; } = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 1, 0);
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _checkingUpdates;
    private UpdateStage? _pendingUpdate;

    internal void InitializeUpdates(IReadOnlyList<string> args)
    {
        Closed += (_, _) => _updateLifetime.Cancel();
        if (args.Count == 2 && args[0] == "--update-result" && Guid.TryParseExact(args[1], "N", out var id))
            _ = ShowUpdateResultAsync(new UpdateStage(id));
        else if (App.Settings.Current.CheckUpdatesAtStartup)
            _ = CheckForUpdatesAsync(manual: false);
    }

    private async void CheckUpdates_Executed(object sender, ExecutedRoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private void CheckUpdates_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && !_closingPrompt && !_checkingUpdates;
        e.Handled = true;
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates || _closingPrompt) return;
        _checkingUpdates = true;
        CommandManager.InvalidateRequerySuggested();
        var token = _updateLifetime.Token;
        try
        {
            if (!manual) await Task.Delay(TimeSpan.FromSeconds(2), token);
            using var client = new UpdateClient(UpdateChannel.Current);
            var offer = await client.CheckAsync(ProductVersion, token);
            if (token.IsCancellationRequested || _closingPrompt || _busy ||
                (!manual && OwnedWindows.Cast<Window>().Any(window => window.IsVisible && window.IsActive))) return;
            if (offer is null)
            {
                if (manual) MessageBox.Show(this, "Aucune version plus récente publiée n’a été trouvée.", "Moneta — mises à jour");
                return;
            }

            var dialog = new UpdateWindow(client, offer, ProductVersion) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Stage is { } stage)
            {
                _pendingUpdate = stage;
                Close();
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            if (manual && !token.IsCancellationRequested)
                MessageBox.Show(this, $"La recherche des mises à jour n’a pas abouti. {error.Message}", "Moneta — mises à jour",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkingUpdates = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool StartUpdateHost(UpdateStage stage)
    {
        try
        {
            var source = Path.Combine(UpdateChannel.InstallDirectory, "Moneta.UpdateHost.exe");
            File.Copy(source, stage.Helper, overwrite: false);
            using var parent = Process.GetCurrentProcess();
            var launch = new ProcessStartInfo(stage.Helper) { UseShellExecute = false, CreateNoWindow = true };
            launch.ArgumentList.Add(stage.Id);
            launch.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            launch.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
            using var child = Process.Start(launch);
            if (child is null) throw new InvalidOperationException("Processus de mise à jour indisponible.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"L’installation n’a pas pu démarrer. {error.Message}", "Moneta — mise à jour",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool AuthorizeUpdate(UpdateStage stage)
    {
        try
        {
            using var marker = new FileStream(stage.Ready, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            marker.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"L’installation n’a pas été lancée. {error.Message}", "Moneta — mise à jour",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async Task ShowUpdateResultAsync(UpdateStage stage)
    {
        await Task.Delay(750);
        if (_updateLifetime.IsCancellationRequested) return;
        string? result = null;
        try
        {
            if (File.Exists(stage.Result) && new FileInfo(stage.Result).Length < 32) result = File.ReadAllText(stage.Result);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        if (result is null) return;
        stage.Clean();
        var message = result switch
        {
            "installed" => $"Moneta {ProductVersion.ToString(3)} a été installé.",
            "invalid" => "Les contrôles de l’installateur ont échoué. La mise à jour a été refusée.",
            "cancelled" => "L’installation a été annulée.",
            "restart" => "Windows doit redémarrer pour terminer l’installation.",
            _ => "L’installation n’a pas abouti. Vérifiez la version et réessayez depuis le menu d’aide."
        };
        MessageBox.Show(this, message, "Moneta — mise à jour", MessageBoxButton.OK,
            result == "installed" ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
