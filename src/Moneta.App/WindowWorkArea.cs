using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Moneta.App;

internal sealed class WindowWorkArea(Window window)
{
    private readonly double _minWidth = window.MinWidth;
    private readonly double _minHeight = window.MinHeight;
    private readonly double _maxWidth = window.MaxWidth;
    private readonly double _maxHeight = window.MaxHeight;
    private nint _monitor;

    internal void Track()
    {
        Update(VisualTreeHelper.GetDpi(window), force: true);
        window.DpiChanged += (_, change) => Update(change.NewDpi, force: true);
        window.LocationChanged += (_, _) => Update(VisualTreeHelper.GetDpi(window));
    }

    private void Update(DpiScale dpi, bool force = false)
    {
        const int monitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, monitorDefaultToNearest);
        if (monitor == nint.Zero || (!force && monitor == _monitor)) return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;
        _monitor = monitor;

        var width = Math.Floor((info.Work.Right - info.Work.Left) / dpi.DpiScaleX);
        var height = Math.Floor((info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
        window.SetCurrentValue(FrameworkElement.MinWidthProperty, Math.Min(_minWidth, width));
        window.SetCurrentValue(FrameworkElement.MinHeightProperty, Math.Min(_minHeight, height));
        window.SetCurrentValue(FrameworkElement.MaxWidthProperty, Math.Min(_maxWidth, width));
        window.SetCurrentValue(FrameworkElement.MaxHeightProperty, Math.Min(_maxHeight, height));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint handle, int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}
