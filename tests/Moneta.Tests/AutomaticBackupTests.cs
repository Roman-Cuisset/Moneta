using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Infrastructure.BankPerfect;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class AutomaticBackupTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2025, 8, 3, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateOnly Day = new(2025, 7, 9);
    private string root = null!;
    private string Source => Path.Combine(root, "Dossier.moneta");
    private BackupPolicy Policy => new() { Enabled = true, DirectoryPath = Path.Combine(root, "copie externe") };
    private readonly Clock clock = new();

    [TestInitialize]
    public void Initialize() => root = Directory.CreateTempSubdirectory("Moneta-Versions-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, recursive: true);

    [TestMethod]
    public void PolicyPersistsAndLegacyPreferencesKeepAutomationOff()
    {
        var path = Path.Combine(root, "options.json");
        File.WriteAllText(path, """
            {"SchemaVersion":2,"Theme":"Dark","CheckUpdatesAtStartup":false,"RefreshRatesAtStartup":false,"RateSource":"NBRB"}
            """);
        var previous = new SettingsStore(path);
        previous.Load();
        Assert.IsFalse(previous.Current.Backups.Enabled);
        var changed = Policy with { AfterSave = false, Daily = true, IntervalMinutes = 61, VersionsToKeep = 7 };
        previous.Save(previous.Current with { Backups = changed });
        var reopened = new SettingsStore(path);
        reopened.Load();
        Assert.AreEqual(changed, reopened.Current.Backups);
        Assert.AreEqual("Dark", reopened.Current.Theme);
        var bytes = File.ReadAllBytes(path);
        Assert.ThrowsException<SettingsException>(() => reopened.Save(reopened.Current with
        { Backups = changed with { VersionsToKeep = 0 } }));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void CadenceUsesPublishedGenerationsAcrossRestartsAndManualBypassesLimits()
    {
        using var source = MonetaFile.Create(Source, FinancialDocument.New("Dossier fictif"));
        var snapshot = source.Snapshot;
        var library = new BackupLibrary(clock);
        Assert.IsNull(library.Run(Policy with { Enabled = false }, BackupKind.AfterSave, Source, snapshot).Published);
        Assert.IsFalse(Directory.Exists(Policy.Destination));
        var first = library.Run(Policy, BackupKind.AfterSave, Source, snapshot).Published!;
        var restarted = new BackupLibrary(clock);
        Assert.IsNull(restarted.Run(Policy, BackupKind.Daily, Source, snapshot).Published);
        clock.Now = clock.Now.AddMinutes(29);
        Assert.IsNull(restarted.Run(Policy, BackupKind.AfterSave, Source, snapshot).Published);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.IsNotNull(restarted.Run(Policy, BackupKind.AfterSave, Source, snapshot).Published);
        var manual = restarted.Run(Policy with { Enabled = false }, BackupKind.Manual, Source, snapshot).Published!;
        Assert.AreEqual(BackupKind.Manual, manual.Kind);
        Assert.AreNotEqual(first.SnapshotId, manual.SnapshotId);
        clock.Now = clock.Now.AddDays(1);
        Assert.AreEqual(BackupKind.Daily, restarted.Run(Policy, BackupKind.Daily, Source, snapshot).Published!.Kind);
        Assert.AreEqual(4, restarted.History(Policy, snapshot.Id).Count);
    }

    [TestMethod]
    public void DamagedLatestVersionDoesNotThrottleReplacement()
    {
        using var source = MonetaFile.Create(Source, FinancialDocument.New("Contrôle d’empreinte"));
        var library = new BackupLibrary(clock);
        var initial = library.Run(Policy, BackupKind.AfterSave, Source, source.Snapshot).Published!;
        var entry = library.History(Policy, source.Snapshot.Id).Single();
        using (var stream = File.Open(Archive(entry), FileMode.Open, FileAccess.Write))
            stream.WriteByte(0);
        var next = library.Run(Policy, BackupKind.AfterSave, Source, source.Snapshot).Published!;
        Assert.AreNotEqual(initial.SnapshotId, next.SnapshotId);
        Assert.AreEqual(2, library.History(Policy, source.Snapshot.Id).Count);
    }

    [TestMethod]
    public void FailedDestinationLeavesCommitAndDirtyDraftIsNotBackedUp()
    {
        using var workspace = SimpleWorkspace();
        var model = workspace.Model!;
        var committed = model.AddTransaction(new(Day, 18m, TransactionDirection.Credit, "Enregistrée"));
        workspace.Save();
        var snapshot = workspace.SavedSnapshot!;
        model.AddTransaction(new(Day.AddDays(1), 3m, TransactionDirection.Debit, "Brouillon"));
        var blocked = Path.Combine(root, "indisponible");
        File.WriteAllText(blocked, "fichier sentinelle");
        var library = new BackupLibrary(clock);
        Assert.ThrowsException<IOException>(() => library.Run(Policy with { DirectoryPath = blocked }, BackupKind.AfterSave, Source, snapshot));
        Assert.AreEqual("fichier sentinelle", File.ReadAllText(blocked));
        Assert.IsTrue(model.IsDirty);
        Assert.AreEqual(1L, MonetaFile.Inspect(Source).OperationCount);
        var published = library.Run(Policy, BackupKind.AfterSave, Source, snapshot).Published!;
        var inspection = library.Verify(Policy, snapshot.Id, published.SnapshotId);
        Assert.AreEqual(1L, inspection.Snapshot.OperationCount);
        Assert.IsTrue(model.IsDirty);
        var target = Path.Combine(root, "Restauré.moneta");
        library.Restore(Policy, snapshot.Id, published.SnapshotId, target);
        using var reopened = MonetaFile.Open(target);
        var restored = reopened.ReadPage(reopened.Snapshot.Accounts[0], 0, 100, []).Transactions.Single();
        Assert.AreEqual(committed.Id, restored.Id);
    }

    [TestMethod]
    public void SameNamesAndEqualUtcDoNotCollideAndExistingRestoreTargetIsPreserved()
    {
        var otherFolder = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        using var one = MonetaFile.Create(Source, FinancialDocument.New("Même nom"));
        using var two = MonetaFile.Create(Path.Combine(otherFolder, Path.GetFileName(Source)), FinancialDocument.New("Même nom"));
        var library = new BackupLibrary(clock);
        var first = library.Run(Policy, BackupKind.Manual, one.FilePath, one.Snapshot).Published!;
        var next = library.Run(Policy, BackupKind.Manual, one.FilePath, one.Snapshot).Published!;
        var other = library.Run(Policy, BackupKind.Manual, two.FilePath, two.Snapshot).Published!;
        Assert.AreEqual(first.CreatedUtc, other.CreatedUtc);
        Assert.AreNotEqual(first.SnapshotId, next.SnapshotId);
        Assert.AreEqual(2, library.History(Policy, one.Snapshot.Id).Count);
        Assert.AreEqual(other, library.History(Policy, two.Snapshot.Id).Single().Version);
        Assert.ThrowsException<DocumentException>(() => library.Restore(Policy, one.Snapshot.Id, first.SnapshotId, Source));
        Assert.AreEqual(one.Snapshot.Id, MonetaFile.Inspect(Source).Id);
    }

    [TestMethod]
    public void RotationKeepsValidCoverageAndPreservesCorruptUnknownAndLockedVersions()
    {
        using var source = MonetaFile.Create(Source, FinancialDocument.New("Rotation fictive"));
        var id = source.Snapshot.Id;
        var library = new BackupLibrary(clock);
        BackupHistoryEntry Make(BackupPolicy policy)
        {
            clock.Now = clock.Now.AddMinutes(1);
            var generation = library.Run(policy, BackupKind.Manual, Source, source.Snapshot).Published!;
            return library.History(policy, id).Single(entry => entry.SnapshotId == generation.SnapshotId);
        }
        var retired = Make(Policy);
        var locked = Make(Policy);
        var corrupt = Make(Policy);
        var unknown = Make(Policy);
        File.AppendAllText(Archive(corrupt), "corruption d’essai");
        File.WriteAllText(Path.Combine(unknown.DirectoryPath, "document-utilisateur.txt"), "à conserver");
        var retained = Make(Policy);
        using (File.Open(Archive(locked), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var fresh = Make(Policy with { VersionsToKeep = 2 });
            Assert.IsTrue(Directory.Exists(fresh.DirectoryPath));
            Assert.IsTrue(Directory.Exists(retained.DirectoryPath));
            Assert.IsFalse(Directory.Exists(retired.DirectoryPath));
            Assert.IsTrue(Directory.Exists(locked.DirectoryPath));
            Assert.IsTrue(Directory.Exists(corrupt.DirectoryPath));
            Assert.IsTrue(File.Exists(Path.Combine(unknown.DirectoryPath, "document-utilisateur.txt")));
            Assert.ThrowsException<DocumentException>(() => library.Verify(Policy, id, corrupt.SnapshotId));
        }
        var last = Make(Policy with { VersionsToKeep = 1 });
        Assert.IsFalse(Directory.Exists(locked.DirectoryPath));
        Assert.AreEqual(id, library.Verify(Policy, id, last.SnapshotId).Snapshot.Id);
        Assert.IsTrue(Directory.Exists(corrupt.DirectoryPath));
    }

    [TestMethod]
    public void InterruptionsAndSimulatedDiskErrorsCannotPublishOrRemoveThePreviousVersion()
    {
        using var source = MonetaFile.Create(Source, FinancialDocument.New("Interruptions fictives"));
        var options = Policy with { VersionsToKeep = 1 };
        var library = new BackupLibrary(clock);
        var baseline = library.Run(options, BackupKind.Manual, Source, source.Snapshot).Published!;
        foreach (var phase in new[] { BackupPhase.Capture, BackupPhase.Verification, BackupPhase.Publication })
        {
            using var stop = new CancellationTokenSource();
            Assert.ThrowsException<OperationCanceledException>(() =>
                library.Run(options, BackupKind.Manual, Source, source.Snapshot, stop.Token,
                    value => { if (value == phase) stop.Cancel(); }));
            Assert.AreEqual(baseline, library.History(options, source.Snapshot.Id).Single().Version);
        }
        foreach (var failure in new Exception[] { new IOException("Disque plein simulé", unchecked((int)0x80070070)),
                     new UnauthorizedAccessException("Écriture refusée (simulation)") })
            Assert.ThrowsException<DocumentException>(() =>
                library.Run(options, BackupKind.Manual, Source, source.Snapshot,
                    phase: value => { if (value == BackupPhase.Verification) throw failure; }));
        Assert.AreEqual(baseline, library.History(options, source.Snapshot.Id).Single().Version);
        using var cancelRotation = new CancellationTokenSource();
        var published = library.Run(options, BackupKind.Manual, Source, source.Snapshot, cancelRotation.Token,
            value => { if (value == BackupPhase.Rotation) cancelRotation.Cancel(); });
        Assert.IsNotNull(published.Published);
        Assert.IsNotNull(published.RotationWarning);
        Assert.AreEqual(2, library.History(options, source.Snapshot.Id).Count);
    }

    [TestMethod]
    public async Task SerializesConcurrentVersionsAndDoesNotBackUpAnUnexpectedCommit()
    {
        using var workspace = SimpleWorkspace();
        var model = workspace.Model!;
        var old = workspace.SavedSnapshot!;
        var library = new BackupLibrary(clock);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var task = Task.Run(() => library.Run(Policy, BackupKind.Manual, Source, old, phase: phase =>
        {
            if (phase != BackupPhase.Capture) return;
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(20)));
        }));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(20)));
            Assert.ThrowsException<IOException>(() => new BackupLibrary(clock).Run(Policy, BackupKind.Manual, Source, old));
            model.AddTransaction(new(Day, 100, TransactionDirection.Credit, "Autre commit"));
            workspace.Save();
        }
        finally { release.Set(); }
        var error = await Assert.ThrowsExceptionAsync<DocumentException>(async () => await task);
        Assert.AreEqual(DocumentError.VerificationFailed, error.Error);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(0, library.History(Policy, old.Id).Count);
        for (var index = 0; index < 4; index++)
        {
            var result = library.Run(Policy, BackupKind.Manual, Source, workspace.SavedSnapshot!).Published!;
            var version = library.Verify(Policy, old.Id, result.SnapshotId);
            using var releasedFile = File.Open(version.Backup.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }

    [TestMethod]
    public void CommitAfterCaptureDoesNotChangeThePublishedVersion()
    {
        using var workspace = SimpleWorkspace();
        var snapshot = workspace.SavedSnapshot!;
        var library = new BackupLibrary(clock);
        var result = library.Run(Policy, BackupKind.AfterSave, Source, snapshot, phase: phase =>
        {
            if (phase != BackupPhase.Verification) return;
            workspace.Model!.AddTransaction(new(Day, 42, TransactionDirection.Credit, "Commit suivant"));
            workspace.Save();
        });
        Assert.AreEqual(1L, workspace.SavedSnapshot!.OperationCount);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        Assert.AreEqual(snapshot.LedgerDigest, library.Verify(Policy, snapshot.Id, result.Published!.SnapshotId).Snapshot.LedgerDigest);
    }

    [TestMethod]
    public void ImportedAndArchivedDossierRestoresProvenanceAndFinancialLinks()
    {
        var oldSource = Path.Combine(root, "origine.bp");
        File.WriteAllBytes(oldSource, BankPerfectV5Tests.Encode("""
            utilisateur=Origine fictive
            guid=source-fictive
            compte0=;;;Import fictif;;1;€;6,55957;F;;;;;;real;UID-LEGACY;
            TNSLPPBNTSOGPTQBC280673
            """));
        BankPerfectImporter.Import(BankPerfectImporter.Prepare(oldSource), Source);
        byte[] provenance;
        using (var stored = MonetaFile.Open(Source)) provenance = stored.ReadBankPerfectProvenance()!;
        using var workspace = new DocumentWorkspace();
        workspace.Open(Source);
        var model = workspace.Model!;
        var eur = model.Accounts[0];
        var byn = model.AddAccount("BYN", model.CurrencyRegistry.Get("BYN"), null, 0, null);
        var rub = model.AddAccount("RUB", model.CurrencyRegistry.Get("RUB"), null, 0, null);
        model.SelectedAccount = eur;
        var reconciled = model.AddTransaction(new(Day, 20m, TransactionDirection.Credit, "Relevé fictif"));
        var transfer = model.CreateTransfer(new(eur.Account.Id, byn.Account.Id, Day.AddDays(1), 2m, 6m,
            "Lien fictif", "", FeeAccountId: rub.Account.Id, FeeAmount: 3m));
        workspace.Save();
        model.StageReconciliation(new(Day, 20m), [reconciled.Id]);
        workspace.Save();
        model.SetAccountArchived(byn.Account.Id, true);
        workspace.Save();
        var committed = workspace.SavedSnapshot!;
        var library = new BackupLibrary(clock);
        var archive = library.Run(Policy, BackupKind.Daily, Source, committed).Published!;
        var target = Path.Combine(root, "ancienne version.moneta");
        library.Restore(Policy, committed.Id, archive.SnapshotId, target);
        using var restored = MonetaFile.Open(target);
        Assert.AreEqual(committed.LedgerDigest, restored.Snapshot.LedgerDigest);
        Assert.AreEqual(committed.BankPerfectProvenanceHash, restored.Snapshot.BankPerfectProvenanceHash);
        CollectionAssert.AreEqual(provenance, restored.ReadBankPerfectProvenance());
        CollectionAssert.AreEqual(committed.Accounts.ToArray(), restored.Snapshot.Accounts.ToArray());
        Assert.AreEqual(transfer, model.GetTransfer(transfer.Value.Id));
        Assert.AreEqual(1L, restored.Snapshot.ReconciliationCount);
        var modelRestored = new Moneta.Core.Presentation.MainViewModel(restored.Snapshot, restored);
        Assert.AreEqual(transfer, modelRestored.GetTransfer(transfer.Value.Id));
        Assert.IsFalse(workspace.HasUnsavedChanges);
    }

    private DocumentWorkspace SimpleWorkspace()
    {
        var workspace = new DocumentWorkspace();
        workspace.New("Dossier fictif");
        var model = workspace.Model!;
        model.AddAccount("Courant", model.CurrencyRegistry.Get("EUR"), null, 0m, null);
        workspace.Save(Source);
        return workspace;
    }

    private static string Archive(BackupHistoryEntry entry) => Path.Combine(entry.DirectoryPath, "dossier.moneta");
}
