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

    internal static bool AllMatch(string[] patterns, ReadOnlySpan<char> text)
    {
        foreach (var pattern in patterns)
        {
            var regex = GetOrCreate(pattern);
            if (!regex.IsMatch(text))
                return false;
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
