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
        ResetAutoInstallFlag();
        _globalAutoInstallingService = false;
        _globalReconnectUntilUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Spends the retry budget too, not just the flag: this is what a reachable service or an explicit user
    /// action calls, and a half-reset budget would leave the next failure counting against the old attempts.
    /// </summary>
    public void ResetAutoInstallFlag()
    {
        _globalAutoInstallAttempted = false;
        _globalAutoInstallAttempts = 0;
        _globalAutoInstallRetryAfterUtc = DateTime.MinValue;
    }

    private bool ShouldWaitForRetry() =>
        ShouldWaitForAutoInstallRetry(_globalAutoInstallAttempts, DateTime.UtcNow, _globalAutoInstallRetryAfterUtc);

    /// <summary>Whether this was the moment to try again, recording the attempt and its cooldown.</summary>
    private bool TryBeginAutoInstallAttempt()
    {
        if (!MayAttemptAutoInstall(_globalAutoInstallAttempts, DateTime.UtcNow, _globalAutoInstallRetryAfterUtc))
            return false;

        _globalAutoInstallAttempted = true;
        _globalAutoInstallAttempts++;
        _globalAutoInstallRetryAfterUtc = DateTime.UtcNow.Add(AutoInstallCooldown(_globalAutoInstallAttempts));
        return true;
    }

    public void AttemptSilentInstall()
    {
        if (_globalAutoInstallingService)
            return;

        // Claimed before the first wait rather than before the install: both steps below end in a process
        // exiting, and a poll tick arriving while the first is still running must not start a second one.
        _globalAutoInstallingService = true;
        BeginServiceReconnectGracePeriod();
        NotifySubscribers(subscriber => subscriber._onServiceInstallStarted());

        // Off the UI thread. This is called from a Dispatcher poll tick, and the non-elevated start waits on
        // the SCM's own 30-second verdict while the elevated installer waits up to 120 s, so a service that is
        // merely slow to come up would otherwise freeze every open window for the length of that wait.
        _ = Task.Run(() =>
        {
            void FinishAutoInstall(Action<ServiceConnectionHandler> notify)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    _globalAutoInstallingService = false;
                    BeginServiceReconnectGracePeriod();
                    NotifySubscribers(notify);
                }));
            }

            // Fast path: if the service is already installed at the current exe path, start it without
            // elevation instead of prompting for a reinstall.
            if (ServiceInstallManager.TryStartExistingService())
            {
                FinishAutoInstall(subscriber => subscriber._onServiceInstallCompleted());
                return;
            }

            var result = ServiceInstallManager.SilentInstall(
                onCompleted: () => FinishAutoInstall(subscriber => subscriber._onServiceInstallCompleted()),
                onFailed: _ => FinishAutoInstall(subscriber => subscriber._onServiceFailedToStart()));

            if (result != ServiceInstallManager.SilentInstallResult.AlreadyRunning)
                return;

            // Another install is in flight in this process, so its outcome will report the result. Leaving the
            // flag set here used to block every later attempt for the rest of the session.
            Logger.Log("[ServiceConnectionHandler] Silent service install already running; waiting for reconnect.", LogLevel.Debug);
            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(new Action(() =>
            {
                _globalAutoInstallingService = false;
                BeginServiceReconnectGracePeriod();
            }));
        });
    }

    public void ExecuteInstallService() =>
        // Manual installs are triggered from UI commands; keep RunElevatedInstaller's wait off the UI thread.
        _ = Task.Run(() => ServiceInstallManager.InstallService(
            onCompleted: () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // The user asked for this install, so it re-arms the automatic budget rather than consuming what
                // is left of it: an unreachable service afterwards should still be retried on the cooldown.
                ResetAutoInstallFlag();
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
            if (ShouldWaitForServiceReconnect() || ShouldWaitForRetry())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (TryBeginAutoInstallAttempt())
            {
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
            if (ShouldWaitForServiceReconnect() || ShouldWaitForRetry())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (TryBeginAutoInstallAttempt())
            {
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
