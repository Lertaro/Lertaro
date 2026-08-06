namespace Lertaro.Core;

/// <summary>
/// Decides when the process has actually been idle long enough to be worth reclaiming memory.
/// </summary>
/// <remarks>
/// Separated from <see cref="SearchEngine"/> because getting it wrong is invisible: nothing errors, the
/// process simply keeps its peak forever, and it took three attempts at the symptom before the cause
/// turned out to be here.
///
/// The two things it exists to get right, both of which were wrong when this was a pair of fields:
///
/// A search that is still running is not idle, however long ago it started. Activity used to be stamped
/// only when a search BEGAN, and a search blocks until it finishes, so a ten-second query looked idle
/// three seconds in. The trimmer fired into the middle of it, tried to collect what that search was
/// actively using, got nothing, and consumed the one-shot arming on the way past -- so once the search
/// really did finish there was nothing left to ask again, and the process sat on its peak. A short query
/// finished inside those three seconds and trimmed correctly, which is why this presented as "short
/// searches give the memory back and long ones never do".
///
/// And the trim is one-shot per burst of activity: there is no point stopping the world again when
/// nothing has happened since the last time.
/// </remarks>
internal sealed class IdleTrimGate
{
    private readonly long _idleMs;
    private readonly object _armLock = new();

    private long _lastActivityTicks;
    private long _inFlight;
    private bool _armed;

    public IdleTrimGate(long idleMs, long nowTicks)
    {
        _idleMs = idleMs;
        _lastActivityTicks = nowTicks;
    }

    /// <summary>Something happened worth eventually reclaiming after.</summary>
    public void RecordActivity(long nowTicks)
    {
        Interlocked.Exchange(ref _lastActivityTicks, nowTicks);
        lock (_armLock)
            _armed = true;
    }

    public void SearchStarted(long nowTicks)
    {
        Interlocked.Increment(ref _inFlight);
        RecordActivity(nowTicks);
    }

    /// <summary>
    /// Records the search as finished. The activity stamp is refreshed here too, so the idle window is
    /// measured from when the search actually ended rather than from when it began.
    /// </summary>
    public void SearchFinished(long nowTicks)
    {
        Interlocked.Decrement(ref _inFlight);
        RecordActivity(nowTicks);
    }

    /// <summary>
    /// Whether to reclaim now. Returns true at most once per burst of activity, and never while a search
    /// is in flight.
    /// </summary>
    public bool ShouldTrim(long nowTicks)
    {
        if (Interlocked.Read(ref _inFlight) > 0)
            return false;

        if (nowTicks - Interlocked.Read(ref _lastActivityTicks) <= _idleMs)
            return false;

        lock (_armLock)
        {
            if (!_armed)
                return false;
            _armed = false;
            return true;
        }
    }
}
