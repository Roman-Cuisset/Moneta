using Moneta.Core;

namespace Moneta.BankPerfect;

internal sealed class V5Parser(string text, V5Diagnostics diagnostics, CancellationToken cancellationToken)
{
    private const string Separator = "TNSLPPBNTSOGPTQBC280673";
    private const string ScriptPrefix = "bp_script_data_";
    private readonly Dictionary<string, V5Header> _firstHeader = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<V5Header> _headers = [];
    private readonly List<V5Extension> _extensions = [];
    private readonly List<V5Unparsed> _unparsed = [];
    private int _line = 1;
    private int _operations;

    public V5Document? Parse()
    {
        using var parsing = V5Reader.Timings.StartActivity("parse");
        CheckPhysicalLines();
        var separator = text.IndexOf(Separator, StringComparison.Ordinal);
        if (separator < 0)
        {
            diagnostics.Add("structure", V5Finding.Unreadable, "Séparateur des blocs V5 introuvable.");
            return null;
        }

        ParseHeader(separator);
        var accounts = ParseAccounts();
        if (accounts.Count == 0)
            diagnostics.Add("no-accounts", V5Finding.Unreadable, "Aucune définition de compte contiguë depuis compte0.");

        var categories = new V5References(diagnostics);
        categories.Parse(_firstHeader.GetValueOrDefault("categ"), _firstHeader.GetValueOrDefault("modes"));
        var payees = ParsePayees(categories);
        var (order, defaultAccount) = ReadAccountPreferences(accounts.Count);
        CheckUnconsumedHeader(accounts.Count, payees.Count);

        var block = 0;
        do
        {
            var start = separator + Separator.Length;
            separator = text.IndexOf(Separator, start, StringComparison.Ordinal);
            var end = separator < 0 ? text.Length : separator;
            V5Limits.Check(block < V5Limits.Accounts, new(_line, block));
            if (block < accounts.Count)
            {
                var account = accounts[block];
                var currency = V5Values.Currency(account.Fields.Currency);
                var operations = ReadLines(start, end, block)
                    .Select(row => ParseOperation(row, currency, categories)).ToList().AsReadOnly();
                accounts[block] = account with { HasBlock = true, Operations = operations };
            }
            else
            {
                diagnostics.Add("orphan-block", V5Finding.Unreadable, "Bloc d’opérations sans compte positionnel.", new(_line, block));
                _unparsed.AddRange(ReadLines(start, end, block));
            }
            block++;
        } while (separator >= 0);

        parsing?.Stop();
        IReadOnlyList<V5Transfer> transfers;
        using (V5Reader.Timings.StartActivity("validate-v5"))
            transfers = V5Validation.Validate(accounts, diagnostics, cancellationToken);
        return new V5Document(_headers.AsReadOnly(), accounts.AsReadOnly(), categories.Categories, categories.Modes,
            payees.AsReadOnly(), _extensions.AsReadOnly(), transfers, _unparsed.AsReadOnly())
        {
            AccountOrder = order,
            DefaultAccountIndex = defaultAccount
        };
    }

    private void CheckPhysicalLines()
    {
        var start = 0;
        var count = 1;
        while (start < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = text.AsSpan(start).IndexOfAny('\r', '\n');
            V5Limits.Check(count <= V5Limits.Lines, new(count));
            V5Limits.Check((length < 0 ? text.Length - start : length) <= V5Limits.LineLength, new(count));
            if (length < 0)
                return;
            start = SkipLineEnding(start + length, text.Length);
            count++;
        }
    }

    private IEnumerable<V5Unparsed> ReadLines(int start, int end, int? block)
    {
        while (start < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = text.AsSpan(start, end - start).IndexOfAny('\r', '\n');
            if (length < 0)
            {
                yield return new(text[start..end], new(_line, block));
                yield break;
            }
            if (length != 0)
                yield return new(text.Substring(start, length), new(_line, block));
            start = SkipLineEnding(start + length, end);
            _line++;
        }
    }

    private int SkipLineEnding(int offset, int end) =>
        text[offset] == '\r' && offset + 1 < end && text[offset + 1] == '\n' ? offset + 2 : offset + 1;

