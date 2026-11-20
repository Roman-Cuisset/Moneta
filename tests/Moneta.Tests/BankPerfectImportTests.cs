using System.Diagnostics;
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
        var historical = MonetaFile.ReadHistoricalFlow(Target, file.Snapshot, debit.Id).Input();
        Assert.AreEqual((HistoricalFlowKind.Expense, new DateOnly(2024, 1, 16), -12.34m, "EUR", debit.Revision),
            (historical.FlowKind!.Value, historical.Date, historical.NativeAmount, historical.SourceCode, historical.Revision!.Value));
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

    [TestMethod]
    public void NegativeOpeningAndOrdinarySameAmountRemainTwoSeparateMovements()
    {
        const string negativeOpening = "46337;R;23;Création du compte courant;;-1;-250,00;46337;UId=12";
        var preview = Prepare(Header, "45306;;23;Autre débit;;-1;-250,00;45306;UId=13\n" + negativeOpening);
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "opening-probable" &&
            issue.Kind == BankPerfectImportIssueKind.HistoricalUncertainty));
        BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        var account = file.Snapshot.Accounts.Single();
        Assert.AreEqual(0m, account.OpeningBalance);
        Assert.AreEqual(-500m, file.Snapshot.AccountBalances![account.Id]);
        Assert.AreEqual(-250m, file.Snapshot.AccountPointedBalances![account.Id]);
        Assert.AreEqual(2, Operations(file, account).Count);
        using var audit = JsonDocument.Parse(file.ReadBankPerfectProvenance()!);
        var opening = audit.RootElement.GetProperty("objects").EnumerateArray().Single(entry => entry.GetProperty("opening").GetBoolean());
        Assert.AreEqual("12", opening.GetProperty("sourceId").GetString());
        Assert.AreEqual("Probable", opening.GetProperty("openingConfidence").GetString());
    }

    [TestMethod]
    public void EarlierOptionalDateRemainsUnclassifiedAndFutureOperationIsReopenedExactly()
    {
        var booked = new DateOnly(2099, 1, 1);
        var earlier = new DateOnly(2098, 12, 31);
        var epoch = new DateOnly(1899, 12, 30);
        var futureRow = FormattableString.Invariant($"{booked.DayNumber - epoch.DayNumber};P;40;Fin d’année;;-1;25,00;{earlier.DayNumber - epoch.DayNumber};UId=5");
        var preview = Prepare(Header, Opening + "\n" + futureRow + "\n36585;;40;Bissextile;;-1;1,00");
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "earlier-additional-date" &&
            issue.Kind == BankPerfectImportIssueKind.HistoricalUncertainty));
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "additional-date-provenance" &&
            issue.Kind == BankPerfectImportIssueKind.ProvenanceOnly));

        BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        var rows = Operations(file, file.Snapshot.Accounts.Single());
        var pointed = rows.Single(row => row.Label == "Fin d’année");
        Assert.AreEqual(booked, pointed.Date);
        Assert.AreEqual(ReconciliationStatus.Marked, pointed.Status);
        Assert.IsNull(pointed.MarkedOn);
        Assert.AreEqual(new DateOnly(2000, 2, 29), rows.Single(row => row.Label == "Bissextile").Date);
        using var audit = JsonDocument.Parse(file.ReadBankPerfectProvenance()!);
        var raw = audit.RootElement.GetProperty("objects").EnumerateArray()
            .Single(item => item.GetProperty("targetId").GetGuid() == pointed.Id).GetProperty("raw");
        Assert.AreEqual(FormattableString.Invariant($"{earlier.DayNumber - epoch.DayNumber}"), raw[7].GetString());
    }

    [TestMethod]
    public void EqualSameDayTransfersKeepTheirOwnSistersAndReconciledLegProtection()
    {
        const string thirdAccount = "compte2=;;;Épargne 2;;1;€;6,55957;F;;;;;;real;C;";
        var preview = Prepare(Header + "\n" + SecondAccount + "\n" + thirdAccount,
            Opening +
            "\n46337;P;24;Transfert vers le compte Épargne;Identique;-1;-150,00;46337;UId=0003<||>#sisterAccUid=B<||>#sisterLineUid=0006" +
            "\n46337;;24;Transfert vers le compte Épargne 2;Identique;-1;-150,00;46337;UId=4<||>#sisterAccUid=C<||>#sisterLineUid=7" +
            Separator + "46337;R;42;Solde initial;;-1;200,00;46337;UId=2" +
            "\n46337;R;42;Transfert depuis le compte Courant;Identique;-1;150,00;46337;UId=0006<||>#sisterAccUid=A<||>#sisterLineUid=0003" +
            Separator + "46337;R;42;Solde initial;;-1;200,00;46337;UId=5" +
            "\n46337;P;42;Transfert depuis le compte Courant;Identique;-1;150,00;46337;UId=7<||>#sisterAccUid=A<||>#sisterLineUid=4");
        Assert.IsTrue(preview.Report.CanImport, Describe(preview));
        BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        Assert.AreEqual(2L, file.Snapshot.TransferCount);
        var account = file.Snapshot.Accounts[0];
        var legs = Operations(file, account).Where(row => row.Direction == TransactionDirection.Debit).ToArray();
        var first = file.ReadTransfer(file.FindTransferId(legs[0].Id)!.Value);
        var second = file.ReadTransfer(file.FindTransferId(legs[1].Id)!.Value);
        Assert.AreNotEqual(first.Value.Id, second.Value.Id);
        Assert.AreEqual(file.Snapshot.Accounts[1].Id, first.Credit.AccountId);
        Assert.AreEqual(file.Snapshot.Accounts[2].Id, second.Credit.AccountId);
        CollectionAssert.AreEqual(new[] { ReconciliationStatus.Marked, ReconciliationStatus.Reconciled,
            ReconciliationStatus.Unmarked, ReconciliationStatus.Marked },
            new[] { first.Debit.Status, first.Credit.Status, second.Debit.Status, second.Credit.Status });
        Assert.ThrowsException<DocumentException>(() => file.Save(file.Snapshot,
            [new(first.Debit with { Details = "Autre", Revision = 2 }, 1),
             new(first.Credit with { Details = "Autre", Revision = 2 }, 1)],
            transfers: [new(first.Value with { Notes = "Autre", Revision = 2 }, 1)]));

        using var audit = JsonDocument.Parse(file.ReadBankPerfectProvenance()!);
        var relation = audit.RootElement.GetProperty("objects").EnumerateArray()
            .Single(item => item.GetProperty("targetId").GetGuid() == first.Value.Id).GetProperty("transfer");
        var debit = relation.GetProperty("debit");
        var credit = relation.GetProperty("credit");
        Assert.AreEqual("0003", debit.GetProperty("sourceOperationId").GetString());
        Assert.AreEqual("0006", debit.GetProperty("sisterOperationId").GetString());
        Assert.AreEqual("0006", credit.GetProperty("sourceOperationId").GetString());
        Assert.AreEqual("0003", credit.GetProperty("sisterOperationId").GetString());
        Assert.AreEqual(first.Debit.Id, debit.GetProperty("targetOperationId").GetGuid());
        Assert.AreEqual(first.Credit.Id, credit.GetProperty("targetOperationId").GetGuid());
    }

    [DataTestMethod]
    [DataRow("missing-line", "transfer-incomplete")]
    [DataRow("wrong-account", "transfer-wrong-account")]
    [DataRow("wrong-return", "transfer-nonreciprocal")]
    [DataRow("duplicate-reference", "transfer-ambiguous")]
    [DataRow("missing-operation-id", "transfer-missing-source-id")]
    public void AmbiguousSistersBlockTheWholePublication(string scenario, string expected)
    {
        var from = "46337;;24;Virement;Lien;-1;-15;46337;UId=3<||>#sisterAccUid=B<||>#sisterLineUid=4";
        var to = "46337;;42;Virement;Lien;-1;15;46337;UId=4<||>#sisterAccUid=A<||>#sisterLineUid=3";
        switch (scenario)
        {
            case "missing-line": from = from.Replace("<||>#sisterLineUid=4", ""); break;
            case "wrong-account": from = from.Replace("#sisterAccUid=B", "#sisterAccUid=A"); break;
            case "wrong-return": to = to.Replace("#sisterLineUid=3", "#sisterLineUid=1"); break;
            case "duplicate-reference": from += "<||>#sisterAccUid=B"; break;
            case "missing-operation-id": to = to.Replace("UId=4<||>", ""); break;
        }
        var preview = Prepare(Header + "\n" + SecondAccount, Opening + "\n" + from +
            Separator + "46337;R;42;Solde initial;;-1;200,00;46337;UId=2\n" + to);
        Assert.IsFalse(preview.Report.CanImport);
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == expected && issue.Blocking), Describe(preview));
        Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target));
        Assert.IsFalse(File.Exists(Target));
    }

    [TestMethod]
    public void ChangedCandidateAfterRestoreFailsTheLastCheckBeforePublication()
    {
        var preview = Prepare(Header, Opening);
        var before = Hash(Source);
        var progress = new InlineProgress(message =>
        {
            if (!message.StartsWith("Vérification de la source", StringComparison.Ordinal)) return;
            var scratch = Directory.GetDirectories(directory, ".moneta-import-*").Single();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(scratch, "candidate.moneta"), Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Transactions SET label='Ligne altérée après contrôle';";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        });
        Assert.AreEqual(DocumentError.VerificationFailed,
            Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target, progress: progress)).Error);
        Assert.IsFalse(File.Exists(Target));
        Assert.AreEqual(before, Hash(Source));
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("read-file")]
    [DataRow("decode")]
    [DataRow("parse")]
    [DataRow("validate-v5")]
    public void PreparationCanBeCancelledWhileReadingOrValidating(string phase)
    {
        _ = Prepare(Header, Opening);
        var before = Hash(Source);
        using var cancellation = new CancellationTokenSource();
        var thread = Environment.CurrentManagedThreadId;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Moneta.BankPerfect.V5",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (Environment.CurrentManagedThreadId == thread && activity.OperationName == phase)
                    cancellation.Cancel();
            }
        };
        ActivitySource.AddActivityListener(listener);
        Assert.ThrowsException<OperationCanceledException>(() => BankPerfectImporter.Prepare(Source, cancellation.Token));
        Assert.AreEqual(before, Hash(Source));
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("Écriture du dossier")]
    [DataRow("Réouverture et comparaison")]
    [DataRow("Restauration et comparaison")]
    public void CancelledImportLeavesNoPublishedCandidate(string phase)
    {
        var preview = Prepare(Header, Opening);
        var before = Hash(Source);
        using var cancellation = new CancellationTokenSource();
        if (phase.Length == 0) cancellation.Cancel();
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith(phase, StringComparison.Ordinal)) cancellation.Cancel();
        });
        Assert.ThrowsException<OperationCanceledException>(() =>
            BankPerfectImporter.Import(preview, Target, cancellation.Token, progress));
        Assert.AreEqual(before, Hash(Source));
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("Écriture du dossier", DocumentError.DestinationExists)]
    [DataRow("Sauvegarde de contrôle", DocumentError.DestinationExists)]
    [DataRow("Restauration et comparaison", DocumentError.VerificationFailed)]
    public void RealStorageFailuresLeaveNoPublishedCandidate(string phase, DocumentError error)
    {
        var preview = Prepare(Header, Opening);
        var before = Hash(Source);
        var progress = new InlineProgress(message =>
        {
            if (!message.StartsWith(phase, StringComparison.Ordinal)) return;
            var work = Directory.GetDirectories(directory, ".moneta-import-*").Single();
            switch (error)
            {
                case DocumentError.VerificationFailed:
                    File.AppendAllText(Path.Combine(work, "backup.moneta"), "archive altérée");
                    break;
                default:
                    var name = phase.StartsWith("Écriture", StringComparison.Ordinal) ? "candidate.moneta" : "backup.moneta";
                    File.WriteAllText(Path.Combine(work, name), "fichier de test qui occupe le nom");
                    break;
            }
        });
        Assert.AreEqual(error, Assert.ThrowsException<DocumentException>(() =>
            BankPerfectImporter.Import(preview, Target, progress: progress)).Error);
        Assert.AreEqual(before, Hash(Source));
        Assert.IsFalse(File.Exists(Target));
        NoScratch();
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("-wal")]
    public void LateDestinationCollisionLeavesTheExistingBytesUntouched(string suffix)
    {
        var preview = Prepare(Header, Opening);
        var existing = Target + suffix;
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Vérification de la source", StringComparison.Ordinal))
                File.WriteAllText(existing, "fichier externe");
        });
        Assert.AreEqual(DocumentError.DestinationExists, Assert.ThrowsException<DocumentException>(() =>
            BankPerfectImporter.Import(preview, Target, progress: progress)).Error);
        Assert.AreEqual("fichier externe", File.ReadAllText(existing));
        Assert.AreEqual(suffix.Length == 0, File.Exists(Target));
        NoScratch();
    }

    [TestMethod]
    public void HeldDestinationLockBlocksImportAndCanBeReusedAfterRelease()
    {
        var preview = Prepare(Header, Opening);
        var lockPath = Target + ".lock";
        File.WriteAllText(lockPath, "verrou réservé au test");
        using (File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(DocumentError.DestinationExists, Assert.ThrowsException<DocumentException>(() =>
                BankPerfectImporter.Import(preview, Target)).Error);
            Assert.IsFalse(File.Exists(Target));
            NoScratch();
        }
        BankPerfectImporter.Import(preview, Target);
        using (var file = MonetaFile.Open(Target))
            Assert.AreEqual(1L, file.Snapshot.OperationCount);
        Assert.AreEqual("verrou réservé au test", File.ReadAllText(lockPath));
        NoScratch();
    }

    [TestMethod]
    public void CleanupLeavesUnrelatedContentsInTheWorkDirectory()
    {
        var preview = Prepare(Header, Opening);
        using var cancellation = new CancellationTokenSource();
        string? marker = null;
        var progress = new InlineProgress(message =>
        {
            if (!message.StartsWith("Réouverture et comparaison", StringComparison.Ordinal)) return;
            var work = Directory.GetDirectories(directory, ".moneta-import-*").Single();
            marker = Path.Combine(work, "fichier-tiers.txt");
            File.WriteAllText(marker, "à préserver");
            cancellation.Cancel();
        });
        Assert.ThrowsException<OperationCanceledException>(() =>
            BankPerfectImporter.Import(preview, Target, cancellation.Token, progress));
        Assert.IsFalse(File.Exists(Target));
        Assert.IsNotNull(marker);
        Assert.AreEqual("à préserver", File.ReadAllText(marker));
        CollectionAssert.AreEqual(new[] { marker }, Directory.GetFiles(Path.GetDirectoryName(marker)!));
    }

    [TestMethod]
    public void AnUnavailableDestinationDirectoryNeverPublishesIntoItsMovedCopy()
    {
        var preview = Prepare(Header, Opening);
        var backup = Hash(Source);
        var originalDirectory = Directory.CreateDirectory(Path.Combine(directory, "destination")).FullName;
        var movedDirectory = Path.Combine(directory, "déplacé");
        var destination = Path.Combine(originalDirectory, "new.moneta");
        var progress = new InlineProgress(message =>
        {
            if (message.StartsWith("Vérification de la source", StringComparison.Ordinal))
                Directory.Move(originalDirectory, movedDirectory);
        });
        Assert.AreEqual(DocumentError.MissingFile, Assert.ThrowsException<DocumentException>(() =>
            BankPerfectImporter.Import(preview, destination, progress: progress)).Error);
        Assert.IsFalse(File.Exists(destination));
        Assert.IsFalse(File.Exists(Path.Combine(movedDirectory, Path.GetFileName(destination))));
        Assert.AreEqual(1, Directory.GetDirectories(movedDirectory, ".moneta-import-*").Length);
        Assert.AreEqual(backup, Hash(Source));
    }

    [DataTestMethod]
    [DataRow("R", "Départ atypique")]
    [DataRow("P", "Création du compte courant")]
    [DataRow("", "Création du compte courant")]
    public void AnUnattestedOpeningStaysBlockedDespiteMatchingAmounts(string state, string label)
    {
        var preview = Prepare(Header, $"46337;{state};40;{label};;-1;1000,00;46337;UId=1\n" +
            "45306;;40;Création du compte courant;;-1;1000,00;45306;UId=2");
        Assert.AreEqual(2000m, preview.Report.Accounts.Single().Balance);
        Assert.IsTrue(preview.Report.Issues.Any(issue => issue.Code == "opening-uncertain" && issue.Blocking));
        Assert.ThrowsException<DocumentException>(() => BankPerfectImporter.Import(preview, Target));
        Assert.IsFalse(File.Exists(Target));
    }

    [TestMethod]
    public void ImportedTransferEditsAndTombstonesPreserveTheOriginalProvenance()
    {
        var preview = TransferPreview();
        var imported = BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        var originalAudit = file.ReadBankPerfectProvenance()!;
        using var report = JsonDocument.Parse(originalAudit);
        var id = report.RootElement.GetProperty("objects").EnumerateArray()
            .Single(entry => entry.GetProperty("type").GetString() == "transfer").GetProperty("targetId").GetGuid();
        var original = file.ReadTransfer(id);
        var model = new MainViewModel(file.Snapshot, file);
        var current = model.EditTransfer(id, original.Value.Revision,
            new(original.Debit.AccountId, original.Credit.AccountId, original.Value.Date.AddDays(1),
                151m, 151m, "Référence Moneta", "Correction Moneta"));
        file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations, model.PendingTransfers);
        Assert.AreEqual(current, file.ReadTransfer(id));
        Assert.AreEqual(original.Debit.Id, current.Debit.Id);
        Assert.AreEqual(original.Credit.Id, current.Credit.Id);
        CollectionAssert.AreEqual(originalAudit, file.ReadBankPerfectProvenance());

        model = new MainViewModel(file.Snapshot, file);
        model.DeleteTransfer(id, current.Value.Revision);
        file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations, model.PendingTransfers);
        var backup = Path.Combine(directory, "changes.backup.moneta");
        MonetaBackup.Create(file, backup);
        using var restored = MonetaBackup.Restore(backup, Path.Combine(directory, "changes.restored.moneta"));
        Assert.IsTrue(restored.ReadTransfer(id).Value.IsDeleted);
        CollectionAssert.AreEqual(originalAudit, restored.ReadBankPerfectProvenance());
        Assert.AreEqual(imported.ProvenanceSha256, restored.Snapshot.BankPerfectProvenanceHash);
    }

    [TestMethod]
    public void ReopeningAFormalStatementRestoresTheImportedPointingWithoutChangingProvenance()
    {
        var marked = Income.Replace("45306;;", "45306;P;").Replace(";45306;UId", ";45291;UId");
        var preview = Prepare(Header + Catalog, Opening + "\n" + marked);
        BankPerfectImporter.Import(preview, Target);
        using var file = MonetaFile.Open(Target);
        var audit = file.ReadBankPerfectProvenance()!;
        var account = file.Snapshot.Accounts.Single();
        var rows = Operations(file, account);
        var opening = rows.Single(row => row.Status == ReconciliationStatus.Reconciled);
        var original = rows.Single(row => row.Status == ReconciliationStatus.Marked);
        var model = new MainViewModel(file.Snapshot, file);
        model.ReleaseLegacyReconciliation(new(opening.Id, opening.Revision), "Libération de test.");
        file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations);

        model = new MainViewModel(file.Snapshot, file);
        var session = model.StageReconciliation(new(original.Date, original.Amount, "Relevé fictif"), [original.Id]);
        file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations);
        Assert.AreEqual(ReconciliationStatus.Reconciled, file.ReadTransactions([original.Id]).Single().Status);

        model = new MainViewModel(file.Snapshot, file);
        model.ReopenReconciliation(session.Id, session.Revision, "Réexamen de test.");
        file.Save(model.CaptureDocument(), model.PendingChanges, model.PendingReconciliations);
        var backup = Path.Combine(directory, "statement.backup.moneta");
        MonetaBackup.Create(file, backup);
        using var restored = MonetaBackup.Restore(backup, Path.Combine(directory, "statement.restored.moneta"));
        var row = restored.ReadTransactions([original.Id]).Single();
        Assert.AreEqual(ReconciliationStatus.Marked, row.Status);
        Assert.IsNull(row.MarkedOn);
        Assert.IsNull(row.ReconciliationId);
        Assert.AreEqual(ReconciliationSessionState.Reopened, restored.ReadReconciliation(session.Id).Session.State);
        CollectionAssert.AreEqual(audit, restored.ReadBankPerfectProvenance());
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
