using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class BackupHistoryWindow : Window
{
    private sealed class VersionRow(BackupHistoryEntry entry) : INotifyPropertyChanged
    {
        public BackupHistoryEntry Entry { get; } = entry;
        public string Date => Entry.Version?.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        public string Kind => Entry.Version?.Kind switch
        {
            BackupKind.AfterSave => "Après Ctrl+S", BackupKind.Daily => "Quotidienne",
            BackupKind.Manual => "Manuelle", _ => "—"
        };
        public string Format => Entry.Version is { } version ? $"{version.FormatVersion} / {version.SchemaVersion}" : "—";
        public string Size => Entry.Version is { } version ? $"{version.Length:N0} octets" : "—";
        public string Status { get; private set; } = entry.Status;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Report(string status)
        {
            Status = status;
            PropertyChanged?.Invoke(this, new(nameof(Status)));
        }
    }

    private readonly BackupLibrary _library;
    private readonly BackupPolicy _policy;
    private readonly Guid _document;
    private readonly Func<Task?> _saveNow;
    private readonly Action _cancelBackup;
    private readonly Func<string> _describeBackup;
    private bool _busy;

    public BackupHistoryWindow(BackupLibrary library, BackupPolicy policy, Guid document, string notice,
        Func<Task?> saveNow, Action cancelBackup, Func<string> describeBackup)
    {
        _library = library;
        _policy = policy;
        _document = document;
        _saveNow = saveNow;
        _cancelBackup = cancelBackup;
        _describeBackup = describeBackup;
        InitializeComponent();
        Notice.Text = notice;
        DestinationText.Text = policy.Destination;
        Loaded += async (_, _) => await RunAsync(LoadVersionsAsync, "Lecture de l’historique…");
        UpdateActions();
    }

    private async Task LoadVersionsAsync()
    {
        var entries = await Task.Run(() => _library.History(_policy, _document));
        Versions.ItemsSource = entries.Select(entry => new VersionRow(entry)).ToArray();
        HistoryState.Text = entries.Count == 0 ? "Aucune version publiée dans cet emplacement."
            : $"{entries.Count} version(s). Vérifiez une version avant de l’utiliser.";
        if (entries.Count > 0) Versions.SelectedIndex = 0;
    }

    private async Task RunAsync(Func<Task> work, string message)
    {
        if (_busy) return;
        _busy = true;
        UpdateActions();
        HistoryState.Text = message;
        try { await work(); }
        catch (Exception error) when (IsBackupError(error))
        {
            HistoryState.Text = error.Message;
        }
        finally { _busy = false; UpdateActions(); }
    }

    private static bool IsBackupError(Exception error) =>
        error is IOException or UnauthorizedAccessException or DocumentException or ArgumentException or JsonException or NotSupportedException;

    private async Task<BackupInspection> VerifyAsync(VersionRow row)
    {
        try
        {
            var checkedBackup = await Task.Run(() => _library.Verify(_policy, _document, row.Entry.SnapshotId));
            row.Report("Vérifiée · SHA-256 et dossier SQLite concordants");
            return checkedBackup;
        }
        catch (Exception error) when (IsBackupError(error))
        {
            var status = error is DocumentException { Error: DocumentError.VerificationFailed or DocumentError.Corrupt or
                DocumentError.InvalidFormat or DocumentError.UnsupportedVersion } ? "Invalide" : "Non vérifiée";
            row.Report(status + " · " + error.Message);
            throw;
        }
    }

    private void UpdateActions()
    {
        Manual.IsEnabled = Reload.IsEnabled = !_busy;
        Verify.IsEnabled = Restore.IsEnabled = !_busy && Versions.SelectedItem is VersionRow { Entry.Version: not null };
    }

    private void Versions_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private async void Reload_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadVersionsAsync, "Lecture de l’historique…");

    private async void Manual_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var work = _saveNow();
            if (work is null)
            {
                HistoryState.Text = "Une autre sauvegarde est en cours. Réessayez après sa fin.";
                return;
            }
            CancelBackup.Visibility = Visibility.Visible;
            CancelBackup.IsEnabled = true;
            try { await work; }
            finally { CancelBackup.Visibility = Visibility.Collapsed; }
            Notice.Text = _describeBackup();
            await LoadVersionsAsync();
        }, "Sauvegarde du dernier enregistrement…");
    }

    private void CancelBackup_Click(object sender, RoutedEventArgs e)
    {
        CancelBackup.IsEnabled = false;
        _cancelBackup();
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is not VersionRow row) return;
        await RunAsync(async () =>
        {
            var inspection = await VerifyAsync(row);
            HistoryState.Text = $"Manifeste {inspection.ManifestVersion}. {inspection.Snapshot.Accounts.Count} compte(s), " +
                $"{inspection.Snapshot.OperationCount} opération(s) et {inspection.Snapshot.TransferCount} virement(s).";
        }, "Vérification de la version…");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is not VersionRow row) return;
        await RunAsync(async () =>
        {
            var verified = await VerifyAsync(row);
            var snapshot = verified.Snapshot;
            HistoryState.Text = $"Version vérifiée. {snapshot.Accounts.Count} compte(s) et {snapshot.OperationCount} opération(s).";
            var dialog = new SaveFileDialog
            {
                Title = "Restaurer dans un nouveau fichier", Filter = "Dossier Moneta (*.moneta)|*.moneta",
                DefaultExt = ".moneta", AddExtension = true, OverwritePrompt = false,
                CheckPathExists = true, FileName = "Dossier restauré.moneta"
            };
            if (dialog.ShowDialog(this) != true) return;
            var details = $"Restaurer « {snapshot.Name} » ({row.Date}, schéma {snapshot.SchemaVersion}) ?\n" +
                $"{snapshot.Accounts.Count} compte(s), {snapshot.OperationCount} opération(s), {snapshot.TransferCount} virement(s).\n\n" +
                $"La copie sera créée et vérifiée dans le fichier suivant.\n{dialog.FileName}";
            if (MessageBox.Show(this, details, "Moneta — restauration sur copie", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            HistoryState.Text = "Restauration et réouverture de la copie…";
            await Task.Run(() => _library.Restore(_policy, _document, row.Entry.SnapshotId, dialog.FileName));
            HistoryState.Text = "Nouvelle copie restaurée et vérifiée. Ouvrez-la depuis Fichier → Ouvrir.";
        }, "Vérification de la version avant restauration…");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }
}
