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
        public string Date => Entry.Version?.CreatedUtc.ToLocalTime().ToString(L.DatePattern + " HH:mm:ss", L.Culture) ?? "—";
        public string Kind => Entry.Version?.Kind switch
        {
            BackupKind.AfterSave => L.Get("BackupHistoryWindow.AfterCtrlS"), BackupKind.Daily => "Quotidienne",
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
        Loaded += async (_, _) => await RunAsync(LoadVersionsAsync, L.Get("BackupHistoryWindow.ReadingHistory"));
        UpdateActions();
    }

    private async Task LoadVersionsAsync()
    {
        var entries = await Task.Run(() => _library.History(_policy, _document));
        Versions.ItemsSource = entries.Select(entry => new VersionRow(entry)).ToArray();
        HistoryState.Text = entries.Count == 0 ? L.Get("Common.NoVersionPublishedInThisLocation")
            : L.Format("BackupHistoryWindow.0VersionSCheckAVersionBeforeUsing", (entries.Count));
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
            row.Report(L.Get("BackupHistoryWindow.VerifiedSha256AndSqliteFile"));
            return checkedBackup;
        }
        catch (Exception error) when (IsBackupError(error))
        {
            var status = error is DocumentException { Error: DocumentError.VerificationFailed or DocumentError.Corrupt or
                DocumentError.InvalidFormat or DocumentError.UnsupportedVersion } ? "Invalide" : L.Get("BackupHistoryWindow.NotVerified");
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
    private async void Reload_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadVersionsAsync, L.Get("BackupHistoryWindow.ReadingHistory"));

    private async void Manual_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var work = _saveNow();
            if (work is null)
            {
                HistoryState.Text = L.Get("BackupHistoryWindow.AnotherBackupIsUnderWayTryAgainAfter");
                return;
            }
            CancelBackup.Visibility = Visibility.Visible;
            CancelBackup.IsEnabled = true;
            try { await work; }
            finally { CancelBackup.Visibility = Visibility.Collapsed; }
            Notice.Text = _describeBackup();
            await LoadVersionsAsync();
        }, L.Get("BackupHistoryWindow.BackupOfLastRecord"));
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
            HistoryState.Text = L.Format("BackupHistory.ManifestAccounts", inspection.ManifestVersion, inspection.Snapshot.Accounts.Count) +
                L.Format("BackupHistoryWindow.0TransactionsAnd1Transfers", (inspection.Snapshot.OperationCount), (inspection.Snapshot.TransferCount));
        }, L.Get("BackupHistoryWindow.CheckingTheVersion"));
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is not VersionRow row) return;
        await RunAsync(async () =>
        {
            var verified = await VerifyAsync(row);
            var snapshot = verified.Snapshot;
            HistoryState.Text = L.Format("BackupHistoryWindow.VerifiedVersion0AccountSAnd1Transaction", (snapshot.Accounts.Count), (snapshot.OperationCount));
            var dialog = new SaveFileDialog
            {
                Title = L.Get("BackupHistoryWindow.RestoreToANewFile"), Filter = L.Get("Common.DocumentMonetaMonetaMoneta"),
                DefaultExt = ".moneta", AddExtension = true, OverwritePrompt = false,
                CheckPathExists = true, FileName = L.Get("Common.DocumentRestoredMoneta")
            };
            if (dialog.ShowDialog(this) != true) return;
            var details = L.Format("BackupHistoryWindow.Restore01Schema2", (snapshot.Name), (row.Date), (snapshot.SchemaVersion)) +
                L.Format("BackupHistoryWindow.0AccountS1TransactionS2Transfer", (snapshot.Accounts.Count), (snapshot.OperationCount), (snapshot.TransferCount)) +
                L.Format("BackupHistoryWindow.TheCopyWillBeCreatedAndVerifiedIn", (dialog.FileName));
            if (MessageBox.Show(this, details, L.Get("BackupHistoryWindow.MonetaRestorationByCopy"), MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            HistoryState.Text = L.Get("BackupHistoryWindow.RestoringAndReopeningTheCopy");
            await Task.Run(() => _library.Restore(_policy, _document, row.Entry.SnapshotId, dialog.FileName));
            HistoryState.Text = L.Get("BackupHistoryWindow.NewCopyRestoredAndVerifiedOpenItFrom");
        }, L.Get("BackupHistoryWindow.CheckVersionBeforeRestoration"));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }
}
