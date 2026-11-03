using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;
using Moneta.Core;
using Moneta.Core.Presentation;
using Moneta.Infrastructure.ExchangeRates;

namespace Moneta.Tests;

[TestClass]
public sealed class RateDisplayTests
{
    private static readonly DateOnly Day = new(2026, 4, 3);

    [TestMethod]
    public void PresentationTargetsDoNotAddCurrenciesToFinancialDocument()
    {
        var byn = new Currency("BYN", "BYN", 2);
        var account = new Account(Guid.NewGuid(), "Compte synthétique", byn, 1000m);
        var document = FinancialDocument.New("Test") with { Currencies = [byn], Accounts = [account], SelectedAccountId = account.Id };
        var model = new MainViewModel(document);
        var rates = ExchangeRateSessionTests.Sample().Rates;
        model.UpdateRateDisplay(rates, RateProvider.Nbrb, Day, true, "Cache");
        model.DisplayCurrency = model.DisplayCurrencies.Single(currency => currency.Code == "EUR");
        Assert.AreEqual("333,33 EUR", model.DisplayBalanceText);
        StringAssert.Contains(model.NativeBalanceDescription, "1 000,00 BYN");
        StringAssert.Contains(model.DisplayRateDescription, "NBRB");
        StringAssert.Contains(model.DisplayRateDescription, "03/04/2026");
        Assert.AreEqual(1000m, model.SelectedAccount!.Balance);
        Assert.AreEqual(1, model.CaptureDocument().Currencies.Count);
        Assert.AreEqual(1, model.CurrencyRegistry.Currencies.Count);
        Assert.IsFalse(model.IsDirty);
        model.DisplayCurrency = model.DisplayCurrencies.Single(currency => currency.Code == "RUB");
        Assert.AreEqual("20 000,00 RUB", model.DisplayBalanceText);
        var cbr = ExchangeRateSessionTests.Sample(RateProvider.Cbr).Rates;
        model.UpdateRateDisplay(cbr, RateProvider.Cbr, Day, true, "Cache CBR");
        Assert.AreEqual("30 000,00 RUB", model.DisplayBalanceText);
        model.DisplayCurrency = byn;
        Assert.AreEqual("1 000,00 BYN", model.DisplayBalanceText);
        Assert.AreEqual(string.Empty, model.NativeBalanceDescription);
    }

    [TestMethod]
    public void FutureOldAndWrongProviderSetsCannotBecomeBalanceEstimates()
    {
        var model = new MainViewModel(Moneta.Infrastructure.DemoData.Create());
        var before = model.SelectedAccount!.Balance;
        foreach (var invalid in new[]
        {
            ExchangeRateSessionTests.Sample(effective: Day.AddDays(1)).Rates,
            ExchangeRateSessionTests.Sample(requested: Day.AddDays(-32)).Rates,
            ExchangeRateSessionTests.Sample(RateProvider.Cbr).Rates
        })
            Assert.ThrowsException<ArgumentException>(() => model.UpdateRateDisplay(invalid, RateProvider.Nbrb, Day, true, "Invalid"));
        Assert.AreEqual(before, model.SelectedAccount.Balance);
        model.UpdateRateDisplay(null, RateProvider.Nbrb, Day, true, "No publication");
        model.DisplayCurrency = model.DisplayCurrencies.Single(currency => currency.Code == "BYN");
        Assert.AreEqual("Cours indisponible", model.DisplayBalanceText);
        Assert.AreEqual(before, model.SelectedAccount.Balance);
    }

    [TestMethod]
    public void CacheAndDisplayChangesPreserveSavedTransfersPointingReconciliationAndSchemaFive()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Moneta-rate-isolation-" + Guid.NewGuid())).FullName;
        var file = Path.Combine(directory, "financial.moneta");
        try
        {
            using var workspace = new DocumentWorkspace();
            workspace.New("Dossier synthétique isolé");
            var model = workspace.Model!;
            var byn = model.AddAccount("BYN", model.CurrencyRegistry.Get("BYN"), null, 5000m, null);
            var eur = model.AddAccount("EUR", model.CurrencyRegistry.Get("EUR"), null, 100m, null);
            model.SelectedAccount = byn;
            var transfer = model.CreateTransfer(new(byn.Account.Id, eur.Account.Id, Day, 500m, 140m));
            workspace.Save(file);
            model.SetPointing([new(transfer.Debit.Id, 1)], true, Day);
            workspace.Save();
            model.NavigateToTransferCounterpart(transfer.Value.Id, transfer.Debit.Id);
            model.StageReconciliation(new(Day, 240m), [transfer.Credit.Id]);
            workspace.Save();
            model.SelectedAccount = byn;
            var original = model.GetTransfer(transfer.Value.Id);
            var snapshot = JsonSerializer.Serialize(model.CaptureDocument());
            var database = SharedBytes(file);
            var wal = File.Exists(file + "-wal") ? SharedBytes(file + "-wal") : null;
            var cache = new SqliteRateCache(Path.Combine(directory, "public-rates.sqlite"));
            foreach (var provider in new[] { RateProvider.Nbrb, RateProvider.Cbr })
            {
                cache.Store(ExchangeRateSessionTests.Sample(provider));
                model.UpdateRateDisplay(cache.Find(provider, Day), provider, Day, true, "Cached official publication");
                foreach (var code in new[] { "EUR", "RUB", "BYN" })
                    model.DisplayCurrency = model.DisplayCurrencies.Single(currency => currency.Code == code);
            }
            Assert.AreEqual(snapshot, JsonSerializer.Serialize(model.CaptureDocument()));
            Assert.AreEqual(original, model.GetTransfer(transfer.Value.Id));
            Assert.AreEqual(500m, original.Debit.Amount);
            Assert.AreEqual(140m, original.Credit.Amount);
            Assert.IsTrue(original.HasReconciledLeg);
            Assert.AreEqual(ReconciliationStatus.Marked, original.Debit.Status);
            Assert.AreEqual(5, model.CaptureDocument().SchemaVersion);
            Assert.IsFalse(workspace.HasUnsavedChanges);
            CollectionAssert.AreEqual(database, SharedBytes(file));
            if (wal is not null) CollectionAssert.AreEqual(wal, SharedBytes(file + "-wal"));
            Assert.ThrowsException<InvalidOperationException>(() => model.DeleteTransfer(original.Value.Id, original.Value.Revision));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static byte[] SharedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
