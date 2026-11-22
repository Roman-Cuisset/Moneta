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
        using var client = new UpdateClient(channel, new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(envelope) }));

        Assert.IsNotNull(await client.CheckAsync(new Version(0, 1, 0, 0), default));
        Assert.IsNull(await client.CheckAsync(new Version(0, 1, 1, 0), default));
        Assert.IsNull(await client.CheckAsync(new Version(0, 2, 0, 0), default));
    }

    [TestMethod]
    public async Task RedirectsCannotSwitchHostsOrDowngradeTls()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        foreach (var redirect in new[] { "http://github.com/installer", "https://example.org/installer", "https://github.com:444/installer" })
        {
            var handler = new Handler(_ =>
            {
                var reply = new HttpResponseMessage(HttpStatusCode.Redirect);
                reply.Headers.Location = new Uri(redirect);
                return reply;
            });
            using var client = new UpdateClient(channel, handler);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.CheckAsync(new Version(0, 1, 0), default));
            Assert.AreEqual(1, handler.Calls);
        }
    }

    [TestMethod]
    public async Task OversizedCatalogIsRejectedAndCancellationPropagates()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var channel = new UpdateChannel(false, signer.ExportSubjectPublicKeyInfo());
        using var client = new UpdateClient(channel, new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[UpdateManifest.MaximumEnvelopeBytes + 1]) }));
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
