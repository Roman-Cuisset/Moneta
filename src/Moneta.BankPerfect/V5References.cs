namespace Moneta.BankPerfect;

internal sealed class V5References(V5Diagnostics diagnostics)
{
    private static readonly string[] StandardModes =
    [
        "Carte", "Retrait DAB", "Chèque émis", "Prélèvement", "Virement émis",
        "TIP", "Versement", "Dépôt de chèque", "Virement reçu", "Titre"
    ];
    private readonly List<V5Category> _categories = [];
    private readonly List<V5Mode> _modes = [];
    private readonly Dictionary<uint, int> _categoryCounts = [];
    private readonly Dictionary<string, int> _modeNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _ambiguousModeOrder;

    public IReadOnlyList<V5Category> Categories => _categories.AsReadOnly();
    public IReadOnlyList<V5Mode> Modes => _modes.AsReadOnly();

    public void Parse(V5Header? categories, V5Header? modes)
    {
        if (categories?.Value is { Length: > 0 })
            ParseCategories(categories);
        if (modes?.Value is { Length: > 0 })
            ParseModes(modes);
    }

    private void ParseCategories(V5Header header)
    {
        int? lastRoot = null;
        var legacyIds = 0;
        foreach (var item in V5Fields.Split(header.Value!, V5Limits.ReferenceEntries, header.Source))
        {
            var source = header.Source with { Item = _categories.Count };
            var equal = item.IndexOf('=');
            var rawId = equal < 0 ? null : item[..equal];
            var rawName = item[(equal + 1)..];
            var name = rawName.TrimStart(' ');
            var indentation = rawName.Length - name.Length;
            uint? id = null;
            if (V5Values.TryUInt(rawId, out var parsed))
                id = parsed;
            else if (rawId is not null)
                diagnostics.Add("invalid-category-id", V5Finding.Uncertain,
                    "Identifiant de catégorie explicite invalide, définition brute conservée.", source);

            if (string.IsNullOrWhiteSpace(name) || rawName.AsSpan().TrimStart(' ').StartsWith("\t"))
                diagnostics.Add("category-layout", V5Finding.Uncertain, "Nom vide ou indentation de catégorie non reconnue.", source);
            if (indentation > 0 && lastRoot is null)
                diagnostics.Add("category-parent", V5Finding.Uncertain, "Sous-catégorie sans racine physique précédente.", source);
            var category = new V5Category(item, rawId, id, name, indentation, indentation > 0 ? lastRoot : null, source);
            _categories.Add(category);
            if (category.LegacyPositionId.HasValue)
                legacyIds++;
            if ((id ?? category.LegacyPositionId) is { } reference)
            {
                var count = _categoryCounts.GetValueOrDefault(reference) + 1;
                _categoryCounts[reference] = count;
                if (count > 1)
                    diagnostics.Add("duplicate-category-id", V5Finding.Uncertain, "Référence de catégorie répétée.", source);
            }
            if (indentation == 0 && !string.IsNullOrWhiteSpace(name))
                lastRoot = source.Item;
        }
        if (legacyIds > 0)
            diagnostics.Add("legacy-category-positions", V5Finding.Read,
                "Catégories historiques référencées par leur position, bruts conservés sans UID.", header.Source);
    }

    private void ParseModes(V5Header header)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in V5Fields.Split(header.Value!, V5Limits.ReferenceEntries, header.Source))
        {
            var source = header.Source with { Item = _modes.Count };
            var debit = item.StartsWith('-');
            var name = debit ? item[1..] : item;
            _modes.Add(new(item, name, debit, source));
            _modeNames[name] = _modeNames.GetValueOrDefault(name) + 1;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(item))
            {
                _ambiguousModeOrder = true;
                diagnostics.Add("mode-definition", V5Finding.Uncertain, "Définition de mode vide ou répétée, ordre historique ambigu.", source);
            }
        }
    }

    public uint? CategoryId(string? raw, V5Location source)
    {
        if (raw?.Trim() == "-1")
            return null;
        if (!V5Values.TryUInt(raw, out var id))
        {
            diagnostics.Add("category-reference", V5Finding.Uncertain, "Référence de catégorie absente ou invalide.", source);
            return null;
        }
        var count = _categoryCounts.GetValueOrDefault(id);
        if (count != 1)
            diagnostics.Add(count == 0 ? "missing-category-target" : "ambiguous-category-target",
                V5Finding.Uncertain, "Référence vers une catégorie absente ou non unique.", source);
        return id;
    }

    public string? ResolveMode(string? raw, V5Location source, bool isTemplate = false)
    {
        string? name = null;
        if (V5Values.TryUInt(raw, out var code))
        {
            var index = isTemplate ? code : code switch
            {
                >= 20 and <= 29 => code - 20,
                >= 40 => code - 34,
                _ => uint.MaxValue
            };
            if (index < StandardModes.Length)
                return StandardModes[index];
            if (!_ambiguousModeOrder && index - (uint)StandardModes.Length < _modes.Count)
                return _modes[(int)index - StandardModes.Length].Name;
        }
        else if (!isTemplate && !string.IsNullOrEmpty(raw))
        {
            if (raw.StartsWith("Chq ", StringComparison.Ordinal))
                return StandardModes[2];
            name = Array.Find(StandardModes, mode => mode.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (name is null && _modeNames.TryGetValue(raw, out var matches) && matches == 1)
                name = raw;
        }
        if (name is null)
            diagnostics.Add("unknown-mode", V5Finding.Uncertain, "Mode de paiement d’origine absent ou non résolu.", source);
        return name;
    }
}
