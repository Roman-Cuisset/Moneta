using System.Windows.Input;

namespace Moneta.App;

public static class WindowCommands
{
    public static RoutedUICommand About { get; } = new("À propos de Moneta", nameof(About), typeof(WindowCommands));
    public static RoutedUICommand Settings { get; } = new("Paramètres", nameof(Settings), typeof(WindowCommands));
    public static RoutedUICommand AddDemoAccount { get; } = new("Ajouter un compte de démonstration", nameof(AddDemoAccount), typeof(WindowCommands));
    public static RoutedUICommand Theme { get; } = new("Thème", nameof(Theme), typeof(WindowCommands));
    public static RoutedUICommand Open { get; } = new("Ouvrir", nameof(Open), typeof(WindowCommands), [new KeyGesture(Key.O, ModifierKeys.Control)]);
    public static RoutedUICommand Save { get; } = new("Enregistrer", nameof(Save), typeof(WindowCommands), [new KeyGesture(Key.S, ModifierKeys.Control)]);
    // No Ctrl+Z/Y window bindings: native text editing retains its own undo/redo.
    public static RoutedUICommand FinancialUndo { get; } = new("Annuler une écriture", nameof(FinancialUndo), typeof(WindowCommands));
    public static RoutedUICommand FinancialRedo { get; } = new("Refaire une écriture", nameof(FinancialRedo), typeof(WindowCommands));
    public static RoutedUICommand Search { get; } = new("Rechercher", nameof(Search), typeof(WindowCommands), [new KeyGesture(Key.F, ModifierKeys.Control)]);
    public static RoutedUICommand DeleteTransaction { get; } = new("Supprimer une opération", nameof(DeleteTransaction), typeof(WindowCommands), [new KeyGesture(Key.Delete), new KeyGesture(Key.Delete, ModifierKeys.Control)]);
    public static RoutedUICommand RefreshRates { get; } = new("Actualiser les cours", nameof(RefreshRates), typeof(WindowCommands));
}
