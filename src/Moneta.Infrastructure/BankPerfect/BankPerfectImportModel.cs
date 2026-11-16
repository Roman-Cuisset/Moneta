using Moneta.BankPerfect;
using Moneta.Core;

namespace Moneta.Infrastructure.BankPerfect;

public sealed record BankPerfectImportIssue(string Code, string Message, bool Blocking, V5Location? Source = null);
public sealed record BankPerfectImportAccount(string Name, string? Currency, int OperationCount,
    decimal? Balance, decimal? PointedBalance, decimal? UnmarkedAmount, V5Location? Opening);

public sealed record BankPerfectImportReport(string? SourceSha256, V5Report Reader,
    IReadOnlyList<BankPerfectImportAccount> Accounts, IReadOnlyList<BankPerfectImportIssue> Issues)
{
    public bool CanImport => SourceSha256 is not null && Reader.Format == "V5" && Accounts.Count != 0 &&
        Issues.All(issue => !issue.Blocking);
}

public sealed class BankPerfectImportPreview
{
    internal BankPerfectImportPreview(string sourcePath, V5Document? source, BankPerfectImportReport report)
    {
        SourcePath = sourcePath;
        Source = source;
        Report = report;
    }

    internal string SourcePath { get; }
    internal V5Document? Source { get; }
    public BankPerfectImportReport Report { get; }
}

public sealed record BankPerfectImportResult(string Destination, Guid DocumentId, string ProvenanceSha256,
    BankPerfectImportReport Report, bool BackupRestoreVerified);

internal sealed record BankPerfectTarget(FinancialDocument Document, IReadOnlyList<TransactionChange> Operations,
    IReadOnlyList<TransferChange> Transfers, IReadOnlyList<BankPerfectObjectMapping> Objects);

internal sealed record BankPerfectObjectMapping(string Type, Guid TargetId, V5Location? Source, string? SourceId,
    IReadOnlyList<string?> Raw, bool Opening = false);
