using System.Collections.ObjectModel;

namespace Moneta.BankPerfect;

internal static class V5Limits
{
    public const int Bytes = 16 * 1024 * 1024;
    public const int Lines = 200_000;
    public const int LineLength = 65_536;
    public const int Operations = 100_000;
    public const int Accounts = 512;
    public const int HeaderEntries = 8_192;
    public const int ReferenceEntries = 4_096;
    public const int Fields = 128;
    public const int IdentifierLength = 256;
    public const int NumberLength = 64;
    public const int Diagnostics = 4_096;

    public static void Check(bool withinLimit, V5Location? source = null)
    {
        if (!withinLimit)
            throw new V5LimitException(source);
    }
}

internal sealed class V5LimitException(V5Location? source) : Exception
{
    public V5Location? SourceLocation { get; } = source;
}

internal sealed class V5Diagnostics
{
    private readonly List<V5Diagnostic> _items = [];
    private readonly Dictionary<V5Finding, int> _findings = [];
    private readonly Dictionary<V5Severity, int> _severities = [];
    private int _omitted;

    public IReadOnlyDictionary<V5Finding, int> Findings => new ReadOnlyDictionary<V5Finding, int>(_findings);
    public IReadOnlyDictionary<V5Severity, int> Severities => new ReadOnlyDictionary<V5Severity, int>(_severities);

    public void Add(string code, V5Finding finding, string message, V5Location? source = null)
    {
        var severity = finding switch
        {
            V5Finding.Read => V5Severity.Information,
            V5Finding.Unreadable => V5Severity.Error,
            _ => V5Severity.Warning
        };
        _findings[finding] = _findings.GetValueOrDefault(finding) + 1;
        _severities[severity] = _severities.GetValueOrDefault(severity) + 1;
        if (_items.Count < V5Limits.Diagnostics || code == "limit")
            _items.Add(new(code, severity, finding, message, source));
        else
            _omitted++;
    }

    public IReadOnlyList<V5Diagnostic> Finish()
    {
        if (_omitted > 0)
            _items.Add(new("diagnostics-omitted", V5Severity.Warning, V5Finding.Uncertain,
                $"Nombre de diagnostics individuels omis après le plafond : {_omitted}.", null));
        return _items.AsReadOnly();
    }
}
