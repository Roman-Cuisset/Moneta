using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Moneta.App;

public partial class MainWindow
{
    private readonly Queue<string> _openRequests = new();
    private bool _processingOpenRequests;
    private bool _windowClosed;

    internal bool QueueActivation(string path)
    {
        if (_windowClosed || _allowClose || _stopBackupRequests) return false;
        if (!_openRequests.Contains(path, StringComparer.OrdinalIgnoreCase)) _openRequests.Enqueue(path);
        if (!_processingOpenRequests) _ = ProcessActivationsAsync();
        return true;
    }

    private async Task ProcessActivationsAsync()
    {
        _processingOpenRequests = true;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();
            while (!_windowClosed && _openRequests.Count > 0)
            {
                if (_busy || _closingPrompt || !IsEnabled || !IsWindowEnabled(new WindowInteropHelper(this).Handle))
                {
                    await Task.Delay(150);
                    continue;
                }

                var requested = _openRequests.Dequeue();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                if (requested.Length == 0) continue;
                string path;
                try { path = FileActivation.NormalizePath(requested); }
                catch (ArgumentException error)
                {
                    MessageBox.Show(this, error.Message, L.Get("FileActivation.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                await OpenMonetaAsync(path);
            }
        }
        finally { _processingOpenRequests = false; }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint handle);
}
