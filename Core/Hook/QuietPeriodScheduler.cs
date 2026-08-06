namespace Lertaro.Core.Hook;

/// <summary>
/// Runs an action either at once or once a burst of requests has stopped arriving, and never lets two runs
/// overlap.
/// </summary>
/// <remarks>
/// For work that a high-frequency signal asks for far more often than it is worth doing. A window being
/// dragged or resized emits EVENT_OBJECT_LOCATIONCHANGE continuously -- measured at roughly 200 a second
/// while resizing Total Commander -- and the work each one asks for can end in a blocking call into that
/// same window. Ignoring the signal outright is not an option where it occasionally carries something real,
/// so the burst is collapsed into one run after it settles instead.
/// </remarks>
internal sealed class QuietPeriodScheduler : IDisposable
{
    private readonly Action _run;
    private readonly int _quietMs;
    private readonly Timer _timer;
    private readonly Lock _runLock = new();

    public QuietPeriodScheduler(Action run, int quietMs)
    {
        _run = run;
        _quietMs = quietMs;
        _timer = new Timer(_ => Execute(rearmIfBusy: true), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Runs now, and drops any pending request -- this run answers whatever that one was going to ask.
    /// </summary>
    public void RunNow()
    {
        Cancel();
        Execute(rearmIfBusy: true);
    }

    /// <summary>
    /// Asks for a run once nothing has asked again for the quiet period. Each call restarts that period,
    /// so a continuous burst produces exactly one run, after it ends.
    /// </summary>
    public void RunWhenQuiet() => _timer.Change(_quietMs, Timeout.Infinite);

    public void Cancel() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void Execute(bool rearmIfBusy)
    {
        // Deferred runs arrive on a timer thread while immediate ones stay on the caller's, so the two can
        // now collide where the work used to be single-threaded. Skipping rather than waiting keeps the
        // caller -- a WinEvent callback, in the case this exists for -- from blocking behind a run that is
        // mid cross-process call. Nothing is lost by skipping: the retry below picks it up once the run in
        // progress is done.
        if (!_runLock.TryEnter())
        {
            if (rearmIfBusy)
                RunWhenQuiet();
            return;
        }

        try
        {
            _run();
        }
        finally
        {
            _runLock.Exit();
        }
    }

    public void Dispose() => _timer.Dispose();
}
