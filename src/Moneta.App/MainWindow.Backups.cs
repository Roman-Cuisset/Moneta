using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

public partial class MainWindow
{
    private sealed record BackupKey(Guid DocumentId, string Destination);
    private sealed record BackupJob(BackupKey Key, BackupPolicy Policy, string Source, FinancialDocument Snapshot, BackupKind? Kind);
    private sealed record BackupNotice(BackupVersion? Latest = null, string? Failure = null, string? Outcome = null);

    private readonly BackupLibrary _backupLibrary;
    private readonly Func<BackupPolicy> _readBackupPolicy;
    private readonly Dictionary<BackupKey, BackupNotice> _backupNotices = [];
    private DispatcherTimer _backupTimer = null!;
    private Task _backupWork = Task.CompletedTask;
    private CancellationTokenSource? _backupCancellation;
    private BackupJob? _runningBackup;
    private BackupJob? _queuedBackup;
    private (BackupPolicy Policy, string Path, Guid Id)? _backupContext;
    private bool _stopBackupRequests;

    private void InitializeBackups()
    {
        _backupTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _backupTimer.Tick += (_, _) => { _ = RequestBackup(BackupKind.Daily); };
        _backupTimer.Start();
        Closed += (_, _) => _backupTimer.Stop();
    }

    private void UpdateBackupContext()
    {
        RefreshBackupUi();
        if (_busy || _stopBackupRequests) return;
        if (_workspace.FilePath is not { } path || _workspace.SavedSnapshot is not { } snapshot)
        {
            _backupContext = null;
            return;
        }
        var policy = _readBackupPolicy();
        var next = (policy, path, snapshot.Id);
        if (_backupContext == next) return;
        _backupContext = next;
        // A first Ctrl+S gets the first chance to enqueue its after-save job.
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_backupContext == next)
                _ = RequestBackup(policy.Enabled && policy.Daily ? BackupKind.Daily : null);
        }, DispatcherPriority.Background);
    }

    private Task? RequestBackup(BackupKind? kind)
    {
        if (_stopBackupRequests || _busy || _workspace.FilePath is not { } source ||
            _workspace.SavedSnapshot is not { } snapshot) return null;
        var policy = _readBackupPolicy();
        if (kind is BackupKind.AfterSave && (!policy.Enabled || !policy.AfterSave) ||
            kind is BackupKind.Daily && (!policy.Enabled || !policy.Daily))
            return null;
        var job = new BackupJob(new(snapshot.Id, policy.Destination), policy, source, snapshot, kind);
        if (_runningBackup is not null)
        {
            if (kind == BackupKind.AfterSave || (kind is null && _queuedBackup is null))
                _queuedBackup = job;
            return null;
        }
        _backupWork = ProcessBackupsAsync(job);
        return _backupWork;
    }

    private async Task ProcessBackupsAsync(BackupJob? job)
    {
        while (job is not null)
        {
            var current = job;
            using var cancellation = new CancellationTokenSource();
            _runningBackup = current;
            _backupCancellation = cancellation;
            var notice = Notice(current.Key);
            var failed = false;
            RefreshBackupUi();
            try
            {
                if (current.Kind is { } kind)
                {
                    var result = await Task.Run(() => _backupLibrary.Run(current.Policy, kind, current.Source,
                        current.Snapshot, cancellation.Token));
                    notice = notice with
                    {
                        Latest = result.Latest ?? notice.Latest,
                        Outcome = result.RotationWarning ?? (result.Published is not null ? "Sauvegarde vérifiée et publiée." : null)
                    };
                }
                else
                {
                    var versions = await Task.Run(() => _backupLibrary.History(current.Policy, current.Snapshot.Id));
                    notice = notice with { Latest = versions.FirstOrDefault(entry => entry.Version is not null)?.Version };
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                notice = notice with { Outcome = "Sauvegarde annulée." };
            }
            catch (Exception error) when (error is DocumentException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or InvalidOperationException)
            {
                failed = true;
                var message = current.Kind switch
                {
                    BackupKind.AfterSave => "Dossier enregistré, mais sauvegarde automatique échouée.",
                    BackupKind.Daily => "Sauvegarde quotidienne échouée.",
                    BackupKind.Manual => "Sauvegarde immédiate échouée.",
                    _ => "Historique des sauvegardes indisponible."
                };
                var details = error switch
                {
                    UnauthorizedAccessException => "Accès refusé au répertoire des sauvegardes.",
                    DirectoryNotFoundException => "Répertoire des sauvegardes indisponible.",
                    IOException => "Vérifiez le répertoire de sauvegarde, l’espace disponible ou une sauvegarde déjà en cours.",
                    _ => error.Message
                };
                notice = notice with { Failure = message + " " + details };
            }
            finally
            {
                // Acknowledging a failure while background work was running must not restore it.
                if (!failed) notice = notice with { Failure = Notice(current.Key).Failure };
                _backupNotices[current.Key] = notice;
                _runningBackup = null;
                _backupCancellation = null;
                job = _queuedBackup;
                _queuedBackup = null;
                if (job?.Kind == BackupKind.AfterSave && job.Policy != _readBackupPolicy()) job = null;
                RefreshBackupUi();
            }
        }
    }

    private BackupKey? CurrentBackupKey() => _workspace.SavedSnapshot is { } snapshot
        ? new(snapshot.Id, _readBackupPolicy().Destination) : null;

    private BackupNotice Notice(BackupKey key) => _backupNotices.GetValueOrDefault(key) ?? new();

    private string DescribeBackup()
    {
        if (CurrentBackupKey() is not { } key) return "Ouvrez un dossier enregistré pour consulter ses sauvegardes.";
        var notice = Notice(key);
        var published = notice.Latest is { } date
            ? $"Dernière sauvegarde publiée : {date.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm}."
            : notice.Failure is null ? "Aucune version publiée dans cet emplacement." : "Dernière sauvegarde indisponible.";
        var current = notice.Failure ?? notice.Outcome;
        return current is null ? published : $"{current} {published}";
    }

    private void RefreshBackupUi()
    {
        var key = CurrentBackupKey();
        var state = key is null ? new BackupNotice() : Notice(key);
        var running = _runningBackup?.Kind is BackupKind && _runningBackup.Key == key;
        BackupCancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        BackupCancel.IsEnabled = running && _backupCancellation?.IsCancellationRequested == false;
        BackupStatus.Visibility = key is not null && (running || state.Failure is not null || state.Latest is not null || state.Outcome is not null)
            ? Visibility.Visible : Visibility.Collapsed;
        BackupStatus.Text = running ? "Sauvegarde en cours…" : state.Failure is not null
            ? (state.Failure.StartsWith("Dossier enregistré", StringComparison.Ordinal)
                ? "Dossier enregistré, mais sauvegarde automatique échouée." : "Sauvegarde : consultez l’historique.")
            : state.Outcome ?? (state.Latest is { } latest
                ? $"Sauvegarde publiée le {latest.CreatedUtc.ToLocalTime():dd/MM HH:mm}" : "");
        BackupStatus.ToolTip = DescribeBackup();
    }

    private void BackupCancel_Click(object sender, RoutedEventArgs e)
        => CancelBackup();

    private void CancelBackup()
    {
        _queuedBackup = null;
        _backupCancellation?.Cancel();
        RefreshBackupUi();
    }

    private void ReportBackupFailuresOnExit()
    {
        var failures = _backupNotices.Values.Select(notice => notice.Failure).OfType<string>().Distinct().ToArray();
        if (failures.Length == 0) return;
        MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, failures),
            "Moneta — sauvegardes à vérifier", MessageBoxButton.OK, MessageBoxImage.Warning);
        foreach (var key in _backupNotices.Keys.ToArray())
            _backupNotices[key] = Notice(key) with { Failure = null };
    }

    private void BackupHistory_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_busy && _workspace.SavedSnapshot is not null;
        e.Handled = true;
    }

    private void BackupHistory_Executed(object sender, ExecutedRoutedEventArgs e) => ShowBackupHistory();

    private void ShowBackupHistory()
    {
        if (CurrentBackupKey() is not { } key) return;
        var description = DescribeBackup();
        var state = Notice(key);
        _backupNotices[key] = state with { Failure = null };
        RefreshBackupUi();
        var window = new BackupHistoryWindow(_backupLibrary, _readBackupPolicy(), key.DocumentId, description,
            () => RequestBackup(BackupKind.Manual), CancelBackup, DescribeBackup)
        {
            Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(window => window.IsActive) ?? (Window)this
        };
        window.ShowDialog();
    }
}
