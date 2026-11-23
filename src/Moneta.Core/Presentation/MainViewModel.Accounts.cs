using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    private bool _showArchivedAccounts;

    public bool ShowArchivedAccounts
    {
        get => _showArchivedAccounts;
        set
        {
            if (value == _showArchivedAccounts) return;
            if (!value && SelectedAccount?.Account.IsArchived == true)
                SelectAccount(_accounts.FirstOrDefault(item => !item.Account.IsArchived));
            _showArchivedAccounts = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowArchivedAccounts)));
            NotifyVisibleAccounts();
        }
    }

    public IReadOnlyList<AccountSummary> VisibleAccounts =>
        _showArchivedAccounts ? Accounts : Accounts.Where(item => !item.Account.IsArchived).ToArray();

    public void EditAccount(Guid id, AccountProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var summary = ManagedAccount(id);
        UpdateAccount(summary, ApplyAccountProperties(summary.Account, properties, existing: true));
    }

    public void SetAccountArchived(Guid id, bool archived)
    {
        var summary = ManagedAccount(id);
        if (summary.Account.IsArchived == archived) return;
        if (archived && HasAccountDrafts(id))
            throw new InvalidOperationException(L.Get("MainViewModel.SaveDraftsFromThisAccountBeforeArchiving"));
        if (archived && ReferenceEquals(SelectedAccount, summary)) ShowArchivedAccounts = true;
        UpdateAccount(summary, summary.Account with { IsArchived = archived });
        NotifyVisibleAccounts();
    }

    public bool CanMoveAccount(Guid id, int step)
    {
        if (IsDemo || !_ledgers.TryGetValue(id, out var ledger) || step is not (-1 or 1)) return false;
        return AdjacentAccount(_accounts.IndexOf(ledger.Summary), step) >= 0;
    }

    public void MoveAccount(Guid id, int step)
    {
        var summary = ManagedAccount(id);
        if (step is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(step));
        var previous = _accounts.IndexOf(summary);
        var target = AdjacentAccount(previous, step);
        if (target < 0) throw new InvalidOperationException(L.Get("MainViewModel.TheAccountIsAlreadyAtThisLimitOf"));
        _accounts.Move(previous, target);
        if (Math.Abs(target - previous) > 1) _accounts.Move(target - step, previous);
        _accounts[previous].UpdateOrdinal(previous);
        summary.UpdateOrdinal(target);
        MarkDirty();
        NotifyVisibleAccounts();
    }

    private int AdjacentAccount(int start, int step)
    {
        for (var index = start + step; index >= 0 && index < _accounts.Count; index += step)
            if (_showArchivedAccounts || !_accounts[index].Account.IsArchived) return index;
        return -1;
    }

    public void ValidateAccountDeletion(Guid id)
    {
        var account = ManagedAccount(id).Account;
        if (ScheduleUsesAccount(id))
            throw new InvalidOperationException(L.Get("MainViewModel.ThisAccountIsUsedByTheTimelineArchive"));
        if (Budgets.Any(item => item.Accounts.Contains(id)))
            throw new InvalidOperationException(L.Get("MainViewModel.ThisAccountIsUsedByABudgetRemove"));
        var persisted = _document!.Accounts.Any(item => item.Id == id);
        if (account.OpeningBalance != 0 || HasAccountDrafts(id) ||
            (persisted && (_source is not IAccountHistorySource history || history.HasAccountHistory(id))))
            throw new InvalidOperationException(L.Get("MainViewModel.OnlyAnAccountWithoutInitialBalanceHistoryOr"));
    }

    public void DeleteEmptyAccount(Guid id)
    {
        ValidateAccountDeletion(id);
        var summary = _ledgers[id].Summary;
        if (ReferenceEquals(summary, SelectedAccount))
            SelectAccount(VisibleAccounts.FirstOrDefault(item => item != summary));
        var index = _accounts.IndexOf(summary);
        _accounts.RemoveAt(index);
        _ledgers.Remove(id);
        for (; index < _accounts.Count; index++) _accounts[index].UpdateOrdinal(index);
        MarkDirty();
        NotifyVisibleAccounts();
    }

    public bool FindArchivedOperation(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (SelectedAccount is not { Account.IsArchived: true } summary || _source is not IAccountHistorySource source)
            throw new InvalidOperationException(L.Get("MainViewModel.SelectASavedArchivedAccount"));
        if (source.FindAccountOperation(summary.Account.Id, text.Trim(), SelectedRow?.Transaction.Id) is not { } id)
            return false;
        var page = ReadPage(summary.Account, 0, PendingChanges, id);
        var rows = BuildRows(page);
        if (!rows.Any(row => row.Transaction.Id == id)) return false;
        ApplyPage(page, rows, id);
        return true;
    }

    private AccountSummary ManagedAccount(Guid id)
    {
        if (IsDemo || !_ledgers.TryGetValue(id, out var ledger))
            throw new InvalidOperationException(L.Get("MainViewModel.ChooseADocumentAccount"));
        return ledger.Summary;
    }

    private bool HasAccountDrafts(Guid id) =>
        _pending.Values.Any(change => change.Value.AccountId == id) ||
        _pendingReconciliations.Sessions.Any(change => change.Value.AccountId == id) ||
        _pendingReconciliations.Events.Any(change => change.OperationBefore?.AccountId == id);

    private void UpdateAccount(AccountSummary summary, Account updated)
    {
        if (updated == summary.Account) return;
        var now = DateTimeOffset.UtcNow;
        summary.UpdateAccount(updated with { ModifiedUtc = now > updated.ModifiedUtc ? now : updated.ModifiedUtc });
        if (ReferenceEquals(summary, SelectedAccount))
        {
            PropertyChanged?.Invoke(this, StatusChanged);
            PropertyChanged?.Invoke(this, AccountChanged);
        }
        MarkDirty();
        NotifyReconciliationChanged();
    }

    private void NotifyVisibleAccounts() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleAccounts)));

    private static Account ApplyAccountProperties(Account account, AccountProperties input, bool existing)
    {
        string? Field(string? value, string? previous, int max, string label)
        {
            if (existing && value == previous) return previous;
            value = value?.Trim();
            if (value?.Length > max || value?.Contains('\0') == true)
                throw new ArgumentException(L.Format("MainViewModel.0IsLimitedTo1ValidCharacters", (label), (max)));
            return string.IsNullOrEmpty(value) ? null : value;
        }
        return account with
        {
            Name = Field(input.Name, account.Name, 120, L.Get("MainViewModel.TheNameOfTheAccount"))
                ?? throw new ArgumentException(L.Get("MainViewModel.EnterAnAccountName")),
            Type = Field(input.Type, account.Type, 80, L.Get("MainViewModel.TheGuy")),
            Notes = Field(input.Notes, account.Notes, 2000, L.Get("MainViewModel.Notes")),
            BankName = Field(input.BankName, account.BankName, 120, L.Get("MainViewModel.TheNameOfTheBank")),
            BankReference = Field(input.BankReference, account.BankReference, 120, L.Get("MainViewModel.TheBankingReference")),
            Holder = Field(input.Holder, account.Holder, 120, L.Get("MainViewModel.TheHolder"))
        };
    }
}