    private void ParseHeader(int end)
    {
        var count = 0;
        foreach (var row in ReadLines(0, end, null))
        {
            V5Limits.Check(++count <= V5Limits.HeaderEntries, row.Source);
            var equals = row.Raw.IndexOf('=');
            if (equals <= 0)
            {
                _unparsed.Add(row);
                diagnostics.Add("invalid-header-line", V5Finding.Unreadable, "Entrée d’en-tête sans clé et signe égal.", row.Source);
                continue;
            }
            var key = row.Raw[..equals];
            var protectedValue = key.Equals("pass", StringComparison.OrdinalIgnoreCase);
            var redacted = protectedValue && equals + 1 != row.Raw.Length;
            var header = new V5Header(key, redacted ? null : row.Raw[(equals + 1)..], row.Source, redacted);
            _headers.Add(header);
            if (!_firstHeader.TryAdd(key, header))
                diagnostics.Add("duplicate-header", V5Finding.Uncertain,
                    "Clé d’en-tête répétée, première occurrence utilisée pour l’analyse.", row.Source);
            if (redacted)
                diagnostics.Add("password-field", V5Finding.Unsupported,
                    "Mot de passe historique présent, valeur occultée dans le modèle.", row.Source);
        }
        if (!_firstHeader.TryGetValue("guid", out var guid) || string.IsNullOrEmpty(guid.Value))
            diagnostics.Add("missing-document-id", V5Finding.Uncertain, "Identifiant de dossier source absent.", guid?.Source);
    }

