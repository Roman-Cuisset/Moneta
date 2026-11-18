using System.Windows.Input;

namespace Moneta.App;

public static class WindowCommands
{
    public static RoutedUICommand About { get; } = new("À propos de Moneta", nameof(About), typeof(WindowCommands));
    public static RoutedUICommand Settings { get; } = new("Paramètres", nameof(Settings), typeof(WindowCommands));
    public static RoutedUICommand AddAccount { get; } = new("Ajouter un compte", nameof(AddAccount), typeof(WindowCommands));
    public static RoutedUICommand AccountProperties { get; } = new("Propriétés du compte", nameof(AccountProperties), typeof(WindowCommands));
    public static RoutedUICommand ArchiveAccount { get; } = new("Archiver le compte", nameof(ArchiveAccount), typeof(WindowCommands));
    public static RoutedUICommand ReactivateAccount { get; } = new("Réactiver le compte", nameof(ReactivateAccount), typeof(WindowCommands));
    public static RoutedUICommand MoveAccount { get; } = new("Déplacer le compte", nameof(MoveAccount), typeof(WindowCommands));
    public static RoutedUICommand DeleteAccount { get; } = new("Supprimer un compte vierge", nameof(DeleteAccount), typeof(WindowCommands));
    public static RoutedUICommand Theme { get; } = new("Thème", nameof(Theme), typeof(WindowCommands));
    public static RoutedUICommand Open { get; } = new("Ouvrir", nameof(Open), typeof(WindowCommands), [new KeyGesture(Key.O, ModifierKeys.Control)]);
    public static RoutedUICommand ImportBankPerfect { get; } = new("Importer BankPerfect", nameof(ImportBankPerfect), typeof(WindowCommands));
    public static RoutedUICommand Save { get; } = new("Enregistrer", nameof(Save), typeof(WindowCommands), [new KeyGesture(Key.S, ModifierKeys.Control)]);
    public static RoutedUICommand New { get; } = new("Nouveau dossier", nameof(New), typeof(WindowCommands), [new KeyGesture(Key.N, ModifierKeys.Control)]);
    public static RoutedUICommand SaveAs { get; } = new("Enregistrer sous", nameof(SaveAs), typeof(WindowCommands), [new KeyGesture(Key.F12)]);
    public static RoutedUICommand Backup { get; } = new("Sauvegarder le dossier", nameof(Backup), typeof(WindowCommands));
    public static RoutedUICommand Restore { get; } = new("Restaurer une sauvegarde", nameof(Restore), typeof(WindowCommands));
    public static RoutedUICommand Demo { get; } = new("Ouvrir la démonstration", nameof(Demo), typeof(WindowCommands));
    // No Ctrl+Z/Y window bindings: native text editing retains its own undo/redo.
    public static RoutedUICommand FinancialUndo { get; } = new("Annuler une écriture", nameof(FinancialUndo), typeof(WindowCommands));
    public static RoutedUICommand FinancialRedo { get; } = new("Refaire une écriture", nameof(FinancialRedo), typeof(WindowCommands));
    public static RoutedUICommand Search { get; } = new("Rechercher", nameof(Search), typeof(WindowCommands));
    public static RoutedUICommand SearchFilters { get; } = new("Filtres avancés", nameof(SearchFilters), typeof(WindowCommands), [new KeyGesture(Key.F7)]);
    public static RoutedUICommand ClearSearch { get; } = new("Effacer les filtres", nameof(ClearSearch), typeof(WindowCommands), [new KeyGesture(Key.F7, ModifierKeys.Shift)]);
    // Register-only key bindings live on the DataGrid, never on a text editor or dialog.
    public static RoutedUICommand NewTransaction { get; } = new("Nouvelle opération", nameof(NewTransaction), typeof(WindowCommands));
    public static RoutedUICommand EditTransaction { get; } = new("Modifier une opération", nameof(EditTransaction), typeof(WindowCommands));
    public static RoutedUICommand DeleteTransaction { get; } = new("Supprimer une opération", nameof(DeleteTransaction), typeof(WindowCommands));
    public static RoutedUICommand NewTransfer { get; } = new("Virement", nameof(NewTransfer), typeof(WindowCommands));
    public static RoutedUICommand OpenTransfer { get; } = new("Ouvrir le virement lié", nameof(OpenTransfer), typeof(WindowCommands));
    public static RoutedUICommand NavigateTransfer { get; } = new("Autre côté du virement", nameof(NavigateTransfer), typeof(WindowCommands));
    public static RoutedUICommand DeleteTransfer { get; } = new("Supprimer le virement entier", nameof(DeleteTransfer), typeof(WindowCommands));
    public static RoutedUICommand TogglePointing { get; } = new("Basculer le pointage", nameof(TogglePointing), typeof(WindowCommands));
    public static RoutedUICommand MarkTransactions { get; } = new("Pointer", nameof(MarkTransactions), typeof(WindowCommands));
    public static RoutedUICommand UnmarkTransactions { get; } = new("Dépointer", nameof(UnmarkTransactions), typeof(WindowCommands));
    public static RoutedUICommand MarkingDate { get; } = new("Date de pointage", nameof(MarkingDate), typeof(WindowCommands));
    public static RoutedUICommand ShowMarkingDates { get; } = new("Afficher les dates de pointage", nameof(ShowMarkingDates), typeof(WindowCommands));
    public static RoutedUICommand Reconcile { get; } = new("Rapprochement bancaire", nameof(Reconcile), typeof(WindowCommands));
    public static RoutedUICommand ReleaseLegacyReconciliation { get; } = new("Réexaminer un état rapproché hérité", nameof(ReleaseLegacyReconciliation), typeof(WindowCommands));
    public static RoutedUICommand RefreshRates { get; } = new("Actualiser les cours", nameof(RefreshRates), typeof(WindowCommands));
    public static RoutedUICommand ShowRates { get; } = new("Cours de change", nameof(ShowRates), typeof(WindowCommands));
}
