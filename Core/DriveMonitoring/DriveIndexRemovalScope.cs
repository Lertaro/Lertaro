namespace Lertaro.Core.DriveMonitoring;

// Provides a cancellable per-drive scan token and keeps removal notification alive until Windows
// reports whether the pending removal completed or failed.
public sealed class DriveIndexRemovalScope : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _parentToken;
    private readonly string _drive;
    private readonly Action _onRemovalRequested;
    private readonly Action<string> _onRecoveryRequired;
    private readonly object _gate = new();
    private DriveDeviceRemovalMonitor? _monitor;
    private bool _removalStarted;
    private bool _resolutionReceived;
    private bool _disposeRequested;
    private int _disposed;

    private DriveIndexRemovalScope(string drive, CancellationToken parentToken, Action onRemovalRequested, Action<string> onRecoveryRequired)
    {
        _drive = drive;
        _parentToken = parentToken;
        _onRemovalRequested = onRemovalRequested;
        _onRecoveryRequired = onRecoveryRequired;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
    }

    public CancellationToken Token => _cts.Token;

    public static DriveIndexRemovalScope Register(string drive, CancellationToken parentToken, Action onRemovalRequested, Action<string> onRecoveryRequired)
    {
        var scope = new DriveIndexRemovalScope(drive, parentToken, onRemovalRequested, onRecoveryRequired);
        scope._monitor = DriveDeviceRemovalMonitor.Register(drive, scope.HandleRemovalRequested, scope.HandleRemovalFailed, scope.HandleRemovalCompleted);
        return scope;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposeRequested = true;
            if (_removalStarted && !_resolutionReceived)
                return;
        }
        DisposeCore();
    }

    private void HandleRemovalRequested()
    {
        lock (_gate)
            _removalStarted = true;
        _cts.Cancel();
        _onRemovalRequested();
    }

    private void HandleRemovalFailed()
    {
        _onRecoveryRequired(_drive);
        FinishRemovalResolution();
    }

    private void HandleRemovalCompleted()
    {
        DriveReattachWaiter.Start(_drive, _parentToken, () => _onRecoveryRequired(_drive));
        FinishRemovalResolution();
    }

    private void FinishRemovalResolution()
    {
        lock (_gate)
        {
            _resolutionReceived = true;
            if (!_disposeRequested)
                return;
        }
        DisposeCore();
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _monitor?.Dispose();
        _cts.Dispose();
    }
}
