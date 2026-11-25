using System.Collections;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.BankPerfect;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.ExchangeRates;
using Moneta.Infrastructure.Storage;
using Moneta.Updates;

namespace Moneta.Tests;

[TestClass, DoNotParallelize]
public sealed class LocalizationTests
{
    [TestCleanup]
    public void RestoreFrench() => L.SetLanguage(L.French);

    [TestMethod]
    public void FrenchAndEnglishResourcesHaveMatchingKeysAndArguments()
    {
        var manager = new ResourceManager("Moneta.Localization.Resources.Strings", typeof(L).Assembly);
        var reference = Read(L.French);
        var english = Read(L.English);
        CollectionAssert.AreEquivalent(reference.Keys.ToArray(), english.Keys.ToArray());
        foreach (var (key, text) in reference)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(english[key]), key);
            Assert.IsFalse(english[key].Contains('▁'), key);
            if (!Regex.IsMatch(text, @"\{\d")) continue;
            Assert.AreEqual(CompositeFormat.Parse(text).MinimumArgumentCount,
                CompositeFormat.Parse(english[key]).MinimumArgumentCount, key);
        }

        Dictionary<string, string> Read(string language) =>
            manager.GetResourceSet(CultureInfo.GetCultureInfo(language), createIfNotExists: true, tryParents: false)!
                .Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [TestMethod]
    public void SwitchingLanguageUpdatesBindingsAndFrenchIsTheFallback()
    {
        L.SetLanguage(null);
        var notices = new List<string?>();
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => notices.Add(args.PropertyName);
        L.Current.PropertyChanged += OnChanged;
        try
        {
            Assert.AreEqual("Langue de Moneta", L.Current["Settings.Language"]);
            L.SetLanguage("en-us");
            Assert.AreEqual("Moneta language", L.Current["Settings.Language"]);
            L.SetLanguage(L.English);
            L.SetLanguage("unsupported");
            Assert.AreEqual(L.French, L.Current.CultureName);
            CollectionAssert.AreEqual(new[] { "CultureName", "Item[]", "CultureName", "Item[]" }, notices);
            Assert.ThrowsException<MissingManifestResourceException>(() => L.Get("Localization.MissingTestKey"));
        }
        finally { L.Current.PropertyChanged -= OnChanged; }
    }

    [TestMethod]
    public void DateAndMoneyUseTheSelectedLocaleWithoutGuessingInput()
    {
        var euro = new CurrencyRegistry().Get("EUR");
        var date = new DateOnly(2026, 2, 13);
        L.SetLanguage(L.French);
        Assert.IsTrue(L.TryReadDate("13/02/2026", out var frenchDate));
        Assert.AreEqual(date, frenchDate);
        Assert.IsFalse(L.TryReadDate("02/13/2026", out _));
        Assert.AreEqual(-1234.56m, MoneyInput.ParseLocalized("-1\u202f234,560", euro, allowNegative: true));
        Assert.AreEqual(1.10m, MoneyInput.ParseLocalized("1.10", euro));
        Assert.AreEqual(-1234.56m, LedgerSearchCriteria.ParseAmount("-1234,56"));
        StringAssert.Contains(MoneyFormatter.Format(1234.56m, euro), ",56");

        L.SetLanguage(L.English);
        Assert.IsTrue(L.TryReadDate("02/13/2026", out var englishDate));
        Assert.AreEqual(date, englishDate);
        Assert.IsFalse(L.TryReadDate("13/02/2026", out _));
        Assert.AreEqual(-1234.56m, MoneyInput.ParseLocalized("-1234.560", euro, allowNegative: true));
        Assert.AreEqual(1.10m, MoneyInput.ParseLocalized("1,10", euro));
        Assert.AreEqual(-1234.56m, LedgerSearchCriteria.ParseAmount("-1234.56"));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseLocalized("1,234", euro));
        Assert.ThrowsException<ArgumentException>(() => MoneyInput.ParseLocalized("1234.567", euro));
        Assert.AreEqual(1234.56m, LedgerSearchCriteria.ParseAmount("1,234.56"));
        StringAssert.Contains(MoneyFormatter.Format(1234.56m, euro), "1,234.56");
    }

    [TestMethod]
    public void LegacySettingsDefaultToFrenchAndExplicitEnglishSurvivesReload()
    {
        var directory = Directory.CreateTempSubdirectory("Moneta-language-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, """{"SchemaVersion":1,"Theme":"Dark","CheckUpdatesAtStartup":false,"RefreshRatesAtStartup":false}""");
            var settings = new SettingsStore(path);
            settings.Load();
            Assert.AreEqual(L.French, settings.Current.Language);
            settings.Save(settings.Current with { Language = L.English });
            var reopened = new SettingsStore(path);
            reopened.Load();
            Assert.AreEqual(L.English, reopened.Current.Language);
            Assert.AreEqual("Dark", reopened.Current.Theme);
            Assert.IsFalse(reopened.Current.CheckUpdatesAtStartup);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void ChangingLocaleRefreshesTheSameLedgerRowAndPreservesFinancialFile()
    {
        var directory = Directory.CreateTempSubdirectory("Moneta-language-ledger-");
        try
        {
            var path = Path.Combine(directory.FullName, "ledger.moneta");
            L.SetLanguage(L.French);
            var document = FinancialDocument.New("Dossier fictif");
            var euro = document.Currencies.Single(currency => currency.Code == "EUR");
            var account = new Account(Guid.NewGuid(), "Compte personnel", euro, 1234.56m);
            var payee = new LedgerPayee(Guid.NewGuid(), "Mon tiers");
            var transaction = new Transaction(Guid.NewGuid(), account.Id, new(2026, 2, 13), "Mon libellé", new(payee.Name),
                new(""), 1m, euro, TransactionDirection.Credit, ReconciliationStatus.Unmarked, "Mes notes") { PayeeId = payee.Id };
            var snapshot = document with { Accounts = [account], SelectedAccountId = account.Id, Payees = [payee] };
            using (MonetaFile.Create(path, snapshot, [new(transaction, 0)])) { }
            var originalBytes = SHA256.HashData(File.ReadAllBytes(path));
            using (var file = MonetaFile.Open(path))
            {
                var model = new MainViewModel(file.Snapshot, file);
                var row = model.Rows.Single();
                model.SelectedRow = row;
                Assert.AreEqual("13/02/2026", row.DateText);
                L.SetLanguage(L.English);
                model.RefreshDisplayLanguage();
                Assert.AreSame(row, model.Rows.Single());
                Assert.AreSame(row, model.SelectedRow);
                Assert.AreEqual("02/13/2026", row.DateText);
                Assert.AreEqual("uncleared", row.MarkText);
                Assert.AreEqual("Mon libellé", row.Label);
                Assert.AreEqual("Mon tiers", row.PayeeName);
                Assert.AreEqual(1235.56m, model.SelectedAccount!.Balance);
                Assert.IsFalse(model.IsDirty);
                var backup = Path.Combine(directory.FullName, "backup.moneta");
                MonetaBackup.Create(file, backup);
                using (var restored = MonetaBackup.Restore(backup, Path.Combine(directory.FullName, "restored.moneta")))
                {
                    Assert.AreEqual(file.Snapshot.LedgerDigest, restored.Snapshot.LedgerDigest);
                    Assert.AreEqual(1235.56m, restored.Snapshot.AccountBalances![account.Id]);
                }
                L.SetLanguage(L.French);
                model.RefreshDisplayLanguage();
                Assert.AreEqual("13/02/2026", row.DateText);
            }
            CollectionAssert.AreEqual(originalBytes, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void LocaleDoesNotChangeHistoricalProofOrScheduledDates()
    {
        var directory = Directory.CreateTempSubdirectory("Moneta-language-proof-");
        try
        {
            var date = new DateOnly(2026, 1, 31);
            var proof = new HistoricalProof(HistoricalProof.CurrentRules,
                new(HistoricalUse.Conversion, date, 1234.56m, "EUR"),
                "EUR", 2, HistoricalMethod.Identity, MidpointRounding.ToEven, null, 1234.56m);
            var recurrence = new ScheduleRule(ScheduleUnit.Month, EndOfMonth: true);
            var french = Path.Combine(directory.FullName, "french.json");
            var english = Path.Combine(directory.FullName, "english.json");
            L.SetLanguage(L.French);
            HistoricalProofFile.WriteNew(french, proof);
            var due = recurrence.At(date, 1);
            L.SetLanguage(L.English);
            Assert.AreEqual(due, recurrence.At(date, 1));
            Assert.AreEqual(1234.56m, HistoricalProofFile.ReadVerified(french).Reproduce());
            HistoricalProofFile.WriteNew(english, proof);
            CollectionAssert.AreEqual(File.ReadAllBytes(french), File.ReadAllBytes(english));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void ImportAndUpdaterKeepTheirValidationAcrossLanguages()
    {
        L.SetLanguage(L.French);
        var original = V5Reader.Read(ReadOnlySpan<byte>.Empty).Report.Diagnostics.Single();
        var french = Assert.ThrowsException<InvalidDataException>(() => UpdateManifest.Verify([], [])).Message;
        L.SetLanguage(L.English);
        var current = V5Reader.Read(ReadOnlySpan<byte>.Empty).Report.Diagnostics.Single();
        Assert.AreEqual(original.Code, current.Code);
        Assert.AreEqual(original.Message, current.Message);
        Assert.AreNotEqual(current.Message, L.Diagnostic(current.Message));
        var english = Assert.ThrowsException<InvalidDataException>(() => UpdateManifest.Verify([], [])).Message;
        Assert.AreNotEqual(french, english);
    }
}
