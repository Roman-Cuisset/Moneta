using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Moneta.Core.Presentation;

namespace Moneta.App;

/// <summary>
/// Keeps a window inside the work area of its current monitor. The window is
/// corrected before it is shown when possible, and only when its current
/// physical rectangle is not valid.
/// </summary>
internal sealed class WindowWorkArea
{
    private static readonly ConditionalWeakTable<Window, WindowWorkArea> Instances = new();
    private readonly Window _window;
    private readonly double _minWidth;
    private readonly double _minHeight;
    private readonly double _maxWidth;
    private readonly double _maxHeight;
    private nint _monitor;
    private bool _applying;

    private WindowWorkArea(Window window)
    {
        _window = window;
        _minWidth = window.MinWidth;
        _minHeight = window.MinHeight;
        _maxWidth = window.MaxWidth;
        _maxHeight = window.MaxHeight;
        window.SourceInitialized += OnSourceInitialized;
        window.Loaded += OnLoaded;
        window.DpiChanged += OnDpiChanged;
        window.LocationChanged += OnLocationChanged;
        window.StateChanged += OnStateChanged;
        window.Closed += OnClosed;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    internal static WindowWorkArea Attach(Window window) =>
        Instances.GetValue(window, static view => new WindowWorkArea(view));

    private void OnSourceInitialized(object? sender, EventArgs e) => Apply(force: true);

    private void OnLoaded(object sender, RoutedEventArgs e) => Apply(force: true);

    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => Apply(force: true);

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_applying) Apply(force: false);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_applying && _window.WindowState == WindowState.Normal) Apply(force: true);
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or null or "")
            Apply(force: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Loaded -= OnLoaded;
        _window.DpiChanged -= OnDpiChanged;
        _window.LocationChanged -= OnLocationChanged;
        _window.StateChanged -= OnStateChanged;
        _window.Closed -= OnClosed;
        Instances.Remove(_window);
    }

    private void Apply(bool force)
    {
        if (_applying) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == nint.Zero) return;

        const int monitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(handle, monitorDefaultToNearest);
        if (monitor == nint.Zero || (!force && monitor == _monitor)) return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;

        _applying = true;
        try
        {
            _monitor = monitor;
            var dpi = VisualTreeHelper.GetDpi(_window);
            var work = new PixelRect(
                info.Work.Left,
                info.Work.Top,
                info.Work.Right - info.Work.Left,
                info.Work.Bottom - info.Work.Top);
            if (!work.IsUsable) return;

            _window.SetCurrentValue(FrameworkElement.MinWidthProperty,
                Math.Min(_minWidth, work.Width / dpi.DpiScaleX));
            _window.SetCurrentValue(FrameworkElement.MinHeightProperty,
                Math.Min(_minHeight, work.Height / dpi.DpiScaleY));
            _window.SetCurrentValue(FrameworkElement.MaxWidthProperty,
                Math.Min(_maxWidth, work.Width / dpi.DpiScaleX));
            _window.SetCurrentValue(FrameworkElement.MaxHeightProperty,
                Math.Min(_maxHeight, work.Height / dpi.DpiScaleY));

            if (_window.WindowState is WindowState.Minimized or WindowState.Maximized) return;

            if (!GetWindowRect(handle, out var current)) return;
            var desired = new PixelRect(
                current.Left,
                current.Top,
                current.Right - current.Left,
                current.Bottom - current.Top);
            var fitted = WindowPlacement.Fit(desired, work, _minWidth, _minHeight,
                dpi.DpiScaleX, dpi.DpiScaleY);
            if (fitted == desired) return;

            const uint noZOrder = 0x0004;
            const uint noActivate = 0x0010;
            _ = SetWindowPos(handle, nint.Zero,
                (int)Math.Round(fitted.Left), (int)Math.Round(fitted.Top),
                (int)Math.Round(fitted.Width), (int)Math.Round(fitted.Height),
                noZOrder | noActivate);
        }
        finally
        {
            _applying = false;
        }
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

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
}
