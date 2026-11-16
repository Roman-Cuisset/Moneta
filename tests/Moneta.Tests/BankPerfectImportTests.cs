using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.BankPerfect;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class BankPerfectImportTests
{
    private const string Separator = "TNSLPPBNTSOGPTQBC280673";
    private const string Header = "utilisateur=Corpus fictif\nguid=uid-source\ncompte0=;;;Courant;;1;€;6,55957;F;;;;;;real;A;";
    private const string SecondAccount = "compte1=;;;Épargne;;1;€;6,55957;F;;;;;;real;B;";
    private const string Opening = "46337;R;40;Création du compte courant;;-1;1000,00;46337;UId=1";
    private const string Income = "45306;;40;Revenu Démo;Recette d'étude;34;100,01;45306;UId=2";
    private const string Expense = "45307;;20;Café Démo;Été à Noël;30;-12,34;45307;UId=3";
    private const string Catalog = "\ncateg=30=Vie;34=     Énergie";
    private string directory = null!;
    private string Source => Path.Combine(directory, "source.bp");
    private string Target => Path.Combine(directory, "result.moneta");

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("MonetaImport-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void EmptyOpeningPreservesMissingHistoricalIdsAndLegacyReconciliation()
    {
        var preview = Prepare(Header.Replace(";A;", "").Replace("\nguid=uid-source", "") + "\ncateg=Racine;     Enfant",
            "46337;R;40;Création du compte courant;;-1;0,00");
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "missing-operation-uid" && !issue.Blocking));
        var before = Hash(Source);

        var result = BankPerfectImporter.Import(preview, Target);
        Assert.IsTrue(result.BackupRestoreVerified);
        using (var file = MonetaFile.Open(Target))
        {
            var account = file.Snapshot.Accounts.Single();
            var row = Operations(file, account).Single();
            Assert.AreEqual(0m, account.OpeningBalance);
            Assert.AreEqual(0m, row.Amount);
            Assert.AreEqual(ReconciliationStatus.Reconciled, row.Status);
            Assert.IsNull(row.MarkedOn);
            Assert.IsNull(row.ReconciliationId);
            Assert.AreEqual(0L, file.Snapshot.ReconciliationCount);
            using var audit = JsonDocument.Parse(file.ReadBankPerfectProvenance()!);
            var objects = audit.RootElement.GetProperty("objects").EnumerateArray().ToArray();
            foreach (var kind in new[] { "document", "account", "operation" })
                Assert.AreEqual(JsonValueKind.Null, objects.Single(item => item.GetProperty("type").GetString() == kind).GetProperty("sourceId").ValueKind);
            var entry = objects.Single(item => item.GetProperty("type").GetString() == "operation");
            Assert.AreEqual(row.Id, entry.GetProperty("targetId").GetGuid());
            Assert.IsTrue(entry.GetProperty("opening").GetBoolean());
            Assert.AreEqual(result.ProvenanceSha256, file.Snapshot.BankPerfectProvenanceHash);
            Assert.AreEqual(before, audit.RootElement.GetProperty("sourceSha256").GetString());

            Assert.ThrowsException<DocumentException>(() => file.Save(file.Snapshot,
                [new(row with { Amount = 1m, Revision = 2 }, 1)]));
            var model = new MainViewModel(file.Snapshot, file);
            model.ReleaseLegacyReconciliation(new(row.Id, row.Revision), "Libération explicitement motivée.");
            file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations);
        }
        using var reopened = MonetaFile.Open(Target);
        Assert.AreEqual(1L, reopened.Snapshot.ReconciliationEventCount);
        Assert.AreEqual(0L, reopened.Snapshot.ReconciliationCount);
        Assert.AreEqual(ReconciliationStatus.Unmarked, Operations(reopened, reopened.Snapshot.Accounts[0]).Single().Status);
        Assert.AreEqual(result.ProvenanceSha256, reopened.Snapshot.BankPerfectProvenanceHash);
        Assert.AreEqual(before, Hash(Source));
        NoScratch();
    }

    [TestMethod]
    public void SingleAccountMapsCatalogDatesSignedAmountsAndPreservesPointingProvenance()
    {
        var marked = Income.Replace("45306;;", "45306;P;").Replace(";45306;UId", ";45322;UId");
        var preview = Prepare(Header + Catalog + "\nmodes=-Carte spéciale\nwho0=Modèle café;0;30;Café Démo;Texte;12,34",
            marked + "\n" + Expense + "\n" + Opening);
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        Assert.AreEqual(1087.67m, preview.Report.Accounts[0].Balance);
        Assert.AreEqual(1100.01m, preview.Report.Accounts[0].PointedBalance);
        Assert.AreEqual(-12.34m, preview.Report.Accounts[0].UnmarkedAmount);

        BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        var account = file.Snapshot.Accounts.Single();
        var rows = Operations(file, account);
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual(0m, account.OpeningBalance);
        Assert.AreEqual(1087.67m, file.Snapshot.AccountBalances![account.Id]);
        Assert.AreEqual(1100.01m, file.Snapshot.AccountPointedBalances![account.Id]);
        var credit = rows.Single(row => row.Label == "Revenu Démo");
        Assert.AreEqual(100.01m, FinancialRules.SignedAmount(credit));
        Assert.AreEqual(new DateOnly(2024, 1, 15), credit.Date);
        Assert.AreEqual("Vie", credit.Category.Name);
        Assert.AreEqual("Énergie", credit.Category.Subcategory);
        Assert.AreEqual(ReconciliationStatus.Marked, credit.Status);
        Assert.IsNull(credit.MarkedOn);
        var debit = rows.Single(row => row.Label == "Café Démo");
        Assert.AreEqual(-12.34m, FinancialRules.SignedAmount(debit));
        Assert.AreEqual("Été à Noël", debit.Details);
        Assert.AreEqual("Carte", debit.PaymentMode);
        Assert.AreEqual("Café Démo", debit.Payee.Name);
        using var audit = JsonDocument.Parse(file.ReadBankPerfectProvenance()!);
        Assert.AreEqual("45322", audit.RootElement.GetProperty("objects").EnumerateArray()
            .Single(item => item.GetProperty("targetId").GetGuid() == credit.Id).GetProperty("raw")[7].GetString());
        Assert.IsTrue(file.Snapshot.PaymentMethods.Any(method => method.Name == "Carte spéciale"));
    }

    [TestMethod]
    public void LinkedTransferAndProvenanceSurviveNormalBackupAndRestore()
    {
        var preview = TransferPreview();
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        var result = BankPerfectImporter.Import(preview, Target);
        var backup = Path.Combine(directory, "copy.moneta");
        var restore = Path.Combine(directory, "restored.moneta");
        byte[] audit;
        Guid transferId;
        using (var file = MonetaFile.Open(Target))
        {
            Assert.AreEqual("Épargne", file.Snapshot.Accounts[0].Name);
            Assert.AreEqual(file.Snapshot.Accounts[0].Id, file.Snapshot.SelectedAccountId);
            var current = file.Snapshot.Accounts.Single(account => account.Name == "Courant");
            var savings = file.Snapshot.Accounts.Single(account => account.Name == "Épargne");
            Assert.AreEqual(850m, file.Snapshot.AccountBalances![current.Id]);
            Assert.AreEqual(350m, file.Snapshot.AccountBalances[savings.Id]);
            Assert.AreEqual(1L, file.Snapshot.TransferCount);
            Assert.IsTrue(file.Snapshot.Accounts.All(account => account.OpeningBalance == 0m));
            Assert.AreEqual("Création du compte courant", Operations(file, current)[0].Label);
            Assert.AreEqual("Solde initial", Operations(file, savings)[0].Label);
            transferId = file.FindTransferId(Operations(file, current).Single(row => row.Amount == 150m).Id)!.Value;
            var transfer = file.ReadTransfer(transferId);
            Assert.AreEqual(current.Id, transfer.Debit.AccountId);
            Assert.AreEqual(savings.Id, transfer.Credit.AccountId);
            Assert.AreEqual(transferId, file.FindTransferId(transfer.Credit.Id));
            Assert.AreEqual(-150m, FinancialRules.SignedAmount(transfer.Debit));
            Assert.AreEqual(150m, FinancialRules.SignedAmount(transfer.Credit));
            Assert.IsNull(transfer.Fee);
            audit = file.ReadBankPerfectProvenance()!;
            using var json = JsonDocument.Parse(audit);
            Assert.IsTrue(json.RootElement.GetProperty("header").EnumerateArray().Any(value =>
                value.GetProperty("key").GetString() == "bp_script_data_fabiobptransfer" && value.GetProperty("value").GetString() == "_41_42"));
            MonetaBackup.Create(file, backup);
        }
        using var restored = MonetaBackup.Restore(backup, restore);
        Assert.AreEqual(transferId, restored.ReadTransfer(transferId).Value.Id);
        Assert.AreEqual(result.ProvenanceSha256, restored.Snapshot.BankPerfectProvenanceHash);
        CollectionAssert.AreEqual(audit, restored.ReadBankPerfectProvenance());
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("fraction", "amount-target-precision")]
    [DataRow("invalid-date", "date-range")]
    [DataRow("currency", "currency-scope")]
    [DataRow("duplicate-uid", "duplicate-operation-uid")]
    [DataRow("unknown-extra", "unknown-extra")]
    [DataRow("unknown-header", "unknown-header")]
    [DataRow("duplicate-category", "duplicate-category-id")]
    [DataRow("missing-opening", "opening-uncertain")]
    [DataRow("ambiguous-opening", "opening-uncertain")]
    [DataRow("missing-link", "transfer-missing-links")]
    [DataRow("protected", "password-field")]
    [DataRow("version", "unsupported-version")]
    [DataRow("truncated", "structure")]
    public void BlocksUnsupportedOrAmbiguousInputBeforeCreation(string variant, string code)
    {
        var header = Header + Catalog;
        var rows = Opening + "\n" + Income;
        switch (variant)
        {
            case "fraction": rows += "\n36585;;40;Limite;Quatre décimales;30;0,0001;36585;UId=4"; break;
            case "invalid-date": rows += "\n2147483647;;40;Erreur;;30;1,00;36585;UId=4"; break;
            case "currency": header = header.Replace(";€;", ";BYN;"); break;
            case "duplicate-uid": rows = rows.Replace("UId=2", "UId=1"); break;
            case "unknown-extra": rows += "<||>Ventilation=inconnue"; break;
            case "unknown-header": header += "\nBudget=inconnu"; break;
            case "duplicate-category": header += ";30=Autre"; break;
            case "missing-opening": rows = Income; break;
            case "ambiguous-opening": rows += "\n" + Opening.Replace("UId=1", "UId=9"); break;
            case "missing-link": rows += "\n46337;;24;Transfert vers le compte Épargne;essai;-1;-45;46337;UId=4"; break;
            case "protected": header += "\npass=secret-fictif"; break;
        }
        var preview = variant == "truncated" ? SaveAndPrepare(BankPerfectV5Tests.Encode(header))
            : Prepare(header, rows, variant == "version" ? '7' : '5');
        var before = Hash(Source);
        Assert.IsFalse(preview.Report.CanImport);
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == code && issue.Blocking), Describe(preview));
        Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target));
        Assert.IsFalse(File.Exists(Target));
        Assert.AreEqual(before, Hash(Source));
        NoScratch();
    }

    [TestMethod]
    public void RejectsReciprocalTransferWhoseDatesCannotBeRepresentedExactly()
    {
        var preview = TransferPreview(creditDate: "46338");
        Assert.IsFalse(preview.Report.CanImport);
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "transfer-invariant"), Describe(preview));
    }

    [TestMethod]
    public void RepeatablePreviewsAndFreshTargetIdentities()
    {
        var first = Prepare(Header, Opening);
        var second = BankPerfectImporter.Prepare(Source);
        Assert.AreEqual(JsonSerializer.Serialize(first.Report), JsonSerializer.Serialize(second.Report));
        var a = BankPerfectImporter.Import(first, Target);
        var b = BankPerfectImporter.Import(second, Path.Combine(directory, "separate.moneta"));
        Assert.AreNotEqual(a.DocumentId, b.DocumentId);
        using var openedA = MonetaFile.Open(a.Destination);
        using var openedB = MonetaFile.Open(b.Destination);
        Assert.AreNotEqual(openedA.Snapshot.Accounts.Single().Id, openedB.Snapshot.Accounts.Single().Id);
        Assert.AreNotEqual(Operations(openedA, openedA.Snapshot.Accounts[0]).Single().Id,
            Operations(openedB, openedB.Snapshot.Accounts[0]).Single().Id);
    }

    [TestMethod]
    public void DestinationCollisionAndChangedSourceNeverOverwrite()
    {
        var preview = Prepare(Header, Opening);
        File.WriteAllText(Target, "sentinelle");
        Assert.AreEqual(DocumentError.DestinationExists,
            Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target)).Error);
        Assert.AreEqual("sentinelle", File.ReadAllText(Target));
        File.Delete(Target);
        _ = Prepare(Header, Opening.Replace("1000,00", "2000,00"));
        Assert.AreEqual(DocumentError.VerificationFailed,
            Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target)).Error);
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [TestMethod]
    public void CancelBeforeRenameLeavesNoTargetEvenAfterBackup()
    {
        var preview = Prepare(Header, Opening);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Vérification de la source", StringComparison.Ordinal)) cancellation.Cancel();
        });
        Assert.ThrowsException<OperationCanceledException>(() => BankPerfectImporter.Import(preview, Target, cancellation.Token, progress));
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [TestMethod]
    public void ReadableFailureAndNoLeakedCandidateWhenAnIoPhaseFails()
    {
        var preview = Prepare(Header, Opening);
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Sauvegarde", StringComparison.Ordinal)) throw new IOException("Défaillance simulée.");
        });
        Assert.AreEqual(DocumentError.WriteFailed, Assert.ThrowsException<DocumentException>(
            () => BankPerfectImporter.Import(preview, Target, progress: progress)).Error);
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [TestMethod]
    public async Task TwoConcurrentWritersPublishOnlyOneWholeVerifiedFile()
    {
        var preview = Prepare(Header, Opening);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try { return BankPerfectImporter.Import(preview, Target); }
            catch (DocumentException error) when (error.Error == DocumentError.DestinationExists) { return null; }
        })));
        var winner = results.Single(result => result is not null)!;
        using var file = MonetaFile.Open(Target);
        Assert.AreEqual(winner.DocumentId, file.Snapshot.Id);
        Assert.AreEqual(1L, file.Snapshot.OperationCount);
        Assert.IsNotNull(file.ReadBankPerfectProvenance());
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("DROP TABLE BankPerfectProvenance;")]
    [DataRow("UPDATE BankPerfectProvenance SET payload=x'6E756C6C';")]
    public void AModifiedOrMissingProvenanceIsDetectedOnNormalOpen(string mutation)
    {
        var preview = Prepare(Header, Opening);
        BankPerfectImporter.Import(preview, Target);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Target, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = mutation;
            command.ExecuteNonQuery();
        }
        Assert.ThrowsException<DocumentException>(() => MonetaFile.Open(Target));
    }

    private BankPerfectImportPreview TransferPreview(string creditDate = "46337") =>
        Prepare(Header + "\n" + SecondAccount + "\naccounts_order=1;0\ncompte_defaut=1\nbp_script_data_fabiobptransfer=_41_42",
            Opening + "\n46337;;24;Transfert vers le compte Épargne;Démo;-1;-150,00;46337;UId=3<||>#sisterAccUid=B<||>#sisterLineUid=4" +
            Separator + "46337;R;42;Solde initial;;-1;200,00;46337;UId=2" +
            $"\n{creditDate};;42;Transfert depuis le compte Courant;Démo;-1;150,00;46337;UId=4<||>#sisterAccUid=A<||>#sisterLineUid=3");

    private BankPerfectImportPreview Prepare(string header, string rows, char version = '5') =>
        SaveAndPrepare(BankPerfectV5Tests.Encode(header + "\n" + Separator + rows, version));

    private BankPerfectImportPreview SaveAndPrepare(byte[] content)
    {
        File.WriteAllBytes(Source, content);
        return BankPerfectImporter.Prepare(Source);
    }

    private static IReadOnlyList<Transaction> Operations(MonetaFile file, Account account) => file.ReadPage(account, 0, 100, []).Transactions;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Describe(BankPerfectImportPreview preview) => string.Join(Environment.NewLine,
        preview.Report.Issues.Select(issue => $"{issue.Code} {issue.Message}"));
    private void NoScratch() => Assert.AreEqual(0, Directory.GetDirectories(directory, ".moneta-import-*").Length);

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
