namespace Lertaro.App.Services;

/// <summary>
/// Allows working-set trimming after the App becomes idle. Trimming evicts pages without reducing
/// committed memory, so the next summon must fault them back in. Measurements across 21 summons found a
/// median 4,366 faults (about 17 MB); under rebuild-related pressure one summon took 4.7 seconds with
/// 112,876 faults.
///
/// Wait for idle instead of trimming after every hide, so rapid show/hide cycles keep their pages. Keep
/// this decision separate from the timer and native call for testing.
/// </summary>
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

    /// <summary>Arms a trim request when no interactive or background work is using the process.</summary>
    public void RequestTrim(long nowTicks)
    {
        lock (_gate)
            ArmIfIdle(nowTicks);
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
