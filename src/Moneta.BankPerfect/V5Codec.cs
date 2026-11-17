namespace Moneta.BankPerfect;

// Tables du codec historique reprises de codec_v5.rs (Fabio Chelly, MIT).
// Révision et notice dans THIRD-PARTY-NOTICE.md.
internal static class V5Codec
{
    internal static ReadOnlySpan<byte> Alphabet =>
        "_8lL6 ,BZu2jp-ND0<(CPeE1AQyFTMG;[3Rxk4H)qdn.JVz7obW]:=OgKXIf>rmsYt+5ahicwSvU9"u8;

    internal static ReadOnlySpan<ushort> Deltas =>
    [
        241, 208, 211, 321, 243, 266, 267, 159, 305, 22, 223, 23, 6, 152, 120, 396, 215, 376, 297, 2,
        101, 185, 16, 2, 80, 116, 291, 147, 314, 5, 47, 184, 32, 223, 16, 74, 396, 61, 344, 362, 278,
        330, 341, 65, 55, 199, 276, 229, 139, 157, 377, 116, 227, 361, 262, 67, 154, 243, 98, 127, 230,
        351, 98, 219, 262, 293, 223, 182, 385, 287, 18, 112, 154, 272, 193, 115, 98, 393, 16, 136, 41,
        271, 318, 345, 47, 82, 165, 79, 356, 129, 112, 296, 326, 190, 5, 46, 249, 13, 367, 44
    ];

    private const int HeaderLength = 9;
    private static readonly int[] Positions = CreatePositions();

    public static (string? Format, byte[]? Decoded, V5LineRecovery Recovery) Decode(ReadOnlySpan<byte> raw,
        CancellationToken cancellationToken = default)
    {
        if (raw.StartsWith("BPFILE6\0"u8))
            return ("V6", null, V5LineRecovery.None);

        var format = Detect(raw);
        if (format == "V5")
            return (format, DecryptBody(raw, cancellationToken), V5LineRecovery.None);

        // CR seul et LF ont la même longueur et sont hors de l'alphabet.
        // Leur échange ne peut changer la signature, et le parseur lit les deux.
        var hasCrLf = raw.IndexOf("\r\n"u8) >= 0;
        if (format is not null || !hasCrLf && raw.Contains((byte)'\r'))
            return (format, null, V5LineRecovery.None);
        var (from, to, recovery) = hasCrLf
            ? ("\r\n", "\n", V5LineRecovery.CrLfToLf)
            : ("\n", "\r\n", V5LineRecovery.LfToCrLf);
        var converted = ConvertLineEnds(raw, from, to, cancellationToken);
        if (converted is null)
            return (format, null, V5LineRecovery.None);
        var alternateFormat = Detect(converted);
        if (alternateFormat == "V5")
            return (alternateFormat, DecryptBody(converted, cancellationToken), recovery);
        return (format ?? alternateFormat, null, V5LineRecovery.None);
    }

    private static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength)
            return null;
        Span<byte> header = stackalloc byte[HeaderLength];
        for (var index = 0; index < header.Length; index++)
            header[index] = Decrypt(bytes[index], index, bytes.Length);
        return header.StartsWith("BP-FILE-"u8) && char.IsAsciiDigit((char)header[^1])
            ? $"V{(char)header[^1]}"
            : null;
    }

    private static byte[] DecryptBody(ReadOnlySpan<byte> raw, CancellationToken cancellationToken)
    {
        var body = new byte[raw.Length - HeaderLength];
        for (var index = HeaderLength; index < raw.Length; index++)
        {
            if (((index - HeaderLength) & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
            body[index - HeaderLength] = Decrypt(raw[index], index, raw.Length);
        }
        return body;
    }

    private static byte Decrypt(byte value, int index, int length)
    {
        var position = Positions[value];
        if (position < 0)
            return value;
        var delta = (length + Deltas[(index + 1) % Deltas.Length]) % Alphabet.Length;
        return Alphabet[(position - delta + Alphabet.Length) % Alphabet.Length];
    }

    private static int[] CreatePositions()
    {
        var positions = new int[256];
        Array.Fill(positions, -1);
        for (var index = 0; index < Alphabet.Length; index++)
            positions[Alphabet[index]] = index;
        return positions;
    }

    private static byte[]? ConvertLineEnds(ReadOnlySpan<byte> raw, string from, string to, CancellationToken cancellationToken)
    {
        Span<byte> search = stackalloc byte[from.Length];
        for (var index = 0; index < from.Length; index++)
            search[index] = (byte)from[index];
        var changes = raw.Count(search);
        var length = raw.Length + changes * (to.Length - from.Length);
        if (changes == 0)
            return null;
        V5Limits.Check(length <= V5Limits.Bytes);

        var converted = new byte[length];
        var written = 0;
        while (raw.IndexOf(search) is var match && match >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            raw[..match].CopyTo(converted.AsSpan(written));
            written += match;
            foreach (var character in to)
                converted[written++] = (byte)character;
            raw = raw[(match + search.Length)..];
        }
        raw.CopyTo(converted.AsSpan(written));
        return converted;
    }
}
