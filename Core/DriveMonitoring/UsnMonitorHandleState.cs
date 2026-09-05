using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

// Owns the live volume handle separately so UsnMonitor can stay below the repository's per-file limit.
// The handle is deliberately disposable from the PnP removal callback while the monitor loop is reading.
internal sealed class UsnMonitorHandleState
{
    private readonly object _gate = new();
    private SafeFileHandle? _handle;

    public void Set(SafeFileHandle handle)
    {
        lock (_gate)
            _handle = handle;
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
            _handle?.Dispose();
            _handle = null;
        }
    }
}
