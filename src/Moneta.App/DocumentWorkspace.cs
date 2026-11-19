using System.ComponentModel;
using System.IO;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure;
using Moneta.Infrastructure.Storage;

namespace Moneta.App;

// Owns one folder at a time; the view asks save/discard/cancel before replacing it.
public sealed class DocumentWorkspace : IDisposable
{
    private MonetaFile? _file;
    public MainViewModel? Model { get; private set; }
    public string? FilePath => _file?.FilePath;
    internal FinancialDocument? SavedSnapshot => _file?.Snapshot;
    public bool CanSave => Model is { IsDemo: false };
    public bool HasUnsavedChanges => CanSave && (_file is null || Model!.IsDirty);
    public bool CanBackup => _file is not null && CanSave;
    public event EventHandler? Changed;

    public void New(string name) => Replace(new MainViewModel(FinancialDocument.New(name)), null);
    public void OpenDemo() => Replace(new MainViewModel(DemoData.Create()), null);

    public void Open(string path)
    {
        if (_file is not null && string.Equals(Path.GetFullPath(path), _file.FilePath, StringComparison.OrdinalIgnoreCase)) return;
        var candidate = MonetaFile.Open(path);
        try { Replace(CreateModel(candidate), candidate); }
        catch { candidate.Dispose(); throw; }
    }

    public void OpenMigratedCopy(string source, string destination)
    {
        var target = Path.GetFullPath(destination);
        var recovery = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + ".avant-migration.moneta");
        var candidate = MonetaBackup.Migrate(source, target, recovery);
        try { Replace(CreateModel(candidate), candidate); }
        catch { candidate.Dispose(); throw; }
    }

    public void Save(string? newPath = null)
    {
        if (Model is not { IsDemo: false } model)
            throw new InvalidOperationException("Le mode démonstration ne peut pas être enregistré. Créez un nouveau dossier vierge.");
        var snapshot = model.CaptureDocument();
        if (_file is null || newPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
            var candidate = _file is null
                ? MonetaFile.Create(newPath, snapshot, model.PendingChanges, model.PendingTransfers)
                : MonetaBackup.SaveAs(_file, newPath, snapshot, model.PendingChanges, model.PendingReconciliations,
                    model.PendingTransfers);
            try { _file?.Close(); }
            catch { candidate.Dispose(); throw; }
            _file = candidate;
        }
        else _file.Save(snapshot, model.PendingChanges, model.PendingReconciliations, model.PendingTransfers);
        model.SetLedgerSource(_file);
        model.AcceptSaved(_file.Snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public BackupResult Backup(string destination)
    {
        if (_file is null || HasUnsavedChanges)
            throw new InvalidOperationException("Enregistrez les modifications avant de sauvegarder le dossier.");
        return MonetaBackup.Create(_file, destination);
    }

    public static void Restore(string source, string destination)
    {
        // Restore returns a reopened, verified folder. The current workspace is untouched.
        using var restored = MonetaBackup.Restore(source, destination);
    }

    private static MainViewModel CreateModel(MonetaFile file)
    {
        var model = new MainViewModel(file.Snapshot, file);
        model.SelectedAccount ??= model.VisibleAccounts.FirstOrDefault();
        return model;
    }

    private void Replace(MainViewModel model, MonetaFile? file)
    {
        _file?.Close();
        if (Model is not null) Model.PropertyChanged -= ModelChanged;
        _file = file;
        Model = model;
        Model.PropertyChanged += ModelChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDirty)) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _file?.Close();
}
