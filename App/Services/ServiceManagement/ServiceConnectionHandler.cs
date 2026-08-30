using static Lertaro.App.Services.ServiceConnectionMonitor;
using Lertaro.Core;
using Lertaro.Core.Indexer.Usn;
using Application = System.Windows.Application;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services;

public class ServiceConnectionHandler : IDisposable
{
    private static readonly TimeSpan ServiceReconnectGracePeriod = TimeSpan.FromSeconds(15);

    internal readonly SearchService _searchService;
    private readonly Action<UsnIndexer.IndexerStatus> _onStatusUpdated;
    private readonly Action _onServiceInstallStarted;
    private readonly Action _onServiceInstallCompleted;
    private readonly Action<Exception> _onServiceInstallError;
    private readonly Action _onServiceFailedToStart;
    private readonly Action _onServiceReachable;
    internal readonly int _pollIntervalMs;
    private bool _isMonitoringActive;
    internal bool _needsDetailedStatus;
    private bool _reachableCallbackIssued;

    public bool IsAutoInstallingService => _globalAutoInstallingService;
    public bool HasAttemptedAutoInstall => _globalAutoInstallAttempted;

    public ServiceConnectionHandler(
        SearchService searchService,
        Action<UsnIndexer.IndexerStatus> onStatusUpdated,
        Action onServiceInstallStarted,
        Action onServiceInstallCompleted,
        Action<Exception> onServiceInstallError,
        Action onServiceFailedToStart,
        Action onServiceReachable,
        int pollIntervalMs = 400)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _onStatusUpdated = onStatusUpdated ?? throw new ArgumentNullException(nameof(onStatusUpdated));
        _onServiceInstallStarted = onServiceInstallStarted ?? throw new ArgumentNullException(nameof(onServiceInstallStarted));
        _onServiceInstallCompleted = onServiceInstallCompleted ?? throw new ArgumentNullException(nameof(onServiceInstallCompleted));
        _onServiceInstallError = onServiceInstallError ?? throw new ArgumentNullException(nameof(onServiceInstallError));
        _onServiceFailedToStart = onServiceFailedToStart ?? throw new ArgumentNullException(nameof(onServiceFailedToStart));
        _onServiceReachable = onServiceReachable ?? throw new ArgumentNullException(nameof(onServiceReachable));
        _pollIntervalMs = pollIntervalMs;
    }

    public void Start(bool requireDetailedStatus = false)
    {
        lock (GlobalMonitorLock)
        {
            if (_isMonitoringActive)
            {
                _needsDetailedStatus |= requireDetailedStatus;
                EnsureSharedTimer_NoLock();
                return;
            }

            _isMonitoringActive = true;
            _needsDetailedStatus = requireDetailedStatus;
            _reachableCallbackIssued = false;
            if (!ActiveSubscribers.Contains(this))
                ActiveSubscribers.Add(this);

            EnsureSharedTimer_NoLock();
        }
    }

    public void Stop()
    {
        lock (GlobalMonitorLock)
        {
            if (!_isMonitoringActive)
                return;

            _isMonitoringActive = false;
            _needsDetailedStatus = false;
            _reachableCallbackIssued = false;
            ActiveSubscribers.Remove(this);
            if (ActiveSubscribers.Count == 0)
                StopSharedTimer_NoLock();
        }
    }

    public void BeginServiceReconnectGracePeriod() => _globalReconnectUntilUtc = DateTime.UtcNow.Add(ServiceReconnectGracePeriod);

    public bool ShouldWaitForServiceReconnect() => _globalAutoInstallingService || DateTime.UtcNow < _globalReconnectUntilUtc;

    public void ClearServiceReconnectState()
    {
        _globalAutoInstallAttempted = false;
        _globalAutoInstallingService = false;
        _globalReconnectUntilUtc = DateTime.MinValue;
    }

    public void ResetAutoInstallFlag() => _globalAutoInstallAttempted = false;

    public void AttemptSilentInstall()
    {
        if (_globalAutoInstallingService)
            return;

        // Fast path: if the service is already installed at the current exe path, start it without
        // elevation instead of prompting for a reinstall.
        if (ServiceInstallManager.TryStartExistingService())
        {
            BeginServiceReconnectGracePeriod();
            NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
            return;
        }

        _globalAutoInstallingService = true;
        BeginServiceReconnectGracePeriod();
        NotifySubscribers(subscriber => subscriber._onServiceInstallStarted());

        // Run the installer off the UI thread: RunElevatedInstaller waits for the elevated process to
        // exit (up to 30s), so calling it synchronously from a Dispatcher poll tick would freeze the UI.
        _ = Task.Run(() =>
        {
            var started = ServiceInstallManager.SilentInstall(() =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    _globalAutoInstallingService = false;
                    BeginServiceReconnectGracePeriod();
                    NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
                }));
            });

            if (!started)
            {
                Logger.Log("[ServiceConnectionHandler] Silent service install already running; waiting for reconnect.", LogLevel.Debug);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    return;
                dispatcher.BeginInvoke(new Action(BeginServiceReconnectGracePeriod));
            }
        });
    }

    public void ExecuteInstallService() =>
        // Manual installs are triggered from UI commands; keep RunElevatedInstaller's wait off the UI thread.
        _ = Task.Run(() => ServiceInstallManager.InstallService(
            onCompleted: () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _globalAutoInstallAttempted = true;
                _globalAutoInstallingService = false;
                BeginServiceReconnectGracePeriod();
                NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
            })),
            onError: ex => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                NotifySubscribers(subscriber => subscriber._onServiceInstallError(ex))))
        ));

    public void Dispose() => Stop();

    internal void ProcessStatus(UsnIndexer.IndexerStatus status)
    {
        if (status.State == "error")
        {
            if (ShouldWaitForServiceReconnect())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (!_globalAutoInstallAttempted)
            {
                _globalAutoInstallAttempted = true;
                AttemptSilentInstall();
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            Stop();
            _onServiceFailedToStart();
            return;
        }

        _onStatusUpdated(status);
    }

    internal void ProcessPingResult(bool isReachable)
    {
        if (!isReachable)
        {
            if (ShouldWaitForServiceReconnect())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (!_globalAutoInstallAttempted)
            {
                _globalAutoInstallAttempted = true;
                AttemptSilentInstall();
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            Stop();
            _onServiceFailedToStart();
            return;
        }

        if (_needsDetailedStatus || _reachableCallbackIssued)
            return;

        _reachableCallbackIssued = true;
        _onServiceReachable();
    }
}
