using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Moneta.Updates;

namespace Moneta.App;

// A second launch hands off a path. Only the primary window can replace its workspace.
internal sealed class FileActivation : IDisposable
{
    private const int MaximumBytes = 32768 * 4;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Mutex _instance;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;

    internal FileActivation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var name = $"{UpdateChannel.RunningMutex}.Shell.{identity.User!.Value}";
        _instance = new Mutex(false, name, out var primary);
        _pipeName = $"{name[6..]}.{process.SessionId}";
        IsPrimary = primary;
    }

    internal bool IsPrimary { get; }

    internal static string NormalizePath(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".moneta", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.Get("FileActivation.InvalidPath"));
        try { return Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            throw new ArgumentException(L.Get("FileActivation.InvalidPath"), error);
        }
    }

    internal void Listen(Func<string, Task<bool>> queue) => _listener = ServeAsync(queue, _lifetime.Token);

    internal async Task<bool> ForwardAsync(string path)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token);
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId)) _ = AllowSetForegroundWindow(processId);
            var bytes = Utf8.GetBytes(path);
            if (bytes.Length > MaximumBytes) return false;
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await pipe.WriteAsync(header, deadline.Token);
            await pipe.WriteAsync(bytes, deadline.Token);
            var response = new byte[1];
            await pipe.ReadExactlyAsync(response, deadline.Token);
            return response[0] == 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or EncoderFallbackException)
        {
            return false;
        }
    }

    private async Task ServeAsync(Func<string, Task<bool>> queue, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var header = new byte[sizeof(int)];
                await pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 0 or > MaximumBytes) continue;
                var bytes = new byte[length];
                await pipe.ReadExactlyAsync(bytes, deadline.Token).ConfigureAwait(false);
                var accepted = await queue(Utf8.GetString(bytes)).WaitAsync(deadline.Token).ConfigureAwait(false);
                await pipe.WriteAsync(new[] { accepted ? (byte)1 : (byte)0 }, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                // A disconnected or malformed activation cannot end the listener.
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        if (_listener is null) _lifetime.Dispose();
        else _ = _listener.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        _instance.Dispose();
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
