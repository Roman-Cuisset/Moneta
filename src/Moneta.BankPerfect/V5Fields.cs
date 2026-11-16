namespace Moneta.BankPerfect;

internal static class V5Fields
{
    public static IReadOnlyList<string> Split(string value, int limit, V5Location source)
    {
        V5Limits.Check(value.AsSpan().Count(';') < limit, source);
        return Array.AsReadOnly(value.Split(';'));
    }

    public static IReadOnlyList<V5Extra> ParseExtra(string? raw, V5Location source, V5Diagnostics diagnostics)
    {
        if (string.IsNullOrEmpty(raw))
            return [];
        var extras = new List<V5Extra>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        while (start <= raw.Length)
        {
            V5Limits.Check(extras.Count < V5Limits.Fields, source);
            var end = raw.IndexOf("<||>", start, StringComparison.Ordinal);
            var entry = raw[start..(end < 0 ? raw.Length : end)];
            var equals = entry.IndexOf('=');
            var key = equals < 0 ? entry : entry[..equals];
            var rawValue = equals < 0 ? null : entry[(equals + 1)..];
            var value = rawValue?.Replace("<fab_pv>", ";", StringComparison.Ordinal).Replace("<fab_cr>", "\n", StringComparison.Ordinal);
            var position = source with { Item = extras.Count };
            extras.Add(new(key, rawValue, value, position));
            if (key.Length == 0 || rawValue is null)
                diagnostics.Add("malformed-extra", V5Finding.Uncertain, "Entrée Extra vide ou sans affectation, conservée.", position);
            if (!keys.Add(key))
                diagnostics.Add("duplicate-extra", V5Finding.Uncertain, "Clé Extra répétée, occurrences conservées.", position);
            if (key is not ("UId" or "#sisterAccUid" or "#sisterLineUid"))
                diagnostics.Add("unknown-extra", V5Finding.Unsupported, "Champ Extra sans interprétation métier, conservé.", position);
            if (key is "UId" or "#sisterAccUid" or "#sisterLineUid")
                V5Limits.Check(rawValue is null || rawValue.Length <= V5Limits.IdentifierLength, position);
            if (end < 0)
                break;
            start = end + 4;
        }
        return extras.AsReadOnly();
    }

    public static ulong? OperationUid(IReadOnlyList<V5Extra> extras, V5Location row, V5Diagnostics diagnostics)
    {
        var uids = extras.Where(extra => extra.Key == "UId").ToArray();
        if (uids.Length == 1 && V5Values.TryUid(uids[0].RawValue, out var uid))
            return uid;
        if (uids.Length == 0 || uids.Length == 1 && string.IsNullOrEmpty(uids[0].RawValue))
            diagnostics.Add("missing-operation-uid", V5Finding.Uncertain, "Identifiant d’opération source absent ou vide.", uids.FirstOrDefault()?.Source ?? row);
        else if (uids.Length == 1)
            diagnostics.Add("invalid-operation-uid", V5Finding.Unreadable, "Identifiant d’opération hors de la plage entière positive.", uids[0].Source);
        return null;
    }
}
