using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Lertaro.Core.SearchIndex.Fzf;

// Compiles and caches the "regex:/.../" clauses a query carried, and evaluates them against a candidate
// name.
//
// Compiled once per distinct pattern string rather than per candidate: a regex search runs its clauses
// against every surviving name, and recompiling would dominate the cost. The cache is keyed on the raw
// pattern text, which is bounded by how many distinct regexes a user actually types.
internal static class RegexClauses
{
    // NonBacktracking keeps a pathological pattern (nested quantifiers the user typed by accident) from
    // taking exponential time -- it is a hard guarantee, at the cost of backreferences and lookaround,
    // neither of which a file-name regex needs. Not all patterns are supported by that mode, so the
    // fallback below retries without it.
    private static readonly RegexOptions BaseOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    // A pattern that exceeds its match budget is a per-candidate miss, not a per-candidate log line.
    private static readonly RegexTimeoutLogThrottle TimeoutLog = new(60_000);

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

    private static Regex GetOrCreate(string pattern) => Cache.GetOrAdd(pattern, Compile);

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
