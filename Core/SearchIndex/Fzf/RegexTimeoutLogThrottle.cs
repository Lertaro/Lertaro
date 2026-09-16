using System.Collections.Concurrent;

namespace Lertaro.Core.SearchIndex.Fzf;

// Split out of RegexClauses so the throttling decision is unit-testable without touching the file-backed
// Logger; this class holds no state beyond the pattern -> last-logged-tick map handed to it. Returns the
// logging decision and records the pattern's log time in one step.
//
// The need: only the backtracking fallback carries a match budget, and it is evaluated once per
// candidate. Without throttling, one pattern that blows its budget on a folder of a million names writes
// a million identical log lines, turning a single skipped candidate into a disk-filling storm.
internal sealed class RegexTimeoutLogThrottle
{
    // The map is keyed by pattern text, and every intermediate state of an edit is a distinct pattern, so a
    // session that keeps blowing the match budget would grow it without bound. Each entry is tiny, but this
    // is unbounded input, so it gets the same treatment as the compiled-regex cache it sits beside: cleared
    // wholesale at a cap. A timeout report never needs to outlive 256 later patterns.
    private const int MaxTrackedPatterns = 256;

    private readonly ConcurrentDictionary<string, long> _lastLoggedTicks = new(StringComparer.Ordinal);
    private readonly long _relogIntervalMs;

    internal RegexTimeoutLogThrottle(long relogIntervalMs) => _relogIntervalMs = relogIntervalMs;

    // Test seam: the bound is only observable from the outside through the count.
    internal int TrackedCount => _lastLoggedTicks.Count;

    // True for a pattern's first timeout, and again once the re-log interval has elapsed since the last
    // one. Records the log time as a side effect.
    internal bool ShouldLog(string pattern, long nowTickCount)
    {
        if (_lastLoggedTicks.TryGetValue(pattern, out var last) &&
            nowTickCount - last < _relogIntervalMs)
            return false;

        // Cleared before the insert, so this call's own decision (already made) is never lost -- only the
        // other patterns' re-log clocks are, which costs at most one extra line each.
        if (_lastLoggedTicks.Count >= MaxTrackedPatterns)
            _lastLoggedTicks.Clear();

        _lastLoggedTicks[pattern] = nowTickCount;
        return true;
    }
}
