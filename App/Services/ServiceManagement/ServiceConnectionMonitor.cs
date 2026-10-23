using System.Windows.Threading;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services;

// Split out from ServiceConnectionHandler to keep that file under the repo's per-file line limit.
// This class owns the process-wide shared status timer and auto-install state; ServiceConnectionHandler
// instances subscribe/unsubscribe through the static members below.
internal static class ServiceConnectionMonitor
{
    // Steady-state poll interval: once the service is reachable and no detailed status is needed, the
    // shared timer backs off from the per-subscriber fast interval to this, so an idle-but-open window
    // isn't pinging the service several times a second forever (it only needs to notice a rare crash).
    internal const int SteadyPollIntervalMs = 2500;
    internal static readonly object GlobalMonitorLock = new();
    internal static readonly List<ServiceConnectionHandler> ActiveSubscribers = new();
    internal static DispatcherTimer? _sharedStatusTimer;
    internal static SearchService? _sharedSearchService;
    internal static int _isStatusCheckInFlight;
    internal static bool _globalAutoInstallingService;
    internal static bool _globalAutoInstallAttempted;
    internal static DateTime _globalReconnectUntilUtc = DateTime.MinValue;

    // One failed install used to be the last attempt for the whole process: at boot the service is frequently
    // still coming up, a single failed ping spent the budget, and every window then sat in Degraded Mode until
    // the App was restarted or the user started the service by hand. Retry on a growing cooldown instead, with
    // a cap so an install that is genuinely broken stops prompting for elevation forever.
    internal const int MaximumAutoInstallAttempts = 3;
    internal static int _globalAutoInstallAttempts;
    internal static DateTime _globalAutoInstallRetryAfterUtc = DateTime.MinValue;

    /// <summary>Whether an attempt may start now: budget left, and the cooldown between attempts run out.</summary>
    internal static bool MayAttemptAutoInstall(int attempts, DateTime nowUtc, DateTime retryAfterUtc) =>
        attempts < MaximumAutoInstallAttempts && nowUtc >= retryAfterUtc;

    /// <summary>
    /// Whether to keep reporting "reconnecting" instead of degrading: attempts remain but the cooldown has not
    /// run out. Without this the caller would give up in the middle of a backoff it just scheduled.
    /// </summary>
    internal static bool ShouldWaitForAutoInstallRetry(int attempts, DateTime nowUtc, DateTime retryAfterUtc) =>
        attempts > 0 && attempts < MaximumAutoInstallAttempts && nowUtc < retryAfterUtc;

    /// <summary>60 s after the first attempt, then 120, then 240: longer than a slow start, short enough to matter.</summary>
    internal static TimeSpan AutoInstallCooldown(int attempts) => TimeSpan.FromSeconds(30L << Math.Min(attempts, 4));

    internal static void EnsureSharedTimer_NoLock()
    {
        if (_sharedStatusTimer == null)
        {
            _sharedStatusTimer = new DispatcherTimer();
            _sharedStatusTimer.Tick += (_, _) => PollStatusTick();
        }

        if (_sharedSearchService == null && ActiveSubscribers.Count > 0)
            _sharedSearchService = ActiveSubscribers[0]._searchService;

        // Start (or re-arm) fast so a freshly shown window gets a prompt status check; the tick backs off
        // to the steady interval once the service is reachable and no detailed status is needed.
        _sharedStatusTimer.Interval = TimeSpan.FromMilliseconds(FastPollIntervalMs_NoLock());
        _sharedStatusTimer.Start();
    }

    internal static int FastPollIntervalMs_NoLock() => ActiveSubscribers.Count > 0 ? ActiveSubscribers.Min(s => s._pollIntervalMs) : 400;

    // Adaptive backoff: poll fast while a detailed status is needed (install / index build) or the service
    // is unreachable (so recovery is detected quickly); otherwise back off to the steady interval.
    internal static void ApplyPollInterval(bool reachable)
    {
        lock (GlobalMonitorLock)
        {
            if (_sharedStatusTimer == null) return;
            var detailed = ActiveSubscribers.Any(s => s._needsDetailedStatus);
            var target = (detailed || !reachable) ? FastPollIntervalMs_NoLock() : SteadyPollIntervalMs;
            if ((int)_sharedStatusTimer.Interval.TotalMilliseconds != target)
                _sharedStatusTimer.Interval = TimeSpan.FromMilliseconds(target);
        }
    }

    internal static void StopSharedTimer_NoLock()
    {
        _sharedStatusTimer?.Stop();
        _sharedSearchService = null;
    }

    internal static void PollStatusTick()
    {
        if (Interlocked.Exchange(ref _isStatusCheckInFlight, 1) == 1)
            return;

        var searchService = _sharedSearchService;
        if (searchService == null)
        {
            Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (RequiresDetailedStatus())
                {
                    var status = await searchService.GetStatusAsync().ConfigureAwait(false);
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            NotifySubscribers(subscriber => subscriber.ProcessStatus(status));
                            ApplyPollInterval(reachable: true);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                        }
                    }));
                    return;
                }

                var isReachable = await searchService.PingAsync().ConfigureAwait(false);
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        NotifySubscribers(subscriber => subscriber.ProcessPingResult(isReachable));
                        ApplyPollInterval(isReachable);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                    }
                }));
            }
            catch
            {
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        NotifySubscribers(subscriber => subscriber.ProcessPingResult(false));
                        ApplyPollInterval(reachable: false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                    }
                }));
            }
        });
    }

    internal static bool RequiresDetailedStatus() { lock (GlobalMonitorLock) return ActiveSubscribers.Any(s => s._needsDetailedStatus); }

    internal static void NotifySubscribers(Action<ServiceConnectionHandler> action)
    {
        ServiceConnectionHandler[] subs;
        lock (GlobalMonitorLock) subs = ActiveSubscribers.ToArray();
        foreach (var sub in subs) action(sub);
    }
}
