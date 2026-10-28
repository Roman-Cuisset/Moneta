using System.Windows.Input;

namespace Moneta.App;

public static class WindowCommands
{
    public static RoutedUICommand About { get; } = new("À propos de Moneta", nameof(About), typeof(WindowCommands));
}
