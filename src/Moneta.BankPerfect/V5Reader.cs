using System.Security;
using System.Text;

namespace Moneta.BankPerfect;

/// <summary>Lecteur local en lecture seule. Le document intermédiaire contient des données sensibles.</summary>
public static class V5Reader
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static V5Report Diagnose(string path) => ReadFile(path).Report;

    public static V5ReadResult ReadFile(string path)
    {
        long? length = null;
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, options: FileOptions.SequentialScan);
            length = stream.Length;
            if (length > V5Limits.Bytes)
                return Failed(length, "limit", "Taille du fichier supérieure à la limite du lecteur.");
            bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() >= 0)
                return Failed(length, "source-changed", "La taille du fichier a changé pendant la lecture.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or SecurityException)
        {
            return Failed(length, "source-io", "Fichier inaccessible ou lecture interrompue.");
        }
        return Read(bytes);
    }

    public static V5ReadResult Read(ReadOnlySpan<byte> raw)
    {
        var diagnostics = new V5Diagnostics();
        if (raw.IsEmpty)
            return Failed(0, "empty-file", "Le fichier est vide.");
        if (raw.Length > V5Limits.Bytes)
            return Failed(raw.Length, "limit", "Taille du fichier supérieure à la limite du lecteur.");

        string? format = null;
        var recovery = V5LineRecovery.None;
        V5Document? document = null;
        try
        {
            (format, var bytes, recovery) = V5Codec.Decode(raw);
            if (bytes is null)
            {
                if (format is null)
                    diagnostics.Add("invalid-signature", V5Finding.Unreadable,
                        "Marqueur V5 introuvable après désobfuscation. Fichier tronqué, altéré ou format inconnu.");
                else
                    diagnostics.Add("unsupported-version", V5Finding.Unsupported, "Version reconnue hors du périmètre V5.");
                return Result(format, raw.Length, recovery, null, diagnostics);
            }
            if (recovery != V5LineRecovery.None)
                diagnostics.Add("line-end-recovery", V5Finding.Read, "Marqueur V5 retrouvé après restauration des fins de ligne.");
            if (CheckEncoding(bytes, diagnostics))
                document = new V5Parser(Windows1252.GetString(bytes), diagnostics).Parse();
        }
        catch (V5LimitException error)
        {
            diagnostics.Add("limit", V5Finding.Unreadable, "Quota de lecture V5 dépassé.", error.SourceLocation);
        }
        return Result(format, raw.Length, recovery, document, diagnostics);
    }

    private static bool CheckEncoding(ReadOnlySpan<byte> bytes, V5Diagnostics diagnostics)
    {
        var undefined = false;
        foreach (var value in bytes)
        {
            if (value is < 32 and not (9 or 10 or 13) or 127)
            {
                diagnostics.Add("control-byte", V5Finding.Unreadable,
                    "Le texte contient des caractères de contrôle hors de la grammaire V5.");
                return false;
            }
            undefined |= value is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D;
        }
        if (undefined)
            diagnostics.Add("undefined-cp1252", V5Finding.Uncertain,
                "Le texte contient des octets sans caractère graphique Windows-1252.");
        return true;
    }

    private static V5ReadResult Failed(long? length, string code, string message)
    {
        var diagnostics = new V5Diagnostics();
        diagnostics.Add(code, V5Finding.Unreadable, message);
        return Result(null, length, V5LineRecovery.None, null, diagnostics);
    }

    private static V5ReadResult Result(string? format, long? length, V5LineRecovery recovery,
        V5Document? document, V5Diagnostics diagnostics)
    {
        var findings = diagnostics.Finish();
        var coverage = document is null
            ? findings.Any(item => item.Code is "invalid-signature" or "unsupported-version")
                ? V5Coverage.Unrecognized : V5Coverage.Rejected
            : diagnostics.Findings.Keys.Any(finding => finding != V5Finding.Read) ? V5Coverage.Partial : V5Coverage.Recognized;
        var accounts = document?.Accounts ?? [];
        var operations = accounts.SelectMany(account => account.Operations).ToArray();
        var report = new V5Report(1, format, length, recovery, coverage,
            accounts.Count, operations.Length, document?.Categories.Count ?? 0,
            document?.PayeeTemplates.Count ?? 0,
            operations.Select(item => item.Payee).Where(name => !string.IsNullOrEmpty(name)).Distinct().Count(),
            accounts.Count(account => account.Opening == V5OpeningConfidence.Confirmed),
            accounts.Count(account => account.Opening == V5OpeningConfidence.Probable),
            accounts.Count(account => account.Opening == V5OpeningConfidence.Indeterminate),
            (document?.Transfers.Count(item => item.Status == V5TransferStatus.Linked) ?? 0) / 2,
            operations.Count(item => item.Pointing == V5Pointing.NP),
            operations.Count(item => item.Pointing == V5Pointing.P),
            operations.Count(item => item.Pointing == V5Pointing.R),
            operations.Count(item => item.Pointing == V5Pointing.Unknown),
            operations.Count(item => item.Amount.Value.HasValue),
            operations.Count(item => item.Amount.Compatibility is V5AmountCompatibility.ExcessPrecision or V5AmountCompatibility.OutOfRange),
            operations.Count(item => item.Amount.Value.HasValue && item.Amount.Compatibility == V5AmountCompatibility.Unknown),
            operations.Count(item => !item.Amount.Value.HasValue), diagnostics.Findings, diagnostics.Severities, findings);
        return new(report, document);
    }
}
