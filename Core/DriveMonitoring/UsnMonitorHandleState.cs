using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

// Owns the live volume handle separately so UsnMonitor can stay below the repository's per-file limit.
// The handle is deliberately disposable from the PnP removal callback while the monitor loop is reading.
internal sealed class UsnMonitorHandleState
{
    private readonly object _gate = new();
    private SafeFileHandle? _handle;
    private bool _disposed;

    public bool TrySet(SafeFileHandle handle)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                handle.Dispose();
                return false;
            }
            _handle = handle;
            return true;
        }
    }

    public void Clear(SafeFileHandle handle)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_handle, handle))
                _handle = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _handle?.Dispose();
            _handle = null;
        }
    }
}
