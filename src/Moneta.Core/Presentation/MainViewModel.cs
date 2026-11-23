using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel : INotifyPropertyChanged, ITransferSource
{
    private static readonly PropertyChangedEventArgs AccountChanged = new(nameof(SelectedAccount));
    private static readonly PropertyChangedEventArgs RowsChanged = new(nameof(Rows));
    private static readonly PropertyChangedEventArgs BalanceChanged = new(nameof(SelectedAccountBalanceText));
    private static readonly PropertyChangedEventArgs RowChanged = new(nameof(SelectedRow));
    private static readonly PropertyChangedEventArgs DetailsChanged = new(nameof(SelectedRowDetails));
    private static readonly PropertyChangedEventArgs StatusChanged = new(nameof(StatusText));
    private static readonly PropertyChangedEventArgs DisplayCurrencyChanged = new(nameof(DisplayCurrency));
    private static readonly PropertyChangedEventArgs DisplayBalanceChanged = new(nameof(DisplayBalanceText));
    private static readonly PropertyChangedEventArgs DisplayRateChanged = new(nameof(DisplayRateDescription));
    private static readonly PropertyChangedEventArgs DirtyChanged = new(nameof(IsDirty));
    private static readonly PropertyChangedEventArgs SelectedAccountIdChanged = new(nameof(SelectedAccountId));
    private readonly ObservableCollection<AccountSummary> _accounts = [];
    private readonly Dictionary<Guid, AccountLedger> _ledgers = [];
    private AccountSummary? _selectedAccount;
    private Currency? _displayCurrency;
    private LedgerRow? _selectedRow;
    private IReadOnlyList<LedgerRow> _rows = Array.Empty<LedgerRow>();
    private string _selectedRowDetails = string.Empty;
    private FinancialDocument? _document;
    private const int PageSize = 100;
    private ILedgerSource? _source;
    private Dictionary<Guid, TransactionChange> _pending = [];
    private int _pageOffset;
    private long _totalCount;
    private string _ledgerErrorText = string.Empty;

    public MainViewModel(DemoPortfolio portfolio, CurrencyRegistry? currencies = null)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        CurrencyRegistry = currencies ?? new CurrencyRegistry();
        Accounts = new ReadOnlyObservableCollection<AccountSummary>(_accounts);
        var accountsById = portfolio.Accounts.ToDictionary(account => account.Id);
        foreach (var transaction in portfolio.Transactions)
        {
            if (!accountsById.TryGetValue(transaction.AccountId, out var account))
                throw new ArgumentException(L.Get("MainViewModel.ATransactionRefersToAnAccountThatIs"), nameof(portfolio));
            if (transaction.Currency != account.Currency)
                throw new ArgumentException(
                    L.Format("MainViewModel.CurrencyIncompatibleOnAccount01InsteadOf", (account.Name), (transaction.Currency.Code), (account.Currency.Code)),
                    nameof(portfolio));
            if (transaction.Amount < 0)
                throw new ArgumentOutOfRangeException(nameof(portfolio), L.Get("MainViewModel.TheAmountOfATransactionMustBeA"));
            if (transaction.Direction is not (TransactionDirection.Debit or TransactionDirection.Credit))
                throw new ArgumentOutOfRangeException(nameof(portfolio), L.Get("Common.UnknownSenseOfTransaction"));
        }

        var transactionsByAccount = portfolio.Transactions.ToLookup(transaction => transaction.AccountId);
        foreach (var account in portfolio.Accounts)
        {
            if (!CurrencyRegistry.TryGet(account.Currency.Code, out var registered))
                CurrencyRegistry.Register(account.Currency);
            else if (registered!.DecimalPlaces != account.Currency.DecimalPlaces)
                throw new ArgumentException(L.Get("MainViewModel.TheAccuracyOfTheAccountCurrencyDiffersFrom"), nameof(portfolio));
        }
        for (var index = 0; index < portfolio.Accounts.Count; index++)
        {
            var account = portfolio.Accounts[index];
            var balance = account.OpeningBalance;
            // OrderBy is stable: equal-date entries retain their order in the supplied portfolio.
            var transactions = transactionsByAccount[account.Id].OrderBy(transaction => transaction.Date).ToArray();
            var rows = new LedgerRow[transactions.Length];
            for (var rowIndex = 0; rowIndex < transactions.Length; rowIndex++)
            {
                var transaction = transactions[rowIndex];
                balance = transaction.Direction == TransactionDirection.Credit
                    ? checked(balance + transaction.Amount)
                    : checked(balance - transaction.Amount);
                rows[rowIndex] = new LedgerRow(transaction, balance);
            }

            var summary = new AccountSummary(account, balance, index);
            _accounts.Add(summary);
            _ledgers.Add(account.Id, new AccountLedger(summary, Array.AsReadOnly(rows)));
        }

        SelectedAccount = Accounts.FirstOrDefault();
    }

    public MainViewModel(FinancialDocument document, ILedgerSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _savedScheduleState = document;
        FinancialRules.ValidateCatalog(document);
        _source = source;
        CurrencyRegistry = new CurrencyRegistry(document.Currencies);
        Accounts = new ReadOnlyObservableCollection<AccountSummary>(_accounts);
        foreach (var account in document.Accounts)
        {
            if (!CurrencyRegistry.TryGet(account.Currency.Code, out var currency) || currency != account.Currency)
                throw new ArgumentException(L.Get("MainViewModel.TheCurrencyOfTheAccountDiffersFromThe"), nameof(document));
            _ = ExactMoney.ToMinorUnits(account.OpeningBalance, account.Currency);
            AddAccountSummary(account);
            if (document.AccountBalances?.TryGetValue(account.Id, out var balance) == true)
                _ledgers[account.Id].Summary.UpdateBalance(balance);
        }

        if (document.SelectedAccountId is { } selectedId)
        {
            if (!_ledgers.TryGetValue(selectedId, out var ledger))
                throw new ArgumentException(L.Get("MainViewModel.TheSelectedAccountIsMissingFromTheDocument"), nameof(document));
            _showArchivedAccounts = ledger.Summary.Account.IsArchived;
            SelectedAccount = ledger.Summary;
        }
    }

    public bool IsDemo => _document is null;
    public bool IsDirty { get; private set; }
    public string DocumentName => _document?.Name ?? L.Get("MainViewModel.Demo");
    public Guid DocumentId => _document?.Id ?? Guid.Empty;
    public Guid? SelectedAccountId => _selectedAccount?.Account.Id;
    public IReadOnlyCollection<TransactionChange> PendingChanges => Array.AsReadOnly(_pending.Values.ToArray());
    public IReadOnlyList<LedgerCategory> Categories => _document?.Categories ?? Array.Empty<LedgerCategory>();
    public IReadOnlyList<LedgerPayee> Payees => _document?.Payees ?? Array.Empty<LedgerPayee>();
    public IReadOnlyList<PaymentMethod> PaymentMethods => _document?.PaymentMethods ?? Array.Empty<PaymentMethod>();
    public bool CanPreviousPage => !IsDemo && _pageOffset > 0;
    public bool CanNextPage => !IsDemo && _pageOffset + (long)_rows.Count < _totalCount;
    public string PageStatusText => IsDemo ? string.Empty : _totalCount == 0
        ? L.Get("MainViewModel.NoTransaction")
        : $"{_pageOffset + 1:N0}–{_pageOffset + _rows.Count:N0} / {_totalCount:N0}";
    public string LedgerErrorText => _ledgerErrorText;

    public FinancialDocument CaptureDocument()
    {
        var document = _document ?? throw new InvalidOperationException(L.Get("MainViewModel.TheDemonstrationCannotBeRecordedInADocument"));
        return document with
        {
            ModifiedUtc = IsDirty ? DateTimeOffset.UtcNow : document.ModifiedUtc,
            Currencies = CurrencyRegistry.Currencies.ToArray(),
            Accounts = _accounts.Select(summary => summary.Account).ToArray(),
            SelectedAccountId = SelectedAccountId
        };
    }

    public void AcceptSaved(FinancialDocument snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var document = _document ?? throw new InvalidOperationException(L.Get("MainViewModel.TheDemonstrationCannotBeRecordedInADocument"));
        if (snapshot.Id != document.Id)
            throw new ArgumentException(L.Get("MainViewModel.TheSavedDocumentDoesNotMatchTheCurrent"), nameof(snapshot));
        FinancialRules.ValidateCatalog(snapshot);
        // SQL has already committed. A refresh error must not resurrect committed drafts.
        _document = snapshot;
        _savedScheduleState = snapshot;
        _pending.Clear();
        _pendingTransfers.Clear();
        _pendingReconciliations = ReconciliationBatch.Empty;
        foreach (var summary in _accounts)
            if (snapshot.AccountBalances?.TryGetValue(summary.Account.Id, out var balance) == true)
                summary.UpdateBalance(balance);
        try
        {
            if (_selectedAccount is not null)
            {
                var page = ReadPage(_selectedAccount.Account, _pageOffset, Array.Empty<TransactionChange>());
                ApplyPage(page, BuildRows(page), _selectedRow?.Transaction.Id);
            }
            SetLedgerError(string.Empty);
        }
        catch (Exception exception)
        {
            SetLedgerError(L.Format("MainViewModel.RecordMadeRecordReadingImpossible0", (exception.Message)));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingChanges)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingTransfers)));
        if (IsDirty)
        {
            IsDirty = false;
            PropertyChanged?.Invoke(this, DirtyChanged);
        }
        NotifyReconciliationChanged();
        NotifySchedules();
    }

    public void SetLedgerSource(ILedgerSource? source)
    {
        if (IsDemo && source is not null)
            throw new InvalidOperationException(L.Get("MainViewModel.TheDemonstrationCannotBeLinkedToAReal"));
        // The workspace calls this after committing and before AcceptSaved; do not read
        // the newly committed source with the old pending insert revisions here.
        _source = source;
        NotifyReconciliationChanged();
    }

    public Transaction AddTransaction(TransactionDraft draft)
    {
        var account = EditableAccount();
        var catalog = new DraftCatalog(CaptureDocument());
        var value = catalog.Build(account, draft);
        Stage(catalog.Capture(), new TransactionChange(value, 0), value.Id);
        return value;
    }

    public Transaction EditTransaction(Guid id, long expectedRevision, TransactionDraft draft)
    {
        var account = EditableAccount();
        var previous = FindEditable(id, expectedRevision, account);
        EnsureOrdinaryTransaction(id);
        EnsureNotReconciled(previous);
        var catalog = new DraftCatalog(CaptureDocument());
        var originalRevision = _pending.TryGetValue(id, out var pending) ? pending.ExpectedRevision : previous.Revision;
        var value = catalog.Build(account, draft, previous) with
        {
            Revision = originalRevision == 0 ? 1 : checked(originalRevision + 1)
        };
        Stage(catalog.Capture(), new TransactionChange(value, originalRevision), id);
        return value;
    }

    public void DeleteTransaction(Guid id, long expectedRevision)
    {
        var account = EditableAccount();
        var previous = FindEditable(id, expectedRevision, account);
        EnsureOrdinaryTransaction(id);
        EnsureNotReconciled(previous);
        if (_pending.TryGetValue(id, out var pending) && pending.ExpectedRevision == 0)
        {
            Stage(CaptureDocument(), null, null, id);
            return;
        }
        var originalRevision = pending?.ExpectedRevision ?? previous.Revision;
        var now = DateTimeOffset.UtcNow;
        if (now < previous.ModifiedUtc) now = previous.ModifiedUtc;
        Stage(CaptureDocument(), new TransactionChange(previous with
        {
            IsDeleted = true, ModifiedUtc = now, Revision = checked(originalRevision + 1)
        }, originalRevision), null);
    }

    public void NextPage()
    {
        if (CanNextPage) ChangePage(checked(_pageOffset + PageSize));
    }

    public void PreviousPage()
    {
        if (CanPreviousPage) ChangePage(Math.Max(0, _pageOffset - PageSize));
    }

    private Account EditableAccount()
    {
        if (IsDemo) throw new InvalidOperationException(L.Get("MainViewModel.DemonstrationTransactionsAreNotEditable"));
        if (!_pendingReconciliations.IsEmpty)
            throw new InvalidOperationException(L.Get("MainViewModel.FirstRecordTheReconciliationDraftWithCtrlS"));
        var account = _selectedAccount?.Account ?? throw new InvalidOperationException(L.Get("MainViewModel.ChooseAnAccount"));
        if (account.IsArchived) throw new InvalidOperationException(L.Get("MainViewModel.TheTransactionsOfAnArchivedAccountAreNot"));
        return account;
    }

    private Transaction FindEditable(Guid id, long revision, Account account)
    {
        ProtectScheduledDraft(id, transfer: false);
        var value = _pending.TryGetValue(id, out var change) ? change.Value
            : _rows.FirstOrDefault(row => row.Transaction.Id == id)?.Transaction;
        if (value is null || value.AccountId != account.Id || value.IsDeleted)
            throw new InvalidOperationException(L.Get("MainViewModel.TheTransactionDoesNotBelongToTheCurrent"));
        if (value.Revision != revision)
            throw new InvalidOperationException(L.Get("MainViewModel.ThisTransactionHasChangedOpenItsCurrentVersion"));
        return value;
    }

    private void Stage(FinancialDocument metadata, TransactionChange? change, Guid? anchorId, Guid? removeId = null)
    {
        FinancialRules.ValidateCatalog(metadata);
        if (change is not null) FinancialRules.ValidateTransaction(change.Value, metadata);
        var proposed = new Dictionary<Guid, TransactionChange>(_pending);
        if (removeId is { } removed) proposed.Remove(removed);
        if (change is not null) proposed[change.Value.Id] = change;
        var pending = proposed.Values.ToArray();
        var selected = _selectedAccount ?? _ledgers[change!.Value.AccountId].Summary;
        LedgerPage? affected = null;
        if (change is not null && change.Value.AccountId != selected.Account.Id)
        {
            affected = ReadPage(_ledgers[change.Value.AccountId].Summary.Account, 0, pending);
            anchorId = _selectedRow?.Transaction.Id;
        }
        var page = ReadPage(selected.Account, _pageOffset, pending, anchorId);
        var rows = BuildRows(page);
        // Publish only after validation, source reads and exact progressive balances succeeded.
        _document = metadata;
        _pending = proposed;
        if (affected is not null) _ledgers[change!.Value.AccountId].Summary.UpdateBalance(affected.AccountBalance);
        SelectAccount(selected, page, rows);
        ApplyPage(page, rows, anchorId);
        SetLedgerError(string.Empty);
        MarkDirty();
        foreach (var property in new[] { nameof(PendingChanges), nameof(Categories), nameof(Payees), nameof(PaymentMethods) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private LedgerPage ReadPage(Account account, int offset, IReadOnlyCollection<TransactionChange> pending, Guid? anchorId = null)
    {
        if (_source is not null) return _source.ReadPage(account, offset, PageSize, pending, anchorId);
        if (_document?.OperationCount > 0)
            throw new InvalidOperationException(L.Get("MainViewModel.ThePersistentDocumentRegistryIsNotLoggedIn"));
        return LedgerProjection.ReadPending(account, offset, PageSize, pending, anchorId);
    }

    private IReadOnlyList<LedgerRow> BuildRows(LedgerPage page, IReadOnlyDictionary<Guid, TransferChange>? transfers = null)
    {
        var balance = page.BalanceBeforePage;
        var rows = new LedgerRow[page.Transactions.Count];
        var links = BuildTransferLinks(page, transfers ?? _pendingTransfers);
        for (var index = 0; index < rows.Length; index++)
        {
            var transaction = page.Transactions[index];
            balance = checked(balance + FinancialRules.SignedAmount(transaction));
            rows[index] = new LedgerRow(transaction, balance, _showMarkingDates,
                links.TryGetValue(transaction.Id, out var transferId) ? transferId : null);
        }
        return Array.AsReadOnly(rows);
    }

    private void ChangePage(int offset)
    {
        var page = ReadPage(_selectedAccount!.Account, offset, PendingChanges);
        var rows = BuildRows(page);
        ApplyPage(page, rows);
        SetLedgerError(string.Empty);
    }

    private void ApplyPage(LedgerPage page, IReadOnlyList<LedgerRow> rows, Guid? anchorId = null)
    {
        _rows = rows;
        _pageOffset = page.Offset;
        _totalCount = page.TotalCount;
        _selectedAccount!.UpdateBalance(page.AccountBalance);
        _totals = page.Totals;
        NotifyReconciliationChanged();
        _selectedRow = anchorId is { } anchor ? rows.FirstOrDefault(row => row.Transaction.Id == anchor) : null;
        _selectedRowDetails = _selectedRow is null ? string.Empty : FormatDetails(_selectedRow);
        foreach (var args in new[] { RowsChanged, BalanceChanged, RowChanged, DetailsChanged, StatusChanged })
            PropertyChanged?.Invoke(this, args);
        NotifyDisplayCurrencyChanged();
        NotifyPagingChanged();
    }

    private void NotifyPagingChanged()
    {
        foreach (var property in new[] { nameof(CanNextPage), nameof(CanPreviousPage), nameof(PageStatusText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private void SetLedgerError(string message)
    {
        _ledgerErrorText = message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LedgerErrorText)));
    }

    private void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        PropertyChanged?.Invoke(this, DirtyChanged);
        NotifyReconciliationChanged();
    }

    public ReadOnlyObservableCollection<AccountSummary> Accounts { get; }
    public CurrencyRegistry CurrencyRegistry { get; }
    public ReadOnlyObservableCollection<Currency> DisplayCurrencies => PresentationCurrencies;

    public Currency? DisplayCurrency
    {
        get => _displayCurrency;
        set
        {
            Currency? registered = null;
            if (value is not null)
            {
                if (!TryPresentationCurrency(value.Code, out registered) || registered != value)
                    throw new ArgumentException(L.Get("MainViewModel.TheDisplayCurrencyIsNotPartOfThe"), nameof(value));
            }
            else if (_selectedAccount is not null)
            {
                throw new ArgumentException(L.Get("MainViewModel.ChooseADisplayCurrency"), nameof(value));
            }
            if (ReferenceEquals(registered, _displayCurrency))
                return;
            _displayCurrency = registered;
            NotifyDisplayCurrencyChanged();
        }
    }

    public string DisplayBalanceText => _projectedBalance;
    public string DisplayRateDescription => _projectedRateDescription;

    private bool IsNativeDisplay => _displayCurrency?.Code == _selectedAccount?.CurrencyCode;

    public AccountSummary AddDemoAccount(string name, Currency currency, string? type, decimal openingBalance, string? notes)
    {
        if (!IsDemo)
            throw new InvalidOperationException(L.Get("MainViewModel.AFictitiousAccountCannotBeAddedToA"));
        return AddAccountCore(new(name, type, notes), currency, openingBalance);
    }

    public AccountSummary AddAccount(string name, Currency currency, string? type, decimal openingBalance, string? notes)
        => AddAccount(new(name, type, notes), currency, openingBalance);

    public AccountSummary AddAccount(AccountProperties properties, Currency currency, decimal openingBalance)
    {
        if (IsDemo)
            throw new InvalidOperationException(L.Get("MainViewModel.ARealAccountCannotBeAddedToThe"));
        ArgumentNullException.ThrowIfNull(properties);
        return AddAccountCore(properties, currency, openingBalance);
    }

    private AccountSummary AddAccountCore(AccountProperties properties, Currency currency, decimal openingBalance)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (!CurrencyRegistry.TryGet(currency.Code, out var registered) || registered != currency)
            throw new ArgumentException(L.Get("MainViewModel.ChooseACurrencyFromTheRepository"), nameof(currency));
        _ = ExactMoney.ToMinorUnits(openingBalance, currency);

        var now = DateTimeOffset.UtcNow;
        var account = new Account(Guid.NewGuid(), properties.Name, currency, openingBalance)
        {
            CreatedUtc = now,
            ModifiedUtc = now
        };
        account = ApplyAccountProperties(account, properties, existing: false);
        var page = IsDemo ? null : ReadPage(account, 0, PendingChanges);
        var rows = page is null ? null : BuildRows(page);
        var summary = AddAccountSummary(account);
        NotifyVisibleAccounts();
        SelectAccount(summary, page, rows);
        if (!IsDemo) MarkDirty();
        return summary;
    }

    private AccountSummary AddAccountSummary(Account account)
    {
        var summary = new AccountSummary(account, account.OpeningBalance, _accounts.Count);
        _ledgers.Add(account.Id, new AccountLedger(summary, Array.Empty<LedgerRow>()));
        _accounts.Add(summary);
        return summary;
    }

    private string EmptyAccountStatus => IsDemo
        ? L.Get("MainViewModel.NoAccountSelectedFictiveDataInMemoryOnly")
        : L.Get("Common.NoAccountSelected");

    public AccountSummary? SelectedAccount
    {
        get => _selectedAccount;
        set => SelectAccount(value);
    }

    private void SelectAccount(AccountSummary? value, LedgerPage? preparedPage = null, IReadOnlyList<LedgerRow>? preparedRows = null)
    {
        if (ReferenceEquals(value, _selectedAccount))
            return;
        if (value is not null &&
            (!_ledgers.TryGetValue(value.Account.Id, out var candidate) || !ReferenceEquals(candidate.Summary, value)))
            throw new ArgumentException(L.Get("MainViewModel.TheSelectedAccountIsNotPartOfThis"), nameof(value));

        var page = preparedPage ?? (!IsDemo && value is not null
            ? ReadPage(value.Account, 0, PendingChanges) : null);
        var rows = preparedRows ?? (page is null ? null : BuildRows(page));
        _selectedAccount = value;
        var ledger = value is null ? null : _ledgers[value.Account.Id];
        _rows = rows ?? ledger?.Rows ?? Array.Empty<LedgerRow>();
        if (rows is null && _showMarkingDates)
            _rows = Array.AsReadOnly(_rows.Select(row => new LedgerRow(row.Transaction, row.Balance, true, row.TransferId)).ToArray());
        _pageOffset = page?.Offset ?? 0;
        _totalCount = page?.TotalCount ?? 0;
        if (page is not null) value!.UpdateBalance(page.AccountBalance);
        _totals = page?.Totals ?? (IsDemo && value is not null ? DemoTotals(value.Account, _rows) : null);
        NotifyReconciliationChanged();
        _selectedRow = null;
        _selectedRowDetails = string.Empty;
        _displayCurrency = value is null ? null : CurrencyRegistry.Get(value.CurrencyCode);
        SetLedgerError(string.Empty);
        PropertyChanged?.Invoke(this, AccountChanged);
        PropertyChanged?.Invoke(this, SelectedAccountIdChanged);
        PropertyChanged?.Invoke(this, RowsChanged);
        PropertyChanged?.Invoke(this, BalanceChanged);
        PropertyChanged?.Invoke(this, RowChanged);
        PropertyChanged?.Invoke(this, DetailsChanged);
        PropertyChanged?.Invoke(this, StatusChanged);
        NotifyDisplayCurrencyChanged();
        NotifyPagingChanged();
    }

    public IReadOnlyList<LedgerRow> Rows => _rows;

    public LedgerRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(value, _selectedRow))
                return;
            if (value is not null && !_rows.Contains(value))
                throw new ArgumentException(L.Get("MainViewModel.TheSelectedTransactionDoesNotBelongToThe"), nameof(value));
            _selectedRow = value;
            _selectedRowDetails = value is null ? string.Empty : FormatDetails(value);
            PropertyChanged?.Invoke(this, RowChanged);
            PropertyChanged?.Invoke(this, DetailsChanged);
        }
    }

    public string SelectedAccountBalanceText => _selectedAccount?.BalanceText ?? string.Empty;
    public string SelectedRowDetails => _selectedRowDetails;
    public string StatusText => _selectedAccount is null ? EmptyAccountStatus
        : L.Format(IsDemo ? "MainViewModel.0Transactions12FictitiousDataInMemory" : "MainViewModel.0Transactions12",
            (IsDemo ? _rows.Count : _totalCount).ToString("N0", L.Culture), _selectedAccount.Name, _selectedAccount.CurrencyCode);

    public void RefreshDisplayLanguage()
    {
        foreach (var account in Accounts) account.RefreshDisplayLanguage();
        foreach (var row in _rows) row.RefreshDisplayLanguage();
        _selectedRowDetails = _selectedRow is { } rowWithDetails ? FormatDetails(rowWithDetails) : string.Empty;
        PropertyChanged?.Invoke(this, DetailsChanged);
        PropertyChanged?.Invoke(this, StatusChanged);
        PropertyChanged?.Invoke(this, BalanceChanged);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateColumnHeader)));
        NotifyReconciliationChanged();
        NotifyDisplayCurrencyChanged();
        NotifyPagingChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyDisplayCurrencyChanged()
    {
        RefreshRateProjection();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayCurrencies)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NativeBalanceDescription)));
        PropertyChanged?.Invoke(this, DisplayCurrencyChanged);
        PropertyChanged?.Invoke(this, DisplayBalanceChanged);
        PropertyChanged?.Invoke(this, DisplayRateChanged);
    }

    private static string FormatDetails(LedgerRow row)
    {
        var transaction = row.Transaction;
        var valuationText = transaction.Valuation is { } valuation
            ? L.Format("MainViewModel.IndicativeValue0RateOf1Source2", MoneyFormatter.Format(valuation.Amount, valuation.Currency), valuation.RateDate.ToString(L.DatePattern, L.Culture), valuation.Source)
            : L.Get("MainViewModel.NoValuation");
        var directionText = transaction.Direction == TransactionDirection.Debit ? L.Get("MainViewModel.FlowRate") : L.Get("MainViewModel.Credit");
        return L.Format("MainViewModel.Date0Wording1Payee2Category3", (row.DateText), (row.Label), (row.PayeeName), (row.CategoryName), (row.SubcategoryName), (row.PaymentModeName), (directionText), (MoneyFormatter.Format(transaction.Amount, transaction.Currency)), (row.MarkText), (transaction.Details), (valuationText));
    }

    private sealed record AccountLedger(AccountSummary Summary, IReadOnlyList<LedgerRow> Rows);
}
