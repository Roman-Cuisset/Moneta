using System.Diagnostics;
using System.Security.Cryptography;
using Moneta.BankPerfect;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Infrastructure.BankPerfect;

/// <summary>Builds and verifies a new local Moneta document after a caller confirms a prepared preview.</summary>
public static class BankPerfectImporter
{
    private static readonly ActivitySource Timings = new("Moneta.BankPerfect.Import");

    public static BankPerfectImportPreview Prepare(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(source);
        var read = V5Reader.ReadFile(fullPath, cancellationToken);
        using var planning = Timings.StartActivity("preview-plan");
        return BankPerfectPlan.Prepare(fullPath, read, cancellationToken);
    }

    public static BankPerfectImportResult Import(BankPerfectImportPreview preview, string destination,
        CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        using var importing = Timings.StartActivity("import");
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        if (!preview.Report.CanImport || preview.Source is null)
            throw new DocumentException(DocumentError.InvalidData, "Le rapport préalable contient un blocage. Aucun dossier ne peut être créé.");
        if (!string.Equals(Path.GetExtension(destination), ".moneta", StringComparison.OrdinalIgnoreCase))
            throw new DocumentException(DocumentError.InvalidData, "La destination doit porter l’extension .moneta.");

        string? scratch = null;
        try
        {
            var target = MonetaFile.ValidatePath(destination, mustExist: false);
            using var original = new FileStream(preview.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifySource(original, preview.Report);
            BankPerfectTarget plan;
            using (Timings.StartActivity("build-plan"))
                plan = BankPerfectPlan.Build(preview.Source, preview.Report, Guid.NewGuid(), DateTimeOffset.UtcNow, cancellationToken);
            byte[] audit;
            using (Timings.StartActivity("encode-provenance"))
                audit = BankPerfectAudit.Encode(plan, preview.Source, preview.Report);
            var provenanceHash = Convert.ToHexString(SHA256.HashData(audit));
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.Combine(Path.GetDirectoryName(target)!, $".moneta-import-{Guid.NewGuid():N}");
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new DocumentException(DocumentError.DestinationExists, "L’espace temporaire est déjà occupé.");
            Directory.CreateDirectory(directory);
            scratch = directory;
            var candidate = Path.Combine(scratch, "candidate.moneta");
            var backup = Path.Combine(scratch, "backup.moneta");
            var restored = Path.Combine(scratch, "restored.moneta");

            progress?.Report("Écriture du dossier de travail…");
            cancellationToken.ThrowIfCancellationRequested();
            using (Timings.StartActivity("write-candidate"))
            using (MonetaFile.Create(candidate, plan.Document, plan.Operations, plan.Transfers, audit)) { }
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report("Réouverture et comparaison des opérations…");
            FinancialDocument verified;
            MonetaFile opened;
            using (Timings.StartActivity("reopen"))
                opened = MonetaFile.Open(candidate);
            using (opened)
            {
                using (Timings.StartActivity("compare"))
                    Verify(opened, plan, preview.Report, audit, provenanceHash, cancellationToken);
                verified = opened.Snapshot;
                progress?.Report("Sauvegarde de contrôle…");
                cancellationToken.ThrowIfCancellationRequested();
                using (Timings.StartActivity("backup"))
                    MonetaBackup.Create(opened, backup);
            }
            progress?.Report("Restauration et comparaison de contrôle…");
            cancellationToken.ThrowIfCancellationRequested();
            MonetaFile copy;
            using (Timings.StartActivity("restore"))
                copy = MonetaBackup.Restore(backup, restored);
            using (copy)
            using (Timings.StartActivity("compare-restored"))
            {
                Verify(copy, plan, preview.Report, audit, provenanceHash, cancellationToken);
                Require(MonetaBackup.Digest(copy.Snapshot) == MonetaBackup.Digest(verified), "Le dossier restauré diffère du dossier importé.");
            }

            progress?.Report("Vérification de la source et publication…");
            VerifySource(original, preview.Report);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new BankPerfectImportResult(target, verified.Id, provenanceHash, preview.Report, true);
            using (Timings.StartActivity("publish"))
                MonetaFile.PublishVerifiedImport(candidate, target, verified, cancellationToken);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or BankPerfectPlan.PlanException or Microsoft.Data.Sqlite.SqliteException)
        {
            throw MonetaFile.Translate(error, DocumentError.WriteFailed);
        }
        finally
        {
            if (scratch is not null) CleanScratch(scratch);
        }
    }

    private static void VerifySource(FileStream stream, BankPerfectImportReport report)
    {
        stream.Position = 0;
        if (stream.Length != report.Reader.ByteLength ||
            Convert.ToHexString(SHA256.HashData(stream)) != report.SourceSha256)
            throw new DocumentException(DocumentError.VerificationFailed, "La source diffère de celle analysée. Relancez l’analyse.");
    }

    private static void Verify(MonetaFile file, BankPerfectTarget plan, BankPerfectImportReport report,
        byte[] audit, string provenanceHash, CancellationToken cancellationToken)
    {
        var expected = plan.Document;
        var actual = file.Snapshot;
        Require(actual.Id == expected.Id && actual.Name == expected.Name && actual.SchemaVersion == expected.SchemaVersion &&
            actual.CreatedUtc == expected.CreatedUtc && actual.ModifiedUtc == expected.ModifiedUtc &&
            actual.SelectedAccountId == expected.SelectedAccountId && actual.Accounts.SequenceEqual(expected.Accounts) &&
            actual.Currencies.SequenceEqual(expected.Currencies), "Le dossier ou ses comptes diffèrent du plan.");
        Require(actual.Categories.SequenceEqual(expected.Categories) && actual.Payees.SequenceEqual(expected.Payees) &&
            actual.PaymentMethods.SequenceEqual(expected.PaymentMethods), "Les référentiels diffèrent du plan.");
        Require(actual.OperationCount == plan.Operations.Count && actual.TransferCount == plan.Transfers.Count &&
            actual.ReconciliationCount == 0 && actual.ReconciliationEventCount == 0, "Les nombres d’opérations, de virements ou de rapprochements diffèrent du plan.");
        Require(actual.BankPerfectProvenanceHash == provenanceHash &&
            audit.AsSpan().SequenceEqual(file.ReadBankPerfectProvenance()), "La provenance a changé pendant l’écriture.");

        var totals = plan.Objects.Where(mapping => mapping.Type == "account")
            .Select((mapping, index) => (mapping.TargetId, Source: report.Accounts[index]));
        foreach (var (id, source) in totals)
            Require(actual.AccountBalances![id] == source.Balance && actual.AccountPointedBalances![id] == source.PointedBalance &&
                actual.AccountBalances[id] - actual.AccountPointedBalances[id] == source.UnmarkedAmount,
                "Le solde, le pointé ou le non pointé diffèrent de la source.");

        foreach (var batch in plan.Operations.Chunk(128))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = file.ReadTransactions(batch.Select(change => change.Value.Id).ToArray()).ToDictionary(value => value.Id);
            foreach (var change in batch)
                Require(persisted.TryGetValue(change.Value.Id, out var value) && value == change.Value,
                    "Une opération relue diffère du plan approuvé.");
        }
        foreach (var change in plan.Transfers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = change.Value;
            var persisted = file.ReadTransfer(value.Id);
            Require(persisted.Value == value && persisted.Debit.Id == value.DebitTransactionId &&
                persisted.Credit.Id == value.CreditTransactionId && persisted.Fee is null &&
                file.FindTransferId(persisted.Debit.Id) == value.Id && file.FindTransferId(persisted.Credit.Id) == value.Id,
                "Le lien ou les jambes d’un virement relu diffèrent du plan.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DocumentException(DocumentError.VerificationFailed, message);
    }

    private static void CleanScratch(string directory)
    {
        // Delete only leaves allocated by this import. A directory replaced with a reparse point is left untouched.
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var file in new[] { "candidate.moneta", "backup.moneta", "restored.moneta" })
                foreach (var suffix in new[] { "", ".lock", ".manifest.json", "-wal", "-shm", "-journal" })
                    File.Delete(Path.Combine(directory, file + suffix));
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
