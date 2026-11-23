using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace Moneta.Updates;

public sealed record UpdateOffer(UpdateManifest Manifest, byte[] SignedEnvelope);

public sealed class UpdateClient : IDisposable
{
    private readonly UpdateChannel _channel;
    private readonly HttpClient _http;

    public UpdateClient(UpdateChannel channel) : this(channel, new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = !channel.Lab
    }) { }

    internal UpdateClient(UpdateChannel channel, HttpMessageHandler handler)
    {
        _channel = channel;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Moneta-Updater/1");
    }

    public async Task<UpdateOffer?> CheckAsync(Version installed, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await GetAsync(_channel.Feed, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > UpdateManifest.MaximumEnvelopeBytes)
            throw new InvalidDataException(L.Get("UpdateClient.TooLargeAnUpdateCatalog"));
        using var buffer = new MemoryStream();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await CopyBoundedAsync(body, buffer, UpdateManifest.MaximumEnvelopeBytes, null, timeout.Token).ConfigureAwait(false);
        var envelope = buffer.ToArray();
        var manifest = _channel.Verify(envelope);
        return manifest.IsNewerThan(installed) ? new UpdateOffer(manifest, envelope) : null;
    }

    public async Task<UpdateStage> DownloadAsync(UpdateOffer offer, Version installed, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        var manifest = _channel.Verify(offer.SignedEnvelope);
        if (manifest != offer.Manifest || !manifest.IsNewerThan(installed))
            throw new InvalidDataException(L.Get("UpdateClient.TheUpdateOfferHasChangedOrWouldCause"));

        using var response = await GetAsync(_channel.Installer(manifest), token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != manifest.Size)
            throw new InvalidDataException(L.Get("UpdateClient.TheAnnouncedSizeOfTheInstallerHasChanged"));

        var stage = new UpdateStage(Guid.NewGuid());
        Directory.CreateDirectory(stage.Directory);
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (var file = new FileStream(stage.Installer, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await CopyBoundedAsync(body, file, manifest.Size, progress, token).ConfigureAwait(false);
            using (var file = OpenVerifiedInstaller(stage.Installer, manifest)) { }
            await File.WriteAllBytesAsync(stage.Envelope, offer.SignedEnvelope, token).ConfigureAwait(false);
            if (!_channel.Lab)
                await File.WriteAllTextAsync(stage.Installer + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", token).ConfigureAwait(false);
            progress?.Report(100);
            return stage;
        }
        catch
        {
            stage.Clean();
            throw;
        }
    }

    public static FileStream OpenVerifiedInstaller(string path, UpdateManifest manifest)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != manifest.Size ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException(L.Get("UpdateClient.TheInstallerSFootprintDoesNotMatchThe"));
            if (!UpdateManifest.TryVersion(manifest.Version, out var expected) ||
                !MatchesVersion(path, expected, requireInstaller: true))
                throw new InvalidDataException(L.Get("UpdateClient.TheWindowsVersionOfTheInstallerDoesNot"));
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static bool MatchesVersion(string path, Version expected, bool requireInstaller = false)
    {
        if (!File.Exists(path)) return false;
        var info = FileVersionInfo.GetVersionInfo(path);
        return (!requireInstaller || info.ProductName?.TrimEnd() == "Moneta") &&
            info.FileMajorPart == expected.Major && info.FileMinorPart == expected.Minor &&
            info.FileBuildPart == expected.Build && info.FilePrivatePart == 0;
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; redirects < 5; redirects++)
        {
            if (!_channel.PermitsRedirect(uri)) throw new InvalidDataException(L.Get("UpdateClient.UnauthorizedUpdateAddress"));
            var reply = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)reply.StatusCode is not (301 or 302 or 303 or 307 or 308)) return reply;
            using (reply)
            {
                if (reply.Headers.Location is not { } location)
                    throw new InvalidDataException(L.Get("UpdateClient.IncompleteUpdateRedirection"));
                uri = new Uri(uri, location);
            }
        }
        throw new InvalidDataException(L.Get("UpdateClient.TooManyRedirectionsDuringTheUpdate"));
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long limit, IProgress<int>? progress, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException(L.Get("UpdateClient.TheResponseIsGreaterThanTheAuthorizedSize"));
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            var percent = (int)(total * 99 / limit);
            if (progress is not null && percent != lastPercent) { lastPercent = percent; progress.Report(percent); }
        }
    }

    public void Dispose() => _http.Dispose();
}
