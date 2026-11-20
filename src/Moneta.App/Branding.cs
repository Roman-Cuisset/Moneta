using System.Windows.Media.Imaging;

namespace Moneta.App;

internal static class Branding
{
    // Keep the ICO decoder attached so WPF can pick a frame for each native icon size.
    public static BitmapFrame WindowIcon { get; } = BitmapFrame.Create(
        new Uri("pack://application:,,,/Moneta.App;component/Branding/moneta.ico", UriKind.Absolute));
}
