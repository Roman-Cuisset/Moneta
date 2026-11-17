using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Infrastructure.BankPerfect;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class BankPerfectLoadTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SyntheticRegisterRoundTripsAtSelectedVolume()
    {
        var configured = Environment.GetEnvironmentVariable("MONETA_BP_TEST_LOAD");
        var count = configured is null ? 1_000 : int.Parse(configured, CultureInfo.InvariantCulture);
        Assert.IsTrue(count is 1_000 or 10_000 or 50_000, "Volumes pris en charge par le protocole.");
        var directory = Directory.CreateTempSubdirectory("MonetaBpLoad-").FullName;
        try
        {
            var path = Path.Combine(directory, "synthétique.bp");
            var target = Path.Combine(directory, "test.moneta");
            var source = BankPerfectSyntheticCorpus.Create(count);
            File.WriteAllBytes(path, source);
            var original = SHA256.HashData(source);
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            var initialPrivate = process.PrivateMemorySize64;
            var initialResident = process.WorkingSet64;
            var observedPrivate = initialPrivate;
            var observedResident = initialResident;
            var times = new Dictionary<string, double>();
            var thread = Environment.CurrentManagedThreadId;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "Moneta.BankPerfect.V5" or "Moneta.BankPerfect.Import",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (Environment.CurrentManagedThreadId != thread) return;
                    times.Add(activity.OperationName, activity.Duration.TotalMilliseconds);
                    process.Refresh();
                    observedPrivate = Math.Max(observedPrivate, process.PrivateMemorySize64);
                    observedResident = Math.Max(observedResident, process.WorkingSet64);
                }
            };
            ActivitySource.AddActivityListener(listener);
            var timer = Stopwatch.StartNew();
            var preview = BankPerfectImporter.Prepare(path);
            var previewMs = timer.Elapsed.TotalMilliseconds;
            Assert.IsTrue(preview.Report.CanImport, string.Join(", ", preview.Report.Issues.Select(issue => issue.Code)));
            var imported = BankPerfectImporter.Import(preview, target);
            var totalMs = timer.Elapsed.TotalMilliseconds;
            process.Refresh();
            var cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            using var file = MonetaFile.Open(target);
            Assert.AreEqual(count, file.Snapshot.OperationCount);
            Assert.AreEqual(count / 100, file.Snapshot.TransferCount);
            Assert.AreEqual(imported.ProvenanceSha256, file.Snapshot.BankPerfectProvenanceHash);
            Assert.IsTrue(imported.BackupRestoreVerified);
            CollectionAssert.AreEqual(original, SHA256.HashData(File.ReadAllBytes(path)));
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                count, sourceBytes = source.Length, targetBytes = new FileInfo(target).Length,
                previewMs, totalMs, cpuMs, initialPrivate, initialResident, observedPrivate, observedResident, times
            }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
