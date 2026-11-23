using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moneta.Core;

public sealed record FrozenReport(Guid Id, Guid? PredecessorId, string Name, DateTimeOffset CapturedUtc,
    string Sha256, string? DraftPayload = null)
{
    public const int MaximumPayloadBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 20,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static FrozenReport Create(string name, ReportContent content, Guid? predecessor = null)
    {
        content.Verify();
        var payload = JsonSerializer.Serialize(content, Json);
        var entry = new FrozenReport(Guid.NewGuid(), predecessor, name.Trim(), DateTimeOffset.UtcNow, Fingerprint(payload), payload);
        entry.Validate();
        return entry;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || PredecessorId == Guid.Empty || PredecessorId == Id || string.IsNullOrWhiteSpace(Name) ||
            Name.Length > 120 || Name.Contains('\0') || CapturedUtc == default || CapturedUtc.Offset != TimeSpan.Zero ||
            Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit))
            throw new ArgumentException(L.Get("FrozenReport.IdentityOrImprintOfTheFrozenInvalidReport"));
    }

    public ReportContent Reproduce(string payload)
    {
        Validate();
        if (!string.Equals(Fingerprint(payload), Sha256, StringComparison.Ordinal))
            throw new ArgumentException(L.Get("FrozenReport.TheReportSFootprintDoesNotMatchIts"));
        var result = JsonSerializer.Deserialize<ReportContent>(payload, Json) ?? throw new ArgumentException(L.Get("FrozenReport.EmptyFrozenReport"));
        result.Verify();
        return result;
    }

    private static string Fingerprint(string payload)
    {
        var count = Encoding.UTF8.GetByteCount(payload);
        if (count == 0 || count > MaximumPayloadBytes) throw new ArgumentException(L.Get("FrozenReport.TheReportSEvidenceExceeds32Mio"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
