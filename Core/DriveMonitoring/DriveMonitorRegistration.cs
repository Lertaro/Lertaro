namespace Lertaro.Core.DriveMonitoring;

internal sealed class DriveMonitorRegistration : IDisposable
{
    private readonly IDisposable _monitor;
    private IDisposable? _removalMonitor;
    private readonly object _gate = new();
    private TaskCompletionSource? _monitorStopped;
    private bool _callbacksInvalidated;
    private bool _disposed;

    public DriveMonitorRegistration(IDisposable monitor, IDisposable? removalMonitor)
    {
        _monitor = monitor;
        _removalMonitor = removalMonitor;
    }

    public bool ReplaceRemovalMonitor(IDisposable removalMonitor)
    {
        IDisposable? previous;
        bool accepted;
        lock (_gate)
        {
            accepted = !_disposed && !_callbacksInvalidated && _monitorStopped == null;
            previous = accepted ? _removalMonitor : removalMonitor;
            (previous as DriveDeviceRemovalMonitor)?.Invalidate();
            if (accepted) _removalMonitor = removalMonitor;
        }
        previous?.Dispose();
        return accepted;
    }

    // Lock order: indexer -> registration -> notification. Only short mutations/queueing belong here;
    // stopping the USN loop must happen outside all three locks (it can replace its notification).
    internal bool TryInvokeRemovalCallback(DriveDeviceRemovalMonitor removal, Action action)
    {
        lock (_gate)
            return !_callbacksInvalidated && ReferenceEquals(_removalMonitor, removal) && removal.TryInvoke(action);
    }

    internal void InvalidateCallbacks()
    {
        lock (_gate)
        {
            _callbacksInvalidated = true;
            (_removalMonitor as DriveDeviceRemovalMonitor)?.Invalidate();
        }
    }

    public void DisposeMonitor()
    {
        TaskCompletionSource stopped;
        bool stop;
        lock (_gate)
        {
            stop = _monitorStopped == null;
            stopped = _monitorStopped ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (stop)
        {
            try
            {
                _monitor.Dispose();
                stopped.SetResult();
            }
            catch (Exception ex)
            {
                stopped.SetException(ex);
            }
        }
        // All callers, including repeated Dispose, observe the same completion and failure.
        stopped.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        IDisposable? removal;
        lock (_gate)
        {
            _disposed = true;
            InvalidateCallbacks();
            removal = _removalMonitor;
            _removalMonitor = null;
        }
        try { DisposeMonitor(); }
        finally { removal?.Dispose(); }
    }
}
