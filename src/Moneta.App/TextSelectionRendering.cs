using System.Runtime.CompilerServices;

namespace Moneta.App;

internal static class TextSelectionRendering
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // WPF's legacy adorner paints selection over the glyphs and ignores
        // SelectionTextBrush. Choose text-renderer selection before WPF caches
        // the switch, so opaque theme selections remain readable in every window.
        AppContext.SetSwitch(
            "Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering",
            false);
    }
}
