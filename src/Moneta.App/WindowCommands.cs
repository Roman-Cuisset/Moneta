using System.Windows.Input;

namespace Moneta.App;

public static class WindowCommands
{
    public static RoutedUICommand About { get; } = new(L.Get("Common.AboutMoneta"), nameof(About), typeof(WindowCommands));
    public static RoutedUICommand CheckUpdates { get; } = new(L.Get("WindowCommands.SearchForUpdates"), nameof(CheckUpdates), typeof(WindowCommands));
    public static RoutedUICommand Settings { get; } = new(L.Get("Common.Options"), nameof(Settings), typeof(WindowCommands));
    public static RoutedUICommand Schedule { get; } = new(L.Get("Common.ScheduledTransactions"), nameof(Schedule), typeof(WindowCommands),
        [new KeyGesture(Key.E, ModifierKeys.Control)]);
    public static RoutedUICommand AddAccount { get; } = new(L.Get("Common.AddAccount"), nameof(AddAccount), typeof(WindowCommands));
    public static RoutedUICommand AccountProperties { get; } = new(L.Get("Common.AccountProperties"), nameof(AccountProperties), typeof(WindowCommands));
    public static RoutedUICommand ArchiveAccount { get; } = new(L.Get("WindowCommands.ArchiveAccount"), nameof(ArchiveAccount), typeof(WindowCommands));
    public static RoutedUICommand ReactivateAccount { get; } = new(L.Get("WindowCommands.ReactivateAccount"), nameof(ReactivateAccount), typeof(WindowCommands));
    public static RoutedUICommand MoveAccount { get; } = new(L.Get("WindowCommands.MoveAccount"), nameof(MoveAccount), typeof(WindowCommands));
    public static RoutedUICommand DeleteAccount { get; } = new(L.Get("WindowCommands.RemoveABlankAccount"), nameof(DeleteAccount), typeof(WindowCommands));
    public static RoutedUICommand Theme { get; } = new(L.Get("WindowCommands.Theme"), nameof(Theme), typeof(WindowCommands));
    public static RoutedUICommand Open { get; } = new(L.Get("Common.Open"), nameof(Open), typeof(WindowCommands), [new KeyGesture(Key.O, ModifierKeys.Control)]);
    public static RoutedUICommand ImportBankPerfect { get; } = new(L.Get("WindowCommands.ImportBankperfect"), nameof(ImportBankPerfect), typeof(WindowCommands));
    public static RoutedUICommand Save { get; } = new(L.Get("Common.Save"), nameof(Save), typeof(WindowCommands), [new KeyGesture(Key.S, ModifierKeys.Control)]);
    public static RoutedUICommand New { get; } = new(L.Get("WindowCommands.NewDocument"), nameof(New), typeof(WindowCommands), [new KeyGesture(Key.N, ModifierKeys.Control)]);
    public static RoutedUICommand SaveAs { get; } = new(L.Get("Common.SaveAs"), nameof(SaveAs), typeof(WindowCommands), [new KeyGesture(Key.F12)]);
    public static RoutedUICommand Backup { get; } = new(L.Get("Common.SaveDocument"), nameof(Backup), typeof(WindowCommands));
    public static RoutedUICommand BackupHistory { get; } = new(L.Get("Common.BackupHistory"), nameof(BackupHistory), typeof(WindowCommands));
    public static RoutedUICommand Restore { get; } = new(L.Get("WindowCommands.RestoreBackup"), nameof(Restore), typeof(WindowCommands));
    public static RoutedUICommand Demo { get; } = new(L.Get("WindowCommands.OpenTheDemo"), nameof(Demo), typeof(WindowCommands));
    // No Ctrl+Z/Y window bindings: native text editing retains its own undo/redo.
    public static RoutedUICommand FinancialUndo { get; } = new(L.Get("WindowCommands.CancelWriting"), nameof(FinancialUndo), typeof(WindowCommands));
    public static RoutedUICommand FinancialRedo { get; } = new(L.Get("WindowCommands.Rewrite"), nameof(FinancialRedo), typeof(WindowCommands));
    public static RoutedUICommand Search { get; } = new(L.Get("Common.Search"), nameof(Search), typeof(WindowCommands));
    public static RoutedUICommand SearchFilters { get; } = new(L.Get("Common.AdvancedFilters"), nameof(SearchFilters), typeof(WindowCommands), [new KeyGesture(Key.F7)]);
    public static RoutedUICommand ClearSearch { get; } = new(L.Get("Common.ClearFilters"), nameof(ClearSearch), typeof(WindowCommands), [new KeyGesture(Key.F7, ModifierKeys.Shift)]);
    // Register-only key bindings live on the DataGrid, never on a text editor or dialog.
    public static RoutedUICommand NewTransaction { get; } = new(L.Get("Common.NewTransaction"), nameof(NewTransaction), typeof(WindowCommands));
    public static RoutedUICommand EditTransaction { get; } = new(L.Get("WindowCommands.EditAnTransaction"), nameof(EditTransaction), typeof(WindowCommands));
    public static RoutedUICommand DeleteTransaction { get; } = new(L.Get("WindowCommands.DeleteAnTransaction"), nameof(DeleteTransaction), typeof(WindowCommands));
    public static RoutedUICommand NewTransfer { get; } = new(L.Get("Common.Transfer"), nameof(NewTransfer), typeof(WindowCommands));
    public static RoutedUICommand OpenTransfer { get; } = new(L.Get("Common.OpenTheLinkedTransfer"), nameof(OpenTransfer), typeof(WindowCommands));
    public static RoutedUICommand NavigateTransfer { get; } = new(L.Get("Common.OtherSideOfTransfer"), nameof(NavigateTransfer), typeof(WindowCommands));
    public static RoutedUICommand DeleteTransfer { get; } = new(L.Get("WindowCommands.DeleteTheEntireTransfer"), nameof(DeleteTransfer), typeof(WindowCommands));
    public static RoutedUICommand TogglePointing { get; } = new(L.Get("WindowCommands.ToggleTheScore"), nameof(TogglePointing), typeof(WindowCommands));
    public static RoutedUICommand MarkTransactions { get; } = new("Pointer", nameof(MarkTransactions), typeof(WindowCommands));
    public static RoutedUICommand UnmarkTransactions { get; } = new(L.Get("WindowCommands.Depoint"), nameof(UnmarkTransactions), typeof(WindowCommands));
    public static RoutedUICommand MarkingDate { get; } = new(L.Get("Common.ClearingDate"), nameof(MarkingDate), typeof(WindowCommands));
    public static RoutedUICommand ShowMarkingDates { get; } = new(L.Get("WindowCommands.ShowScoreDates"), nameof(ShowMarkingDates), typeof(WindowCommands));
    public static RoutedUICommand Reconcile { get; } = new(L.Get("Common.BankReconciliation"), nameof(Reconcile), typeof(WindowCommands));
    public static RoutedUICommand ReleaseLegacyReconciliation { get; } = new(L.Get("WindowCommands.ReExaminingAnInheritedCloseState"), nameof(ReleaseLegacyReconciliation), typeof(WindowCommands));
    public static RoutedUICommand RefreshRates { get; } = new(L.Get("WindowCommands.UpdatingRates"), nameof(RefreshRates), typeof(WindowCommands));
    public static RoutedUICommand ShowRates { get; } = new(L.Get("WindowCommands.ExchangeRates"), nameof(ShowRates), typeof(WindowCommands));
    public static RoutedUICommand HistoricalConverter { get; } = new("Convertisseur historique", nameof(HistoricalConverter), typeof(WindowCommands));
    public static RoutedUICommand HistoricalFlow { get; } = new(L.Get("WindowCommands.ValueOfTheRecordedTransaction"), nameof(HistoricalFlow), typeof(WindowCommands));
    public static RoutedUICommand HistoricalBalance { get; } = new(L.Get("Common.ValorizationOfBalanceToDate"), nameof(HistoricalBalance), typeof(WindowCommands));
    public static RoutedUICommand Reports { get; } = new(L.Get("WindowCommands.Reports"), nameof(Reports), typeof(WindowCommands));
    public static RoutedUICommand Budgets { get; } = new(L.Get("Common.Budgets"), nameof(Budgets), typeof(WindowCommands), new InputGestureCollection { new KeyGesture(Key.B, ModifierKeys.Control) });
    public static RoutedUICommand CategoryReports { get; } = new(L.Get("Common.TotalsByCategory"), nameof(CategoryReports), typeof(WindowCommands), new InputGestureCollection { new KeyGesture(Key.T, ModifierKeys.Control) });
}
