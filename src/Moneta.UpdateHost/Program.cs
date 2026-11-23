using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Moneta.Localization;
using Moneta.Updates;

if (args.Length != 3 || !Guid.TryParseExact(args[0], "N", out var id) ||
    !int.TryParse(args[1], CultureInfo.InvariantCulture, out var pid) || pid < 1 ||
    !long.TryParse(args[2], CultureInfo.InvariantCulture, out var startTime))
    return 2;

var stage = new UpdateStage(id);
if (!string.Equals(Environment.ProcessPath, stage.Helper, StringComparison.OrdinalIgnoreCase) ||
    !OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
    return 2;

try
{
    using var parent = FindParent(pid);
    if (parent is not null && !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == startTime &&
        !parent.WaitForExit(120_000))
        return 3;
}
catch (Exception error) when (error is InvalidOperationException or Win32Exception)
{
    // Without a valid parent and a recorded close decision, there is no installation.
    if (!File.Exists(stage.Ready)) return 3;
}

if (!File.Exists(stage.Ready)) return 3;
var result = Install(stage);
try
{
    File.WriteAllText(stage.Result, result);
    var app = new ProcessStartInfo(UpdateChannel.InstalledExecutable) { UseShellExecute = true };
    app.ArgumentList.Add("--update-result");
    app.ArgumentList.Add(stage.Id);
    using var restarted = Process.Start(app);
    if (restarted is null) throw new InvalidOperationException();
    return result == "installed" ? 0 : 4;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
{
    L.SetLanguage(Environment.GetEnvironmentVariable(UpdateChannel.HelperLanguageVariable));
    _ = MessageBoxW(nint.Zero, L.Get("Updates.CannotRestartMoneta"), L.Get("Updates.MonetaUpdate"), 0x30);
    return 5;
}

static Process? FindParent(int pid)
{
    try { return Process.GetProcessById(pid); }
    catch (ArgumentException) { return null; }
}

static string Install(UpdateStage stage)
{
    try
    {
        using var manifestFile = new FileStream(stage.Envelope, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (manifestFile.Length is <= 0 or > UpdateManifest.MaximumEnvelopeBytes)
            return "invalid";
        var envelope = new byte[manifestFile.Length];
        manifestFile.ReadExactly(envelope);
        var manifest = UpdateChannel.Current.Verify(envelope);
        var file = FileVersionInfo.GetVersionInfo(UpdateChannel.InstalledExecutable);
        var previous = new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart);
        if (!manifest.IsNewerThan(previous) || !UpdateManifest.TryVersion(manifest.Version, out var expected))
            return "invalid";

        // Open without write or delete sharing, keeping the authenticated bytes stable through launch.
        using var verified = UpdateClient.OpenVerifiedInstaller(stage.Installer, manifest);
        var installer = new ProcessStartInfo(stage.Installer) { UseShellExecute = true };
        foreach (var option in new[] { "/SP-", "/SILENT", "/NORESTART", "/RESTARTEXITCODE=3010" })
            installer.ArgumentList.Add(option);
        using var process = Process.Start(installer);
        if (process is null) return "failed";
        process.WaitForExit();
        if (process.ExitCode == 3010) return "restart";
        return process.ExitCode == 0 && UpdateClient.MatchesVersion(UpdateChannel.InstalledExecutable, expected)
            ? "installed" : "failed";
    }
    catch (Win32Exception error) when (error.NativeErrorCode == 1223)
    {
        return "cancelled";
    }
    catch (InvalidDataException) { return "invalid"; }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
    {
        return "failed";
    }
}

[DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
static extern int MessageBoxW(nint window, string message, string caption, uint type);
