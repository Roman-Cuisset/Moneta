using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moneta.Updates;

public sealed record UpdateEnvelope(
    [property: JsonRequired] byte[] Payload,
    [property: JsonRequired] byte[] Signature);

public sealed record UpdateManifest(
    [property: JsonRequired] int Format,
    [property: JsonRequired] string Version,
    [property: JsonRequired] string Architecture,
    [property: JsonRequired] string Installer,
    [property: JsonRequired] long Size,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired] string ReleaseNotes)
{
    public const int MaximumEnvelopeBytes = 192 * 1024;
    public const long MaximumInstallerBytes = 300 * 1024 * 1024;
    public const int MaximumNoteLength = 24_000;
    public const string EnvelopeName = "moneta-update.json";
    public const string Target = "win-x64";

    public static UpdateManifest Verify(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> publicKey)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumEnvelopeBytes)
            throw new InvalidDataException(L.Get("UpdateManifest.SizeOfInvalidUpdateMetadata"));

        try
        {
            var envelope = JsonSerializer.Deserialize(bytes, UpdateJson.Default.UpdateEnvelope);
            if (envelope is not { Payload.Length: > 0, Signature.Length: 64 })
                throw new InvalidDataException(L.Get("UpdateManifest.SignatureOfUpdateAbsentOrInvalid"));

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
            if (consumed != publicKey.Length || verifier.KeySize != 256 ||
                !verifier.VerifyData(envelope.Payload, envelope.Signature, HashAlgorithmName.SHA256))
                throw new InvalidDataException(L.Get("UpdateManifest.TheMetadataSignatureDoesNotMatchTheMoneta"));

            var manifest = JsonSerializer.Deserialize(envelope.Payload, UpdateJson.Default.UpdateManifest);
            if (manifest is null || manifest.Format != 1 || manifest.Architecture != Target ||
                !TryVersion(manifest.Version, out _) ||
                manifest.Installer != InstallerName(manifest.Version) ||
                manifest.Size is <= 0 or > MaximumInstallerBytes ||
                manifest.Sha256 is not { Length: 64 } || !manifest.Sha256.All(Uri.IsHexDigit) ||
                manifest.ReleaseNotes is not { Length: > 0 and <= MaximumNoteLength } ||
                string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
                throw new InvalidDataException(L.Get("UpdateManifest.TheSignedCatalogueDoesNotDescribeACompatible"));
            return manifest;
        }
        catch (Exception error) when (error is JsonException or CryptographicException or FormatException)
        {
            throw new InvalidDataException(L.Get("UpdateManifest.InvalidUpdateMetadata"), error);
        }
    }

    public static bool TryVersion(string? text, out Version version)
    {
        if (System.Version.TryParse(text, out var parsed) && parsed is { Build: >= 0, Revision: -1 } &&
            parsed.Major <= 65535 && parsed.Minor <= 65535 && parsed.Build <= 65535 &&
            parsed.ToString(3) == text)
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    public static string InstallerName(string version) => $"Moneta-{version}-{Target}.exe";
    public bool IsNewerThan(Version installed) =>
        TryVersion(Version, out var version) && version > new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(UpdateEnvelope))]
[JsonSerializable(typeof(UpdateManifest))]
public sealed partial class UpdateJson : JsonSerializerContext;
