namespace Lertaro.Core;

/// <summary>
/// Allows one trim after each burst of activity has finished and the idle delay has elapsed. Running
/// searches remain active regardless of their start time; consuming the trim request mid-search would
/// leave no trim scheduled when the query ends.
/// </summary>
internal sealed class IdleTrimGate
{
    private readonly long _idleMs;
    private readonly object _armLock = new();

    private long _lastActivityTicks;
    // Guarded by _armLock, not Interlocked: the in-flight check inside ShouldTrim must be atomic with
    // the arming decision, or a SearchStarted squeezing between them gets its trim granted anyway.
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
        Interlocked.Exchange(ref _lastActivityTicks, nowTicks);
        lock (_armLock)
        {
            _inFlight++;
            _armed = true;
        }
    }

    /// <summary>
    /// Records the search as finished. The activity stamp is refreshed here too, so the idle window is
    /// measured from when the search actually ended rather than from when it began.
    /// </summary>
    public void SearchFinished(long nowTicks)
    {
        Interlocked.Exchange(ref _lastActivityTicks, nowTicks);
        lock (_armLock)
        {
            _inFlight--;
            _armed = true;
        }
    }

    /// <summary>
    /// Whether a search is running right now, without touching the arming. A caller that deferred work
    /// behind a delay uses this to re-check when the delay ends, since <see cref="ShouldTrim"/> would
    /// either consume the one-shot arm or answer for the idle window rather than for "is it safe now".
    /// </summary>
    public bool HasSearchInFlight
    {
        get { lock (_armLock) return _inFlight > 0; }
    }

    /// <summary>
    /// Whether to reclaim now. Returns true at most once per burst of activity, and never while a search
    /// is in flight.
    /// </summary>
    public bool ShouldTrim(long nowTicks)
    {
        lock (_armLock)
        {
            // Same lock as SearchStarted/SearchFinished: the "never while a search is in flight"
            // promise only holds if the in-flight count is read atomically with the arming decision.
            if (_inFlight > 0)
                return false;

            if (nowTicks - Interlocked.Read(ref _lastActivityTicks) <= _idleMs)
                return false;

            if (!_armed)
                return false;
            _armed = false;
            return true;
        }
    }
}
