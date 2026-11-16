using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.BankPerfect;

namespace Moneta.Tests;

[TestClass]
public sealed class BankPerfectV5Tests
{
    private const string Separator = "TNSLPPBNTSOGPTQBC280673";
    private const string Header = "utilisateur=Fictif\nguid=fictif\ncompte0=;;;Courant;;1;€;6,55957;F;;;;;;real;A;";
    private const string AccountB = "compte1=;;;Épargne;;1;€;6,55957;F;;;;;;real;B;";
    private const string Operation = "36585;;40;Épicerie;À Noël;-1;1,00;36585;UId=1";

    [TestMethod]
    public void ReadsAndPreservesSourceWithoutWritingFile()
    {
        var content = Encode(Header + "\n" + Separator + Operation);
        var path = Path.Combine(Path.GetTempPath(), $"moneta-v5-{Guid.NewGuid():N}.bp");
        try
        {
            File.WriteAllBytes(path, content);
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = V5Reader.ReadFile(path);
            var row = result.Document!.Accounts.Single().Operations.Single();
            Assert.AreEqual("V5", result.Report.Format);
            Assert.AreEqual(V5Coverage.Recognized, result.Report.Coverage);
            Assert.AreEqual("Épicerie", row.Payee);
            Assert.AreEqual("À Noël", row.Details);
            Assert.AreEqual("Versement", row.Mode);
            Assert.AreEqual("1,00", row.Amount.Raw);
            Assert.AreEqual(2, row.Amount.Scale);
            Assert.AreEqual(1m, row.Amount.Value);
            Assert.AreEqual(new DateOnly(2000, 2, 29), row.Date.Date);
            Assert.AreEqual(new V5Location(4, 0, 0), row.Date.Source);
            CollectionAssert.AreEqual(SHA256.HashData(content), SHA256.HashData(source));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [DataTestMethod]
    [DataRow("\n", "\r\n", V5LineRecovery.CrLfToLf)]
    [DataRow("\r\n", "\n", V5LineRecovery.LfToCrLf)]
    [DataRow("\n", "\r", V5LineRecovery.None)]
    public void RecoversConvertedLineEndings(string original, string converted, V5LineRecovery recovery)
    {
        var encoded = Encode((Header + "\n" + Separator + Operation).Replace("\n", original));
        var changed = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(encoded).Replace(original, converted));
        var result = V5Reader.Read(changed);
        Assert.AreEqual(recovery, result.Report.Recovery);
        Assert.AreEqual(1m, result.Document!.Accounts.Single().LedgerSum);
    }

    [TestMethod]
    public void KeepsAbsentIdentifiersAndAdditionalDateSeparateFromExplicitZero()
    {
        var result = Parse("46337;R;40;Création du compte courant;;-1;0,00",
            Header.Replace(";A;", ""));
        var account = result.Document!.Accounts.Single();
        var row = account.Operations.Single();
        Assert.IsNull(account.Fields.Uid);
        Assert.IsNull(row.Uid);
        Assert.IsNull(row.AdditionalDate.Raw);
        Assert.IsFalse(row.AdditionalDate.Present);
        Assert.IsNull(row.AdditionalDate.Date);
        Assert.AreEqual(V5Pointing.R, row.Pointing);
        Assert.AreEqual(0m, account.LedgerSum);
        Assert.AreEqual(V5OpeningConfidence.Probable, account.Opening);
        Assert.AreEqual(0, result.Report.ConfirmedOpenings);
        Assert.AreEqual(1, result.Report.ProbableOpenings);
        Assert.AreEqual(0, result.Report.IndeterminateOpenings);
        AssertFinding(result, "missing-account-uid");
        AssertFinding(result, "missing-operation-uid");
        AssertFinding(result, "missing-additional-date");

        var present = Parse("36585;P;20;;;-1;0;0;UId=1").Document!.Accounts[0].Operations[0];
        Assert.IsTrue(present.AdditionalDate.Present);
        Assert.AreEqual(new DateOnly(1899, 12, 30), present.AdditionalDate.Date);
    }

    [DataTestMethod]
    [DataRow("-922337203685477,5808", "-922337203685477.5808", 4, V5AmountCompatibility.ExcessPrecision)]
    [DataRow("0,0001", "0.0001", 4, V5AmountCompatibility.ExcessPrecision)]
    [DataRow("1087.6701", "1087.6701", 4, V5AmountCompatibility.ExcessPrecision)]
    [DataRow("100,0100", "100.0100", 4, V5AmountCompatibility.Exact)]
    [DataRow("92233720368547758.08", "92233720368547758.08", 2, V5AmountCompatibility.OutOfRange)]
    [DataRow("1.000000000000000000000000000000", "1", 30, V5AmountCompatibility.Exact)]
    public void DecodesAmountsExactly(string raw, string exact, int scale, V5AmountCompatibility compatibility)
    {
        var result = Parse($"36585;1;20;;;-1;{raw};36585;UId=1");
        var row = result.Document!.Accounts[0].Operations[0];
        Assert.AreEqual(raw, row.Amount.Raw);
        Assert.AreEqual(decimal.Parse(exact, CultureInfo.InvariantCulture), row.Amount.Value);
        Assert.AreEqual(scale, row.Amount.Scale);
        Assert.AreEqual(compatibility, row.Amount.Compatibility);
        Assert.AreEqual(V5Pointing.P, row.Pointing);
        Assert.AreEqual(1, result.Report.ExactSourceAmounts);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("1.23.4")]
    [DataRow("1-2")]
    [DataRow("12,")]
    [DataRow(".3")]
    [DataRow("1e2")]
    [DataRow("1 000,00")]
    [DataRow("79228162514264337593543950336")]
    [DataRow("0.00000000000000000000000000001")]
    [DataRow("7922816251426433759354395033.51")]
    public void RefusesToRoundOrReplaceInvalidNumbers(string raw)
    {
        var result = Parse($"36585;;20;;;-1;{raw};36585;UId=1");
        var account = result.Document!.Accounts[0];
        Assert.AreEqual(raw, account.Operations[0].Amount.Raw);
        Assert.IsNull(account.Operations[0].Amount.Value);
        Assert.IsNull(account.LedgerSum);
        AssertFinding(result, "invalid-amount");
    }

    [DataTestMethod]
    [DataRow("", "missing-operation-date")]
    [DataRow("abc", "invalid-date")]
    [DataRow("2147483648", "invalid-date")]
    [DataRow("2147483647", "date-range")]
    [DataRow("-2147483648", "date-range")]
    public void RetainsInvalidDatesWithoutSubstitution(string date, string code)
    {
        var result = Parse($"{date};;40;;;-1;1;36585;UId=1");
        var row = result.Document!.Accounts[0].Operations[0];
        Assert.IsNull(row.Date.Date);
        Assert.AreEqual(date, row.Date.Raw);
        Assert.AreEqual(new DateOnly(2000, 2, 29), row.AdditionalDate.Date);
        AssertFinding(result, code);
    }

    [TestMethod]
    public void PreservesMaximumUidAndFlagsDuplicatesWithoutReassigning()
    {
        var first = Operation.Replace("UId=1", "UId=18446744073709551615");
        var second = Operation.Replace("UId=1", "UId=18446744073709551616");
        var result = Parse(first + "\n" + second + Separator + first, Header + "\n" + AccountB.Replace(";B;", ";A;"));
        var rows = result.Document!.Accounts.SelectMany(account => account.Operations).ToArray();
        Assert.AreEqual(ulong.MaxValue, rows[0].Uid);
        Assert.IsNull(rows[1].Uid);
        Assert.AreEqual(ulong.MaxValue, rows[2].Uid);
        AssertFinding(result, "invalid-operation-uid");
        AssertFinding(result, "duplicate-operation-uid");
        AssertFinding(result, "duplicate-account-uid");
    }

    [TestMethod]
    public void ResolvesReciprocalReferencesWithoutMatchingDatesOrModes()
    {
        var result = TwoAccounts(Link(1, "B", 2, "-15"), Link(2, "A", 1, "15").Replace("36585", "40000"));
        Assert.AreEqual(1, result.Report.LinkedTransfers);
        var transfers = result.Document!.Transfers;
        Assert.AreEqual(2, transfers.Count);
        Assert.IsTrue(transfers.All(link => link.Status == V5TransferStatus.Linked && link.Reciprocal));
        Assert.AreEqual(new V5Location(5, 1), transfers[0].Target);
        Assert.AreEqual("B", transfers[0].RawTargetAccountUid);
    }

    [DataTestMethod]
    [DataRow("#sisterAccUid=B", true, V5TransferStatus.Incomplete, "transfer-incomplete")]
    [DataRow("#sisterAccUid=C<||>#sisterLineUid=2", true, V5TransferStatus.MissingTarget, "transfer-missing-target")]
    [DataRow("#sisterAccUid=A<||>#sisterLineUid=2", true, V5TransferStatus.Inconsistent, "transfer-wrong-account")]
    [DataRow("#sisterAccUid=B<||>#sisterLineUid=0", true, V5TransferStatus.Inconsistent, "transfer-invalid-uid")]
    [DataRow("#sisterAccUid=B<||>#sisterAccUid=C<||>#sisterLineUid=2", true, V5TransferStatus.Ambiguous, "transfer-ambiguous")]
    [DataRow("#sisterAccUid=B<||>#sisterLineUid=2", false, V5TransferStatus.Inconsistent, "transfer-nonreciprocal")]
    public void ReportsBrokenTransfers(string extra, bool hasReturn, V5TransferStatus status, string code)
    {
        var row = "36585;;24;;;-1;-15;36585;UId=1<||>" + extra;
        var result = TwoAccounts(row, hasReturn ? Link(2, "A", 1, "15") : Operation.Replace("UId=1", "UId=2"));
        Assert.AreEqual(status, result.Document!.Transfers[0].Status);
        Assert.AreEqual(0, result.Report.LinkedTransfers);
        AssertFinding(result, code);
    }

    [TestMethod]
    public void DoesNotResolveDuplicatedOperationUidAcrossAccounts()
    {
        var result = TwoAccounts(Link(1, "B", 2, "-15") + "\n" + Operation.Replace("UId=1", "UId=2"),
            Link(2, "A", 1, "15"));
        Assert.AreEqual(V5TransferStatus.Ambiguous, result.Document!.Transfers[0].Status);
        Assert.AreEqual(0, result.Report.LinkedTransfers);
    }

    [TestMethod]
    public void DoesNotLinkByAmountsOrTreatModeAsAnOpening()
    {
        var result = TwoAccounts("36585;R;42;Virement reçu;;-1;-15;36585;UId=1",
            "36585;;42;;;-1;15;36585;UId=2");
        Assert.AreEqual(0, result.Document!.Transfers.Count);
        Assert.AreEqual(V5OpeningConfidence.Indeterminate, result.Document.Accounts[0].Opening);
        Assert.AreEqual(2, result.Report.IndeterminateOpenings);
    }

    [TestMethod]
    public void KeepsCategoryOrderAndCustomModesAndPayeeTemplates()
    {
        const string refs = "\ncateg=30=Salaire;34=     Énergie;2=Vie\nmodes=-Chèque spécial;Prime\nwho0=Abonnement;10;34;Café;Détail;0,01";
        var result = Parse("36585;;44;Café;;34;-15;36585;UId=1\n36585;;Prime;Café;;2;15;36585;UId=2", Header + refs);
        var document = result.Document!;
        CollectionAssert.AreEqual(new uint?[] { 30, 34, 2 }, document.Categories.Select(item => item.Id).ToArray());
        Assert.AreEqual(0, document.Categories[1].ParentItem);
        Assert.AreEqual(5, document.Categories[1].Indentation);
        Assert.AreEqual("Énergie", document.Categories[1].Name);
        Assert.AreEqual("Chèque spécial", document.PayeeTemplates[0].Mode);
        Assert.AreEqual("0,01", document.PayeeTemplates[0].AmountText);
        Assert.AreEqual("Chèque spécial", document.Accounts[0].Operations[0].Mode);
        Assert.AreEqual("Prime", document.Accounts[0].Operations[1].Mode);
        Assert.AreEqual(1, result.Report.DistinctOperationPayees);
    }

    [TestMethod]
    public void PreservesLegacyCategoryPositionsSeparatelyFromExplicitIds()
    {
        var result = Parse(Operation.Replace(";-1;", ";1;"), Header + "\ncateg=Racine;     Enfant;99=Implicite");
        var categories = result.Document!.Categories;
        Assert.IsNull(categories[1].Id);
        Assert.IsNull(categories[1].RawId);
        Assert.AreEqual(1U, categories[1].LegacyPositionId);
        Assert.AreEqual(0, categories[1].ParentItem);
        Assert.AreEqual(99U, categories[2].Id);
        Assert.IsFalse(result.Report.Diagnostics.Any(item => item.Code == "missing-category-target"));
        AssertFinding(result, "legacy-category-positions");
    }

    [TestMethod]
    public void RetainsUnknownFieldsAndOpaquePayloadButMasksPasswordAndReportValues()
    {
        var result = Parse(Operation + "<||>champ=un<fab_pv>deux<fab_cr>trois<||>champ=encore",
            Header + "\npass=secretSynthetique\ncompté0=valeurPrivee\nbp_script_data_essai=_00_AA_zz");
        var document = result.Document!;
        Assert.IsTrue(document.Header.Single(item => item.Key == "pass").Redacted);
        Assert.IsNull(document.Header.Single(item => item.Key == "pass").Value);
        Assert.AreEqual("valeurPrivee", document.Header.Single(item => item.Key == "compté0").Value);
        Assert.AreEqual(5, document.Extensions[0].DecodedByteCount);
        Assert.AreEqual("un;deux\ntrois", document.Accounts[0].Operations[0].Extra[1].Value);
        AssertFinding(result, "unknown-header");
        AssertFinding(result, "unknown-extra");
        AssertFinding(result, "duplicate-extra");
        var json = JsonSerializer.Serialize(result.Report);
        Assert.IsFalse(json.Contains("valeurPrivee", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("secretSynthetique", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("1,00", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DoesNotTreatUnknownModePointingOrDuplicateCategoryAsDefaults()
    {
        var result = Parse("36585;?;19;;;2;1;36585;UId=1", Header + "\ncateg=2=Vie;2=Doublon");
        var row = result.Document!.Accounts[0].Operations[0];
        Assert.AreEqual(V5Pointing.Unknown, row.Pointing);
        Assert.IsNull(row.Mode);
        Assert.AreEqual(2U, row.CategoryId);
        AssertFinding(result, "ambiguous-category-target");
        AssertFinding(result, "unknown-mode");
    }

    [TestMethod]
    public void RejectsEmptyGarbageClearHeaderAndOtherVersions()
    {
        AssertFinding(V5Reader.Read([]), "empty-file");
        AssertFinding(V5Reader.Read("invalid input"u8), "invalid-signature");
        AssertFinding(V5Reader.Read("BP-FILE-5"u8), "invalid-signature");
        var v6 = V5Reader.Read("BPFILE6\0"u8);
        Assert.AreEqual("V6", v6.Report.Format);
        AssertFinding(v6, "unsupported-version");
        var v7 = V5Reader.Read(Encode(Header, '7'));
        Assert.AreEqual("V7", v7.Report.Format);
        AssertFinding(v7, "unsupported-version");
        Assert.IsNull(v6.Document);
        Assert.IsNull(v7.Document);
    }

    [TestMethod]
    public void DetectsTruncationAndAmbiguousOrphanBlocks()
    {
        var encoded = Encode(Header + "\n" + Separator + Operation);
        AssertFinding(V5Reader.Read(encoded.AsSpan(0, encoded.Length - 1)), "invalid-signature");
        AssertFinding(V5Reader.Read(Encode(Header)), "structure");
        var result = Parse("36585;;40" + Separator + Operation, Header + "\ncompte2=incomplet\nligne sans égal");
        Assert.AreEqual(V5Coverage.Partial, result.Report.Coverage);
        Assert.IsFalse(result.Document!.Accounts[0].Operations[0].ValidLayout);
        Assert.AreEqual(2, result.Document.Unparsed.Count);
        AssertFinding(result, "orphan-block");
        AssertFinding(result, "operation-layout");
        AssertFinding(result, "invalid-header-line");
    }

    [TestMethod]
    public void ReportsUndefinedCp1252AndRejectsUnexpectedControlBytes()
    {
        var raw = Encode(Header.Replace("Fictif", "!") + "\n" + Separator + Operation);
        var offset = Array.IndexOf(raw, (byte)'!');
        raw[offset] = 0x81;
        var undefined = V5Reader.Read(raw);
        AssertFinding(undefined, "undefined-cp1252");
        Assert.AreEqual("\u0081", undefined.Document!.Header[0].Value);
        raw[offset] = 0;
        var control = V5Reader.Read(raw);
        AssertFinding(control, "control-byte");
        Assert.IsNull(control.Document);
    }

    [TestMethod]
    public void EnforcesByteLineOperationAndCollectionLimits()
    {
        AssertFinding(V5Reader.Read(new byte[V5Limits.Bytes + 1]), "limit");
        AssertFinding(Parse(new string('a', V5Limits.LineLength + 1)), "limit");
        AssertFinding(Parse(new string('\n', V5Limits.Lines + 1)), "limit");
        AssertFinding(Parse(new string(';', V5Limits.Fields)), "limit");
        AssertFinding(Parse(Operation + "<||>#sisterAccUid=" + new string('a', V5Limits.IdentifierLength + 1)), "limit");
        var repeated = string.Concat(Enumerable.Repeat(Operation + "\n", V5Limits.Operations + 1));
        AssertFinding(Parse(repeated), "limit");
        var accounts = string.Join('\n', Enumerable.Range(1, V5Limits.Accounts).Select(i => $"compte{i}=;;;Courant;;1;€"));
        AssertFinding(Parse(Operation, Header + "\n" + accounts), "limit");
    }

    [TestMethod]
    public void KeepsDiagnosticsBoundedAndParsingDeterministicAcrossCultures()
    {
        var data = Encode(Header + "\n" + Separator + string.Concat(Enumerable.Repeat("bad\n", V5Limits.Diagnostics + 20)));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var first = V5Reader.Read(data);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = V5Reader.Read(data);
            Assert.AreEqual(V5Limits.Diagnostics + 1, first.Report.Diagnostics.Count);
            AssertFinding(first, "diagnostics-omitted");
            Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static V5ReadResult Parse(string operations, string header = Header) =>
        V5Reader.Read(Encode(header + "\n" + Separator + operations));

    private static V5ReadResult TwoAccounts(string first, string second) =>
        Parse(first + Separator + second, Header + "\n" + AccountB);

    private static string Link(ulong uid, string account, ulong target, string amount) =>
        $"36585;;24;;;-1;{amount};36585;UId={uid}<||>#sisterAccUid={account}<||>#sisterLineUid={target}";

    private static void AssertFinding(V5ReadResult result, string code) =>
        Assert.IsTrue(result.Report.Diagnostics.Any(item => item.Code == code), $"Diagnostic attendu : {code}");

    private static byte[] Encode(string body, char version = '5')
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var bytes = encoding.GetBytes($"BP-FILE-{version}" + body);
        for (var index = 0; index < bytes.Length; index++)
        {
            var position = V5Codec.Alphabet.IndexOf(bytes[index]);
            if (position >= 0)
                bytes[index] = V5Codec.Alphabet[(position + bytes.Length +
                    V5Codec.Deltas[(index + 1) % V5Codec.Deltas.Length]) % V5Codec.Alphabet.Length];
        }
        return bytes;
    }
}
