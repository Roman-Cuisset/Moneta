using System.Text.Json;
using System.Text.Json.Serialization;
using Moneta.Core;

namespace Moneta.Infrastructure.ExchangeRates;

public static class HistoricalProofFile
{
    private const int MaximumProofBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };

    public static void WriteNew(string path, HistoricalProof proof)
    {
        _ = proof.Reproduce();
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".fxproof-{Guid.NewGuid():N}.tmp");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(proof, Json);
        if (bytes.Length > MaximumProofBytes) throw new IOException("La preuve est trop volumineuse.");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            created = false;
        }
        finally { if (created) File.Delete(temporary); }
    }

    public static HistoricalProof ReadVerified(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumProofBytes) throw new IOException("Le fichier de preuve est trop volumineux.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var proof = JsonSerializer.Deserialize<HistoricalProof>(bytes, Json) ?? throw new JsonException("Fichier de preuve vide.");
        _ = proof.Reproduce();
        return proof;
    }
}
