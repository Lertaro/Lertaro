namespace Lertaro.App.Services;

/// <summary>
/// Decides when it is actually worth emptying the process's working set after the quick window hides.
/// </summary>
/// <remarks>
/// Trimming used to happen 100ms after every hide. It does not free a single byte of committed memory --
/// SetProcessWorkingSetSize(-1,-1) only evicts pages from the working set, and every one of them has to
/// come back the next time the window is summoned. Measured over 21 summons: each one faulted a median
/// of 4,366 pages (about 17MB) back in, and the Show phase where that lands was 70% of the total summon
/// time. That is the price paid for a smaller number in Task Manager, on the interactive path, every
/// single time.
///
/// When the machine is under memory pressure the evicted pages go to the pagefile instead of the standby
/// list, and coming back means real disk reads. An index rebuild supplies exactly that pressure while
/// also saturating the disk with a raw $MFT scan, and the one summon captured in that state took 4.7
/// seconds and never painted at all, faulting 112,876 pages -- twenty-six times a healthy summon.
///
/// So the trim is kept, because giving memory back when the window is genuinely done with is worth
/// something, but it waits for the process to actually be idle. A burst of show/hide (the log shows them
/// 300ms apart) now pays nothing at all. Same shape as the service's own IdleTrimGate, and separated
/// from the timer and the P/Invoke for the same reason: so the decision can be tested directly.
/// </remarks>
internal sealed class IdleWorkingSetTrimGate
{
    private readonly long _idleMs;
    private readonly object _gate = new();

    private long _idleStartTicks;
    private int _backgroundSearches;
    private bool _windowShowing;
    private bool _armed;

    public IdleWorkingSetTrimGate(long idleMs) => _idleMs = idleMs;

    /// <summary>The window went away, so a trim becomes worth considering once things stay quiet.</summary>
    public void WindowHidden(long nowTicks)
    {
        lock (_gate)
        {
            _windowShowing = false;
            ArmIfIdle(nowTicks);
        }
    }

    /// <summary>
    /// The window is being summoned again. Cancels any pending trim outright -- trimming just before a
    /// summon is strictly worse than never trimming, since the pages are about to be needed.
    /// </summary>
    public void WindowShowing()
    {
        lock (_gate)
        {
            _windowShowing = true;
            _armed = false;
        }
    }

    /// <summary>Prevents an App-pipe CLI search from being trimmed while it is still using memory.</summary>
    public void BackgroundSearchStarted()
    {
        lock (_gate)
        {
            _backgroundSearches++;
            _armed = false;
        }
    }

    /// <summary>Arms a trim after the final background CLI search completes while no quick window is shown.</summary>
    public void BackgroundSearchFinished(long nowTicks)
    {
        lock (_gate)
        {
            if (_backgroundSearches > 0)
                _backgroundSearches--;
            ArmIfIdle(nowTicks);
        }
    }

    /// <summary>
    /// Whether to trim now. True at most once per hide, and never until the window has stayed hidden for
    /// the whole idle window.
    /// </summary>
    public bool ShouldTrim(long nowTicks)
    {
        lock (_gate)
        {
            if (!_armed)
                return false;
            if (_backgroundSearches > 0 || nowTicks - _idleStartTicks < _idleMs)
                return false;
            _armed = false;
            return true;
        }
    }

    private void ArmIfIdle(long nowTicks)
    {
        if (_backgroundSearches != 0 || _windowShowing)
            return;

        _idleStartTicks = nowTicks;
        _armed = true;
    }
}
