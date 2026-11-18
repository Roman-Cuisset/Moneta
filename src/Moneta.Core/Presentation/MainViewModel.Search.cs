namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    public LedgerPage? LocateOperation(Guid accountId, Guid operationId)
    {
        if (IsDemo || !_ledgers.TryGetValue(accountId, out var ledger)) return null;
        var page = ReadPage(ledger.Summary.Account, 0, PendingChanges, operationId);
        return page.Transactions.Any(row => row.Id == operationId) ? page : null;
    }

    public bool ShowLocatedOperation(Guid accountId, Guid operationId, LedgerPage page)
    {
        if (!_ledgers.TryGetValue(accountId, out var ledger) ||
            !page.Transactions.Any(row => row.Id == operationId && row.AccountId == accountId && !row.IsDeleted))
            return false;
        var summary = ledger.Summary;
        var rows = BuildRows(page);
        if (summary.Account.IsArchived) ShowArchivedAccounts = true;
        SelectAccount(summary, page, rows);
        ApplyPage(page, rows, operationId);
        SetLedgerError(string.Empty);
        return true;
    }
}
