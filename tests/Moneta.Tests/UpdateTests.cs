using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Updates;

namespace Moneta.Tests;

[TestClass]
public sealed class UpdateTests
{
    [TestMethod]
    public void VerifiesTheExactNotesAndAuthenticationBeforeReadingThePayload()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Manifest() with { ReleaseNotes = "## Quoi de neuf ?\nRéparation de l’affichage BYN." };
        var verified = UpdateManifest.Verify(Pack(manifest, signer), signer.ExportSubjectPublicKeyInfo());
        Assert.AreEqual(manifest, verified);

        var signed = JsonSerializer.Deserialize(Pack(manifest, signer), UpdateJson.Default.UpdateEnvelope)!;
        signed.Payload[3] ^= 1;
        Assert.ThrowsException<InvalidDataException>(() =>
            UpdateManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(signed, UpdateJson.Default.UpdateEnvelope), signer.ExportSubjectPublicKeyInfo()));
        Assert.ThrowsException<InvalidDataException>(() =>
            UpdateManifest.Verify(Pack(manifest, stranger), signer.ExportSubjectPublicKeyInfo()));
    }

    [TestMethod]
    public void SignedPayloadStillRequiresACompatibleInstallerAndReasonableBounds()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = signer.ExportSubjectPublicKeyInfo();
        var original = Manifest();
        foreach (var invalid in new[] {
            original with { Architecture = "win-arm64" },
            original with { Installer = "../setup.exe" },
            original with { Version = "0.01.2" },
            original with { Version = "0.1.2-preview" },
            original with { Size = 0 },
            original with { Size = UpdateManifest.MaximumInstallerBytes + 1 },
            original with { Sha256 = "invalide" },
            original with { ReleaseNotes = " " }
        })
            Assert.ThrowsException<InvalidDataException>(() => UpdateManifest.Verify(Pack(invalid, signer), key));
    }

    [TestMethod]
    public async Task CurrentAndOlderVersionsDoNotOpenAnUpdateOffer()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        var envelope = Pack(Manifest(), signer);
        using var client = new UpdateClient(channel, new Handler(request => IsIndex(request)
            ? Index(Release("v0.1.1"))
            : Reply(envelope)));

        Assert.IsNotNull(await client.CheckAsync(new Version(0, 1, 0, 0), default));
        Assert.IsNull(await client.CheckAsync(new Version(0, 1, 1, 0), default));
        Assert.IsNull(await client.CheckAsync(new Version(0, 2, 0, 0), default));
    }

    [TestMethod]
    public async Task EmptyAlphaIndexDoesNotRequestACatalog()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        var handler = new Handler(request =>
        {
            Assert.IsTrue(IsIndex(request));
            return Reply("[]"u8.ToArray());
        });
        using var client = new UpdateClient(channel, handler);

        Assert.IsNull(await client.CheckAsync(new Version(0, 2, 0), default));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task MissingCatalogIsNoOfferAndApiFailuresSurfaceAsNetworkErrors()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        using var missing = new UpdateClient(channel, new Handler(request => IsIndex(request)
            ? Index(Release("v0.2.1"))
            : new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.IsNull(await missing.CheckAsync(new Version(0, 2, 0), default));

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden,
                     HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            using var failed = new UpdateClient(channel, new Handler(_ => new HttpResponseMessage(status)));
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => failed.CheckAsync(new Version(0, 2, 0), default));
        }

        using var offline = new UpdateClient(channel, new Handler(_ => throw new HttpRequestException("Hors ligne")));
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => offline.CheckAsync(new Version(0, 2, 0), default));
    }

    [TestMethod]
    public async Task AlphaDiscoverySelectsHighestEligibleVersionAndUsesOnlyOfficialAssetUrls()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        var manifest = Manifest() with { Version = "0.2.10", Installer = "Moneta-0.2.10-win-x64.exe" };
        var handler = new Handler(request =>
        {
            if (IsIndex(request))
                return Index(Release("v0.2.9"), Release("v0.2.10"), Release("v1.0.0", prerelease: false),
                    Release("v0.3.0", draft: true), Release("v0.4.0", withCatalog: false),
                    Release("v0.02.11"), Release("v0.2.11-beta"), Release("v0.2.11", state: "new"));
            Assert.AreEqual("https://github.com/Roman-Cuisset/Moneta/releases/download/v0.2.10/moneta-update.json",
                request.RequestUri!.AbsoluteUri);
            return Reply(Pack(manifest, signer));
        });
        using var client = new UpdateClient(channel, handler);

        var offer = await client.CheckAsync(new Version(0, 2, 0), default);

        Assert.AreEqual(manifest, offer?.Manifest);
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task AlphaIndexCannotAuthorizeAnInvalidSignatureOrDifferentSignedVersion()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        var future = Manifest() with { Version = "0.2.1", Installer = "Moneta-0.2.1-win-x64.exe" };
        foreach (var envelope in new[] { Pack(future, stranger), Pack(Manifest(), signer) })
        {
            using var client = new UpdateClient(channel, new Handler(request => IsIndex(request)
                ? Index(Release("v0.2.1"))
                : Reply(envelope)));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.CheckAsync(new Version(0, 2, 0), default));
        }
    }

    [TestMethod]
    public async Task AlphaDiscoveryRejectsInvalidIndexAndDoesNotFollowIndexRedirects()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        using var invalid = new UpdateClient(channel, new Handler(_ => Reply("{}"u8.ToArray())));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => invalid.CheckAsync(new Version(0, 2, 0), default));
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://github.com/Roman-Cuisset/Moneta") } });
        using var redirected = new UpdateClient(channel, handler);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => redirected.CheckAsync(new Version(0, 2, 0), default));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task RedirectsCannotSwitchHostsOrDowngradeTls()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        foreach (var redirect in new[] { "http://github.com/installer", "https://example.org/installer", "https://github.com:444/installer" })
        {
            var handler = new Handler(request =>
            {
                if (IsIndex(request)) return Index(Release("v0.1.1"));
                var reply = new HttpResponseMessage(HttpStatusCode.Redirect);
                reply.Headers.Location = new Uri(redirect);
                return reply;
            });
            using var client = new UpdateClient(channel, handler);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.CheckAsync(new Version(0, 1, 0), default));
            Assert.AreEqual(2, handler.Calls);
        }
    }

    [TestMethod]
    public async Task OversizedCatalogIsRejectedAndCancellationPropagates()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        using var client = new UpdateClient(channel, new Handler(request => IsIndex(request)
            ? Index(Release("v0.1.1"))
            : Reply(new byte[UpdateManifest.MaximumEnvelopeBytes + 1])));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.CheckAsync(new Version(0, 1, 0), default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await client.CheckAsync(new Version(0, 1, 0), cancellation.Token);
            Assert.Fail("Une recherche annulée ne doit pas continuer.");
        }
        catch (OperationCanceledException) { }
    }

    [TestMethod]
    public async Task InstallerEndingBeforeItsDeclaredLengthIsRejected()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var completeInstaller = new byte[] { 1, 2, 3, 4 };
        var manifest = Manifest() with
        {
            Size = completeInstaller.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(completeInstaller))
        };
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        using var client = new UpdateClient(channel, new Handler(_ =>
        {
            var content = new ByteArrayContent(completeInstaller[..^1]);
            content.Headers.ContentLength = manifest.Size;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var offer = new UpdateOffer(manifest, Pack(manifest, signer));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            client.DownloadAsync(offer, new Version(0, 1, 0), null, default));
    }

    [TestMethod]
    public void HashMismatchIsRejectedBeforeTheFileCanBeExecuted()
    {
        var path = Path.Combine(Path.GetTempPath(), "Moneta-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var manifest = Manifest() with { Size = 3 };
            Assert.ThrowsException<InvalidDataException>(() => UpdateClient.OpenVerifiedInstaller(path, manifest));
            using var unlocked = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    private static UpdateManifest Manifest() =>
        new(1, "0.1.1", "win-x64", "Moneta-0.1.1-win-x64.exe", 40, new string('a', 64), "Notes fictives");

    private static bool IsIndex(HttpRequestMessage request) => request.RequestUri ==
        new Uri("https://api.github.com/repos/Roman-Cuisset/Moneta/releases?per_page=30");

    private static object Release(string tag, bool prerelease = true, bool draft = false, bool withCatalog = true,
        string state = "uploaded") => new
    {
        tag_name = tag,
        prerelease,
        draft,
        assets = withCatalog
            ? new[] { new { name = "moneta-update.json", state, browser_download_url = "https://example.invalid/catalog" } }
            : []
    };

    private static HttpResponseMessage Index(params object[] releases) => Reply(JsonSerializer.SerializeToUtf8Bytes(releases));

    private static HttpResponseMessage Reply(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static byte[] Pack(UpdateManifest manifest, ECDsa signer)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateJson.Default.UpdateManifest);
        var signature = signer.SignData(payload, HashAlgorithmName.SHA256);
        return JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope(payload, signature), UpdateJson.Default.UpdateEnvelope);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(handle(request));
        }
    }
}
