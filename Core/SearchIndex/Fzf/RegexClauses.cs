using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Lertaro.Core.SearchIndex.Fzf;

// Compiles and caches the "regex:/.../" clauses a query carried, and evaluates them against a candidate
// name.
//
// Compiled once per distinct pattern string rather than per candidate: a regex search runs its clauses
// against every surviving name, and recompiling would dominate the cost. The cache is keyed on the raw
// pattern text and bounded by MaxCacheEntries, so a long editing session cannot grow it without limit.
internal static class RegexClauses
{
    // NonBacktracking keeps a pathological pattern (nested quantifiers the user typed by accident) from
    // taking exponential time -- it is a hard guarantee, at the cost of backreferences and lookaround,
    // neither of which a file-name regex needs. Not all patterns are supported by that mode, so the
    // fallback below retries without it.
    private static readonly RegexOptions BaseOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // A user types a handful of distinct regexes, but every intermediate state of an edit is a distinct
    // pattern string, so an editing session is unbounded input. A compiled Regex is expensive enough to
    // hold that it must not accumulate forever; the cap is far above any real query's clause count, so
    // clearing it costs a recompile at most once per MaxCacheEntries new patterns.
    private const int MaxCacheEntries = 256;
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheGate = new();

    // A pattern that exceeds its match budget is a per-candidate miss, not a per-candidate log line.
    private static readonly RegexTimeoutLogThrottle TimeoutLog = new(60_000);

    // Test seam: the bound is only observable from the outside through the count.
    internal static int CachedCount => Cache.Count;

    internal static bool AllMatch(string[] patterns, ReadOnlySpan<char> text)
    {
        foreach (var pattern in patterns)
        {
            var regex = GetOrCreate(pattern);
            try
            {
                if (!regex.IsMatch(text))
                    return false;
            }
            catch (RegexMatchTimeoutException)
            {
                // Only the backtracking fallback below carries a budget -- NonBacktracking cannot time
                // out at all -- so this is a user's lookaround/backreference pattern losing a race
                // against one name. Rejecting that candidate is the whole cost; letting the exception
                // out would abandon the entire search for every remaining name.
                if (TimeoutLog.ShouldLog(pattern, Environment.TickCount64))
                    Logger.Log($"[Search] Regex clause '{pattern}' exceeded its match budget; that candidate was skipped.", LogLevel.Warn);

                return false;
            }
        }

        return true;
    }

    // The fast path is a plain read, so the common case (a pattern already compiled) never takes the
    // lock. The cap is enforced inside it, where the count cannot change underneath the check.
    private static Regex GetOrCreate(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached))
            return cached;

        lock (CacheGate)
        {
            if (Cache.TryGetValue(pattern, out cached))
                return cached;

            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();

            var compiled = Compile(pattern);
            Cache[pattern] = compiled;
            return compiled;
        }
    }

    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, BaseOptions | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // Lookaround/backreferences are legal in a user's regex but not under NonBacktracking.
            return new Regex(pattern, BaseOptions, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            // An invalid pattern matches nothing rather than failing the whole search.
            return new Regex("(?!)", BaseOptions);
        }
    }
}
