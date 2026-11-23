namespace Moneta.BankPerfect;

public enum V5Severity { Information, Warning, Error }
public enum V5Finding { Read, Unrepresentable, Uncertain, Unreadable, Unsupported }
public enum V5Pointing { Unknown, NP, P, R }
public enum V5DateKind { Operation, Additional }
public enum V5AmountCompatibility { Unknown, Exact, ExcessPrecision, OutOfRange }
public enum V5OpeningConfidence { Indeterminate, Probable, Confirmed }
public enum V5TransferStatus { Linked, Incomplete, MissingTarget, Ambiguous, Inconsistent }
public enum V5LineRecovery { None, CrLfToLf, LfToCrLf }
public enum V5Coverage { Unrecognized, Rejected, Partial, Recognized }

/// <summary>One-based decoded text line. Block and field are zero-based source positions.</summary>
public sealed record V5Location(int Line, int? Block = null, int? Field = null, int? Item = null);

public sealed record V5Diagnostic(string Code, V5Severity Severity, V5Finding Finding, string Message,
    V5Location? Source);

public sealed record V5Header(string Key, string? Value, V5Location Source, bool Redacted = false);
public sealed record V5Extension(V5Header Header, int DecodedByteCount);
public sealed record V5AccountFields(IReadOnlyList<string> Raw)
{
    public string? Bank => Raw.ElementAtOrDefault(0);
    public string? Branch => Raw.ElementAtOrDefault(1);
    public string? Address => Raw.ElementAtOrDefault(2);
    public string? Name => Raw.ElementAtOrDefault(3);
    public string? Number => Raw.ElementAtOrDefault(4);
    public string? PrimaryRate => Raw.ElementAtOrDefault(5);
    public string? Currency => Raw.ElementAtOrDefault(6);
    public string? SecondaryRate => Raw.ElementAtOrDefault(7);
    public string? SecondaryCurrency => Raw.ElementAtOrDefault(8);
    public string? BankCode => Raw.ElementAtOrDefault(9);
    public string? BranchCode => Raw.ElementAtOrDefault(10);
    public string? Bic => Raw.ElementAtOrDefault(11);
    public string? Logo => Raw.ElementAtOrDefault(12);
    public string? Iban => Raw.ElementAtOrDefault(13);
    public string? VirtualMarker => Raw.ElementAtOrDefault(14);
    public string? Uid => Raw.ElementAtOrDefault(15);
    public string? DisabledMarker => Raw.ElementAtOrDefault(16);
}

public sealed record V5Date(V5DateKind Kind, string? Raw, int? Days, DateOnly? Date, V5Location Source)
{
    public bool Present => Raw is not null;
}

public sealed record V5Amount(string? Raw, decimal? Value, int? Scale, V5AmountCompatibility Compatibility,
    V5Location Source);
public sealed record V5Extra(string Key, string? RawValue, string? Value, V5Location Source);
public sealed record V5Category(string Raw, string? RawId, uint? Id, string Name, int Indentation,
    int? ParentItem, V5Location Source)
{
    // L'ancien format sans '=' utilise la position physique pour ses références.
    public uint? LegacyPositionId => RawId is null && !string.IsNullOrWhiteSpace(Name) ? (uint?)Source.Item : null;
}
public sealed record V5Mode(string Raw, string Name, bool Debit, V5Location Source);
public sealed record V5Payee(int Index, IReadOnlyList<string> Fields, string? Mode, uint? CategoryId,
    V5Location Source)
{
    public string? Caption => Fields.ElementAtOrDefault(0);
    public string? Payee => Fields.ElementAtOrDefault(3);
    public string? Details => Fields.ElementAtOrDefault(4);
    public string? AmountText => Fields.ElementAtOrDefault(5);
}

public sealed record V5Operation(V5Location Source, IReadOnlyList<string> Fields, bool ValidLayout,
    V5Date Date, V5Date AdditionalDate, V5Pointing Pointing, string? Mode, uint? CategoryId,
    V5Amount Amount, IReadOnlyList<V5Extra> Extra, ulong? Uid)
{
    public string? RawPointing => Fields.ElementAtOrDefault(1);
    public string? RawMode => Fields.ElementAtOrDefault(2);
    public string? Payee => Fields.ElementAtOrDefault(3);
    public string? Details => Fields.ElementAtOrDefault(4);
    public string? RawCategory => Fields.ElementAtOrDefault(5);
}

public sealed record V5Account(int Index, V5Location Source, V5AccountFields Fields, string? MonetaCurrency,
    bool HasBlock, IReadOnlyList<V5Operation> Operations, decimal? LedgerSum,
    V5OpeningConfidence Opening, IReadOnlyList<V5Location> OpeningCandidates);
public sealed record V5Transfer(V5Location Source, string? SourceAccountUid, ulong? SourceOperationUid,
    string? RawTargetAccountUid, string? RawTargetOperationUid, ulong? TargetOperationUid,
    V5Location? Target, bool Reciprocal, V5TransferStatus Status)
{
    public bool RecoveredAccountReference { get; init; }
}
public sealed record V5Unparsed(string Raw, V5Location Source);

public sealed record V5Document(IReadOnlyList<V5Header> Header, IReadOnlyList<V5Account> Accounts,
    IReadOnlyList<V5Category> Categories, IReadOnlyList<V5Mode> Modes, IReadOnlyList<V5Payee> PayeeTemplates,
    IReadOnlyList<V5Extension> Extensions, IReadOnlyList<V5Transfer> Transfers,
    IReadOnlyList<V5Unparsed> Unparsed)
{
    public IReadOnlyList<int> AccountOrder { get; init; } = [];
    public int? DefaultAccountIndex { get; init; }
}

/// <summary>Localisation et résultats sans valeurs brutes, noms, chemins ou secrets.</summary>
public sealed record V5Report(int ReportVersion, string? Format, long? ByteLength, V5LineRecovery Recovery,
    V5Coverage Coverage, int Accounts, int Operations, int Categories, int PayeeTemplates, int DistinctOperationPayees,
    int ConfirmedOpenings, int ProbableOpenings, int IndeterminateOpenings,
    int LinkedTransfers, int NP, int P, int R, int UnknownPointing, int ExactSourceAmounts,
    int UnrepresentableAmounts, int UnqualifiedAmounts, int UnreadableAmounts,
    IReadOnlyDictionary<V5Finding, int> Findings, IReadOnlyDictionary<V5Severity, int> Severities,
    IReadOnlyList<V5Diagnostic> Diagnostics);

public sealed record V5ReadResult(V5Report Report, V5Document? Document)
{
    public string? SourceSha256 { get; init; }
}
