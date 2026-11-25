using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.App;

namespace Moneta.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    private string _directory = null!;
    private string FilePath => Path.Combine(_directory, "settings.json");
    [TestInitialize] public void Initialize() => _directory = Path.Combine(Path.GetTempPath(), "Moneta-settings-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [TestMethod]
    public void FirstLaunchDoesNotWriteOrEnableNetworkPreferences()
    {
        var store = new SettingsStore(FilePath); store.Load();
        Assert.AreEqual("Light", store.Current.Theme);
        Assert.IsFalse(store.Current.CompactRegister);
        Assert.IsTrue(store.Current.CheckUpdatesAtStartup); Assert.IsFalse(store.Current.RefreshRatesAtStartup);
        Assert.IsFalse(File.Exists(FilePath));
    }
    [TestMethod]
    public void PreferencesSurviveReplacementAndNewSession()
    {
        var store = new SettingsStore(FilePath); store.Load();
        store.Save(new LocalSettings { Theme = "Dark", CheckUpdatesAtStartup = true });
        store.Save(store.Current with { RefreshRatesAtStartup = true, CompactRegister = true });
        var reopened = new SettingsStore(FilePath); reopened.Load();
        Assert.AreEqual("Dark", reopened.Current.Theme);
        Assert.IsTrue(reopened.Current.CompactRegister);
        Assert.IsTrue(reopened.Current.CheckUpdatesAtStartup); Assert.IsTrue(reopened.Current.RefreshRatesAtStartup);
        CollectionAssert.AreEquivalent(new[] { FilePath }, Directory.GetFiles(_directory));
    }
    [DataTestMethod]
    [DataRow("{broken")]
    [DataRow("{\"SchemaVersion\":999,\"Theme\":\"Dark\",\"CheckUpdatesAtStartup\":false,\"RefreshRatesAtStartup\":false}")]
    [DataRow("{\"SchemaVersion\":1,\"Theme\":\"Dark\"}")]
    public void InvalidPreferencesArePreservedAndCannotBeOverwritten(string content)
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(FilePath, content);
        var before = File.ReadAllBytes(FilePath); var store = new SettingsStore(FilePath);
        Assert.ThrowsException<SettingsException>(store.Load);
        Assert.ThrowsException<SettingsException>(() => store.Save(new LocalSettings()));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(FilePath));
    }
    [TestMethod]
    public void PreferencesInvalidatedAfterLoadRemainUntouched()
    {
        var store = new SettingsStore(FilePath); store.Load(); store.Save(new LocalSettings { Theme = "Dark" });
        File.WriteAllText(FilePath, "{invalid");
        Assert.ThrowsException<SettingsException>(() => store.Save(new LocalSettings()));
        Assert.AreEqual("{invalid", File.ReadAllText(FilePath)); Assert.AreEqual("Dark", store.Current.Theme);
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
    }
    [TestMethod]
    public void FailedWriteDoesNotChangeCommittedPreferences()
    {
        Directory.CreateDirectory(_directory);
        var parent = Path.Combine(_directory, "blocked"); File.WriteAllText(parent, "not a directory");
        var store = new SettingsStore(Path.Combine(parent, "settings.json")); store.Load();
        Assert.ThrowsException<SettingsException>(() => store.Save(new LocalSettings { Theme = "Dark" }));
        Assert.AreEqual("Light", store.Current.Theme); Assert.AreEqual("not a directory", File.ReadAllText(parent));
    }
    [TestMethod]
    public void CompleteLegacyPreferencesUpgradeWithoutLosingChoices()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"SchemaVersion":1,"Theme":"Dark","CheckUpdatesAtStartup":true,"RefreshRatesAtStartup":true}""");
        var store = new SettingsStore(FilePath); store.Load();
        Assert.AreEqual("NBRB", store.Current.RateSource);
        Assert.IsFalse(store.Current.CompactRegister);
        store.Save(store.Current with { RateSource = "CBR" });
        var reopened = new SettingsStore(FilePath); reopened.Load();
        Assert.AreEqual("CBR", reopened.Current.RateSource);
        Assert.AreEqual("Dark", reopened.Current.Theme);
        Assert.IsTrue(reopened.Current.RefreshRatesAtStartup && reopened.Current.CheckUpdatesAtStartup);
    }

    [DataTestMethod]
    [DataRow("""{"SchemaVersion":2,"Theme":"Light","CheckUpdatesAtStartup":false,"RefreshRatesAtStartup":false}""")]
    [DataRow("""{"SchemaVersion":2,"Theme":"Light","CheckUpdatesAtStartup":false,"RefreshRatesAtStartup":false,"RateSource":"UNKNOWN"}""")]
    public void MissingOrUnknownRateProviderPreservesPreferences(string contents)
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(FilePath, contents);
        var store = new SettingsStore(FilePath);
        Assert.ThrowsException<SettingsException>(store.Load);
        Assert.ThrowsException<SettingsException>(() => store.Save(new LocalSettings()));
        Assert.AreEqual(contents, File.ReadAllText(FilePath));
    }

    [TestMethod]
    public void WindowGeometryRoundTripsAndInvalidValuesAreIgnored()
    {
        var store = new SettingsStore(FilePath); store.Load();
        store.Save(store.Current with
        {
            WindowLeft = -120,
            WindowTop = -80,
            WindowWidth = 1280,
            WindowHeight = 800,
            WindowMaximized = true
        });
        var reopened = new SettingsStore(FilePath); reopened.Load();
        Assert.AreEqual(-120, reopened.Current.WindowLeft);
        Assert.AreEqual(-80, reopened.Current.WindowTop);
        Assert.AreEqual(1280, reopened.Current.WindowWidth);
        Assert.AreEqual(800, reopened.Current.WindowHeight);
        Assert.IsTrue(reopened.Current.WindowMaximized);

        var invalidJson = File.ReadAllText(FilePath).Replace("\"WindowWidth\": 1280", "\"WindowWidth\": 0");
        File.WriteAllText(FilePath, invalidJson);
        var invalid = new SettingsStore(FilePath); invalid.Load();
        Assert.IsNull(invalid.Current.WindowLeft);
        Assert.IsNull(invalid.Current.WindowTop);
        Assert.IsNull(invalid.Current.WindowWidth);
        Assert.IsNull(invalid.Current.WindowHeight);
        Assert.IsFalse(invalid.Current.WindowMaximized);
    }

    [TestMethod]
    public void InvalidProviderCannotReplaceCommittedSettings()
    {
        var store = new SettingsStore(FilePath); store.Load();
        store.Save(new LocalSettings { RateSource = "CBR" });
        var before = File.ReadAllBytes(FilePath);
        Assert.ThrowsException<SettingsException>(() => store.Save(store.Current with { RateSource = "Other" }));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(FilePath));
        Assert.AreEqual("CBR", store.Current.RateSource);
    }
}