    private List<V5Account> ParseAccounts()
    {
        var accounts = new List<V5Account>();
        while (_firstHeader.TryGetValue("compte" + accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), out var header))
        {
            V5Limits.Check(accounts.Count < V5Limits.Accounts, header.Source);
            var fields = new V5AccountFields(V5Fields.Split(header.Value!, V5Limits.Fields, header.Source));
            if (fields.Raw.Count is < 7 or > 17)
                diagnostics.Add("account-layout", V5Finding.Unsupported, "Nombre inhabituel de champs de compte.", header.Source);
            CheckAccountFields(fields, header.Source);
            var currency = V5Values.Currency(fields.Currency);
            if (currency is null)
                diagnostics.Add("unknown-currency", V5Finding.Uncertain,
                    "Devise principale absente ou sans correspondance avec le registre Moneta par défaut.", header.Source with { Field = 6 });
            accounts.Add(new(accounts.Count, header.Source, fields, currency?.Code, false, [], null,
                V5OpeningConfidence.Indeterminate, []));
        }
        return accounts;
    }

    private void CheckAccountFields(V5AccountFields fields, V5Location source)
    {
        if (string.IsNullOrWhiteSpace(fields.Uid))
            diagnostics.Add("missing-account-uid", V5Finding.Uncertain, "Identifiant de compte source absent ou vide.", source with { Field = 15 });
        else
            V5Limits.Check(fields.Uid.Length <= V5Limits.IdentifierLength, source with { Field = 15 });

        if (fields.VirtualMarker is not (null or "" or "real" or "virtual") ||
            fields.DisabledMarker is not (null or "" or "disabled"))
            diagnostics.Add("account-marker", V5Finding.Unsupported, "Marqueur de compte non reconnu.", source);
        if (fields.Logo is not (null or "" or "Aucun"))
            diagnostics.Add("logo", V5Finding.Unsupported, "Référence de logo conservée comme texte.", source with { Field = 12 });
        CheckRate(fields.PrimaryRate, source with { Field = 5 });
        CheckRate(fields.SecondaryRate, source with { Field = 7 });
    }

    private void CheckRate(string? rate, V5Location source)
    {
        if (!string.IsNullOrEmpty(rate) && !V5Values.TryExactDecimal(rate, out _, out _))
            diagnostics.Add("invalid-account-rate", V5Finding.Uncertain, "Taux source conservé comme texte, décimal exact non reconnu.", source);
    }

    private List<V5Payee> ParsePayees(V5References references)
    {
        var payees = new List<V5Payee>();
        while (_firstHeader.TryGetValue("who" + payees.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), out var header)
            && !string.IsNullOrEmpty(header.Value))
        {
            V5Limits.Check(payees.Count < V5Limits.ReferenceEntries, header.Source);
            var fields = V5Fields.Split(header.Value, V5Limits.Fields, header.Source);
            if (fields.Count != 6)
                diagnostics.Add("payee-layout", V5Finding.Unsupported, "Nombre inhabituel de champs du modèle de tiers.", header.Source);
            payees.Add(new(payees.Count, fields,
                references.ResolveMode(fields.ElementAtOrDefault(1), header.Source with { Field = 1 }, isTemplate: true),
                references.CategoryId(fields.ElementAtOrDefault(2), header.Source with { Field = 2 }), header.Source));
        }
        return payees;
    }

    private (IReadOnlyList<int> Order, int? DefaultAccount) ReadAccountPreferences(int count)
    {
        int? defaultAccount = null;
        if (_firstHeader.TryGetValue("compte_defaut", out var preferred))
        {
            if (V5Values.TryUInt(preferred.Value, out var index) && index < count)
                defaultAccount = (int)index;
            else
                diagnostics.Add("default-account", V5Finding.Uncertain, "Compte par défaut source hors de la liste positionnelle.", preferred.Source);
        }
        var seen = new HashSet<int>();
        var indices = new List<int>(count);
        if (_firstHeader.TryGetValue("accounts_order", out var order) && !string.IsNullOrEmpty(order.Value))
            foreach (var item in V5Fields.Split(order.Value, V5Limits.Accounts, order.Source))
            {
                if (V5Values.TryUInt(item, out var index) && index < count && seen.Add((int)index))
                    indices.Add((int)index);
                else
                    diagnostics.Add("account-order", V5Finding.Uncertain, "Ordre des comptes source incohérent.", order.Source);
            }
        indices.AddRange(Enumerable.Range(0, count).Where(index => !seen.Contains(index)));
        return (indices.AsReadOnly(), defaultAccount);
    }

    private void CheckUnconsumedHeader(int accounts, int payees)
    {
        foreach (var header in _headers)
        {
            var key = header.Key.ToLowerInvariant();
            if (key is "utilisateur" or "guid" or "username" or "modes" or "categ" or "accounts_order" or "compte_defaut" or "pass")
                continue;
            if (header.Key.StartsWith(ScriptPrefix, StringComparison.Ordinal))
            {
                ParseExtension(header);
                continue;
            }
            if (IsIndexedKey(key, "compte", accounts) || IsIndexedKey(key, "who", payees))
                continue;
            diagnostics.Add("unknown-header", V5Finding.Unsupported,
                "Clé d’en-tête inconnue ou entrée positionnelle hors de la séquence contiguë, conservée.", header.Source);
        }
    }

    private static bool IsIndexedKey(string key, string prefix, int count) =>
        key.StartsWith(prefix, StringComparison.Ordinal) &&
        V5Values.TryUInt(key[prefix.Length..], out var index) && index < count &&
        key[prefix.Length..] == index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void ParseExtension(V5Header header)
    {
        var value = header.Value!.AsSpan();
        var count = 0;
        for (var index = 0; index < value.Length; index++, count++)
        {
            if (value[index] == '_' && index + 2 < value.Length &&
                char.IsAsciiHexDigit(value[index + 1]) && char.IsAsciiHexDigit(value[index + 2]))
                index += 2;
        }
        _extensions.Add(new(header, count));
        diagnostics.Add("extension-data", V5Finding.Unsupported, "Donnée de script conservée sous sa forme encodée, contenu métier opaque.", header.Source);
    }

    private V5Operation ParseOperation(V5Unparsed row, Currency? currency, V5References references)
    {
        V5Limits.Check(++_operations <= V5Limits.Operations, row.Source);
        var fields = V5Fields.Split(row.Raw, V5Limits.Fields, row.Source);
        if (fields.Count is < 7 or > 9)
        {
            diagnostics.Add("operation-layout", V5Finding.Unreadable, "Ligne d’opération hors du format de sept à neuf champs.", row.Source);
            return new(row.Source, fields, false, new(V5DateKind.Operation, fields.ElementAtOrDefault(0), null, null, row.Source with { Field = 0 }),
                new(V5DateKind.Additional, fields.ElementAtOrDefault(7), null, null, row.Source with { Field = 7 }),
                V5Pointing.Unknown, null, null,
                new(fields.ElementAtOrDefault(6), null, null, V5AmountCompatibility.Unknown, row.Source with { Field = 6 }), [], null);
        }

        var date = V5Values.Date(fields[0], V5DateKind.Operation, row.Source with { Field = 0 }, diagnostics);
        var additional = V5Values.Date(fields.ElementAtOrDefault(7), V5DateKind.Additional, row.Source with { Field = 7 }, diagnostics);
        var state = fields[1] switch { "" => V5Pointing.NP, "P" or "1" => V5Pointing.P, "R" => V5Pointing.R, _ => V5Pointing.Unknown };
        if (state == V5Pointing.Unknown)
            diagnostics.Add("unknown-pointing", V5Finding.Uncertain, "État de pointage inconnu.", row.Source with { Field = 1 });
        if (state is V5Pointing.P or V5Pointing.R && date.Date is { } booked && additional.Date is { } marked && marked < booked)
            diagnostics.Add("earlier-additional-date", V5Finding.Uncertain, "Date supplémentaire antérieure à la date d’opération sur une ligne pointée.", additional.Source);
        var extra = V5Fields.ParseExtra(fields.ElementAtOrDefault(8), row.Source with { Field = 8 }, diagnostics);
        var uid = V5Fields.OperationUid(extra, row.Source, diagnostics);
        return new(row.Source, fields, true, date, additional, state,
            references.ResolveMode(fields[2], row.Source with { Field = 2 }),
            references.CategoryId(fields[5], row.Source with { Field = 5 }),
            V5Values.Amount(fields[6], currency, row.Source with { Field = 6 }, diagnostics), extra, uid);
    }
}
