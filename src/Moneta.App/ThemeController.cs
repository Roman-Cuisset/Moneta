using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Moneta.App;

/// <summary>Switches presentation resources without recreating windows or their view models.</summary>
public sealed class ThemeController : IDisposable
{
    private static readonly ConditionalWeakTable<Window, WindowWorkArea> WorkAreas = new();
    private Application? _application;
    private ResourceDictionary? _lightPalette;
    private ResourceDictionary? _darkPalette;
    private ResourceDictionary? _highContrastPalette;
    private ResourceDictionary? _activePalette;
    private bool _disposed;

    static ThemeController()
    {
        // A static class handler holds no window references and also covers owned dialogs.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    /// <summary>The user's light/dark choice; Windows High Contrast takes precedence visually.</summary>
    public string CurrentMode { get; private set; } = "Light";

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_application is not null)
        {
            return;
        }

        _application = Application.Current
            ?? throw new InvalidOperationException("Theme initialization requires a WPF application.");
        _application.Dispatcher.VerifyAccess();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Refresh();
    }

    /// <summary>Applies presentation only. Use <see cref="App.ChangeTheme"/> for a persisted user choice.</summary>
    public void Apply(string mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mode is not ("Light" or "Dark"))
        {
            throw new ArgumentException("The theme must be Light or Dark.", nameof(mode));
        }

        Initialize();
        _application!.Dispatcher.VerifyAccess();
        CurrentMode = mode;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _disposed = true;
        _application = null;
    }

    private static ResourceDictionary LoadPalette(string name) => new()
    {
        Source = new Uri($"/Moneta.App;component/Themes/{name}.xaml", UriKind.Relative)
    };

    private void Refresh()
    {
        if (_application is null || _disposed)
        {
            return;
        }

        var palette = SystemParameters.HighContrast
            ? _highContrastPalette ??= LoadPalette("HighContrast")
            : CurrentMode == "Dark"
                ? _darkPalette ??= LoadPalette("Dark")
                : _lightPalette ??= LoadPalette("Light");

        if (!ReferenceEquals(palette, _activePalette))
        {
            var dictionaries = _application.Resources.MergedDictionaries;
            if (_activePalette is null)
            {
                // Controls are merged separately; keep palette replacement isolated.
                dictionaries.Insert(0, palette);
            }
            else
            {
                dictionaries[dictionaries.IndexOf(_activePalette)] = palette;
            }

            _activePalette = palette;
        }

        foreach (Window window in _application.Windows)
        {
            RefreshCaption(window);
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.HighContrast) or null or ""))
        {
            return;
        }

        var dispatcher = _application?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Refresh));
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && Application.Current is App)
        {
            _ = WorkAreas.GetValue(window, static view =>
            {
                var workArea = new WindowWorkArea(view);
                workArea.Track();
                return workArea;
            });
            App.ThemeController.RefreshCaption(window);
        }
    }

    private void RefreshCaption(Window window)
    {
        if (_disposed)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        // Let Windows supply its own accessible non-client colors in High Contrast.
        var dark = !SystemParameters.HighContrast && CurrentMode == "Dark" ? 1 : 0;
        // Unsupported Windows versions keep their native caption; client theming is unaffected.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
