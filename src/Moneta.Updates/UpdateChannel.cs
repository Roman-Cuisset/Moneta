using System.Runtime.InteropServices;

namespace Moneta.Updates;

public sealed class UpdateChannel
{
    private const string Repository = "https://github.com/Roman-Cuisset/Moneta/releases/";
    private const string Loopback = "http://127.0.0.1:18761/";
    public const string HelperLanguageVariable = "MONETA_UPDATE_LANGUAGE";
#if MONETA_LAB
    public const string InstallName = "Moneta Lab";
    public const string RunningMutex = @"Local\Moneta.Lab.Running.v1";
    public static UpdateChannel Current { get; } = new(true, ReadKey());
#else
    public const string InstallName = "Moneta";
    public const string RunningMutex = @"Local\Moneta.Alpha.Running.v1";
    public static UpdateChannel Current { get; } = new(false, ReadKey());
#endif

    internal UpdateChannel(bool lab, byte[] publicKey)
    {
        Lab = lab;
        PublicKey = publicKey;
    }

    public bool Lab { get; }
    internal byte[] PublicKey { get; }
    public Uri Feed => new((Lab ? Loopback : Repository + "latest/download/") + UpdateManifest.EnvelopeName);
    public Uri Installer(UpdateManifest manifest) => new((Lab ? Loopback : Repository + $"download/v{manifest.Version}/") + manifest.Installer);
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", InstallName);
    public static string InstalledExecutable => Path.Combine(InstallDirectory, "Moneta.App.exe");
    public static string StagingRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallName, "Updates");
    public static bool SupportsInstall => OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 &&
        string.Equals(Environment.ProcessPath, InstalledExecutable, StringComparison.OrdinalIgnoreCase);

    internal bool PermitsRedirect(Uri uri) =>
        uri.UserInfo.Length == 0 && (Lab
            ? uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1" && uri.Port == 18761
            : uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
              uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com");

    public UpdateManifest Verify(ReadOnlySpan<byte> envelope) => UpdateManifest.Verify(envelope, PublicKey);

    private static byte[] ReadKey()
    {
        using var stream = typeof(UpdateChannel).Assembly.GetManifestResourceStream("Moneta.UpdateKey")
            ?? throw new InvalidOperationException(L.Get("UpdateChannel.MonetaPublicKeyAbsent"));
        using var reader = new StreamReader(stream);
        return Convert.FromBase64String(reader.ReadToEnd().Trim());
    }
}
