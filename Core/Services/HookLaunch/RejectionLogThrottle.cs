using System.Collections.Concurrent;

namespace Lertaro.Core.Services.HookLaunch;

// Split out of HookLaunchRequestHandler so the per-PID throttling decision is unit-testable
// without touching the file-backed Logger; this class holds no state of its own beyond the
// caller-PID -> last-logged-tick map handed to it. Returns the logging decision and records
// the PID's log time in one step.
internal sealed class RejectionLogThrottle
{
    private readonly ConcurrentDictionary<int, long> _lastLoggedTicks = new();
    private readonly long _relogIntervalMs;

    internal RejectionLogThrottle(long relogIntervalMs) => _relogIntervalMs = relogIntervalMs;

    /// <summary>
    /// Returns true when a rejection of this PID should be logged now: its first occurrence,
    /// or the re-log interval having elapsed since the last one (a PID reused by a different
    /// process must become visible again). Records the log time as a side effect.
    /// </summary>
    internal bool ShouldLog(int pid, long nowTickCount)
    {
        if (_lastLoggedTicks.TryGetValue(pid, out var last) &&
            nowTickCount - last < _relogIntervalMs)
            return false;

        _lastLoggedTicks[pid] = nowTickCount;
        return true;
    }
}
