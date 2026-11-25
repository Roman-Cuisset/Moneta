namespace Moneta.Core.Presentation;

/// <summary>A physical-pixel rectangle used only for window placement.</summary>
public readonly record struct PixelRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height);
    public bool IsUsable => IsFinite && Width > 0 && Height > 0;
}

/// <summary>
/// Fits a window rectangle into a monitor work area without changing a valid
/// user position. Coordinates are physical pixels, as returned by Win32.
/// </summary>
public static class WindowPlacement
{
    public static PixelRect Fit(
        PixelRect desired,
        PixelRect work,
        double minWidthDip,
        double minHeightDip,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (!work.IsUsable)
        {
            return desired;
        }

        var scaleX = double.IsFinite(dpiScaleX) && dpiScaleX > 0 ? dpiScaleX : 1.0;
        var scaleY = double.IsFinite(dpiScaleY) && dpiScaleY > 0 ? dpiScaleY : 1.0;
        var minWidth = Math.Min(Math.Max(0, minWidthDip) * scaleX, work.Width);
        var minHeight = Math.Min(Math.Max(0, minHeightDip) * scaleY, work.Height);

        var width = desired.Width;
        var height = desired.Height;
        if (!double.IsFinite(width) || width <= 0) width = minWidth;
        if (!double.IsFinite(height) || height <= 0) height = minHeight;
        width = Math.Clamp(width, minWidth, work.Width);
        height = Math.Clamp(height, minHeight, work.Height);

        var left = double.IsFinite(desired.Left) ? desired.Left : work.Left;
        var top = double.IsFinite(desired.Top) ? desired.Top : work.Top;

        // A fully visible rectangle keeps its exact user position and size.
        if (left >= work.Left && top >= work.Top &&
            left + width <= work.Right && top + height <= work.Bottom)
        {
            return new PixelRect(left, top, width, height);
        }

        left = Math.Clamp(left, work.Left, work.Right - width);
        top = Math.Clamp(top, work.Top, work.Bottom - height);
        return new PixelRect(left, top, width, height);
    }

    public static bool IsUsable(double left, double top, double width, double height) =>
        double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width)
        && double.IsFinite(height) && width > 0 && height > 0;
}
