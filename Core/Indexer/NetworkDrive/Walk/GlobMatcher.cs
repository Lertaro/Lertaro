using System.Text.RegularExpressions;

namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal static class GlobMatcher
{
    public static NetworkGlobPattern Compile(string pattern) => new(pattern);
}

internal sealed class NetworkGlobPattern
{
    // A literal shorter than this isn't selective enough to be worth testing before the regex.
    private const int MinimumUsefulLiteral = 3;

    private readonly string _rawPattern;
    private readonly Regex? _regex;
    private readonly string? _requiredLiteral;

    // Set once this pattern has proved it can run away, after which it is not run again. See IsMatch.
    private volatile bool _abandoned;

    public NetworkGlobPattern(string pattern) : this(pattern, null) { }

    // The timeout override exists so a test can force the runaway path deterministically instead of
    // having to actually spend the real budget on a pattern engineered to burn it.
    internal NetworkGlobPattern(string pattern, TimeSpan? matchTimeout)
    {
        _rawPattern = pattern ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_rawPattern))
        {
            var trimmed = _rawPattern.Trim();
            IsSegmentOnly = !trimmed.Replace('\\', '/').Contains('/');
            try
            {
                _regex = GlobToRegex.Compile(trimmed, matchTimeout: matchTimeout);
                _requiredLiteral = LongestRequiredLiteral(trimmed);
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkGlobPattern] Failed to compile glob '{_rawPattern}' to regex: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    /// <summary>The pattern as configured, before compilation. See ExclusionRuleSet.</summary>
    internal string RawPattern => _rawPattern;

    /// <summary>
    /// Whether the pattern names a single path segment (it contains no separator). Such a pattern compiles
    /// to "a segment boundary, the segment, end of text", so against a full path it can only ever describe
    /// the LAST segment -- the name -- and the relative and separator-suffixed spellings the callers also
    /// try can never turn that into a match. Callers use this to run one match instead of four.
    /// </summary>
    public bool IsSegmentOnly { get; }

    /// <summary>Whether this pattern has been abandoned for exceeding the match timeout.</summary>
    internal bool IsAbandoned => _abandoned;

    public bool IsEmpty => string.IsNullOrWhiteSpace(_rawPattern);

    public bool IsMatch(string text) => IsMatch(text.AsSpan());

    /// <summary>
    /// Span overload: the per-row callers hold a path or a name as a slice and would otherwise
    /// materialize a string for every rule.
    /// </summary>
    public bool IsMatch(ReadOnlySpan<char> text)
    {
        if (IsEmpty)
            return text.IsEmpty;

        // Already known to run away: matching nothing is the answer the timeout produced anyway, so
        // there is nothing to gain by spending the budget again on every remaining entry.
        if (_abandoned)
            return false;

        // Anything this pattern can match has to contain that literal, so a text that doesn't is a
        // no-match without running the regex at all. Contains is a vectorised scan against a compiled
        // regex's automaton, and the answer is overwhelmingly "no" -- the exclusion rules are asked
        // about every result on the drive and reject almost none of them.
        if (_requiredLiteral != null && !text.Contains(_requiredLiteral, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_regex != null)
        {
            try
            {
                return _regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                // The timeout is a per-match ceiling, and this is asked about every entry on a drive --
                // so a pattern that backtracks catastrophically used to cost the full budget, and write a
                // log line, MILLIONS of times over. Retiring it on the first occurrence makes that cost
                // once, and turns a log flood into one line that names the pattern to go and fix.
                //
                // Safe to treat one occurrence as proof now that the budget is a second: an ordinary
                // match measures well under a microsecond, so reaching it means overshooting by six
                // orders of magnitude, which no amount of machine load accounts for. It would not have
                // been safe at the 50ms this used to be -- ordinary matches reached that under load,
                // which is the bug that led here.
                _abandoned = true;
                Logger.Log(
                    $"[NetworkGlobPattern] Glob '{_rawPattern}' exceeded the match timeout and will be ignored for the rest of this session -- it is almost certainly backtracking catastrophically. First offending input: '{text.ToString()}'",
                    LogLevel.Warn);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any spelling of the same path could still match: the required literal is a necessary
    /// condition for every spelling (name, relative and separator-suffixed forms are all substrings of the
    /// path, plus at most a separator the literal never contains), so one scan of the path decides it.
    /// </summary>
    public bool CouldMatch(string text) =>
        !IsEmpty
        && !_abandoned
        && (_requiredLiteral == null || text.Contains(_requiredLiteral, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Longest run of plain literal characters the pattern requires verbatim, or null if it has none
    /// worth testing.
    /// </summary>
    /// <remarks>
    /// Only runs that every match must contain count, so this skips anything inside {a,b} or [abc] --
    /// those are alternatives, and no one of them is required. Wildcards, separators and escapes end a
    /// run rather than extending it. Case is ignored on the way back out, matching how the compiled
    /// regex is built.
    /// </remarks>
    private static string? LongestRequiredLiteral(string glob)
    {
        var best = string.Empty;
        var start = -1;
        var brackets = 0;
        var inBraces = false;

        void EndRun(int end)
        {
            if (start >= 0 && end - start > best.Length)
                best = glob[start..end];
            start = -1;
        }

        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];

            if (inBraces)
            {
                if (c == '}') inBraces = false;
                continue;
            }
            if (brackets > 0)
            {
                if (c == '[') brackets++;
                else if (c == ']') brackets--;
                continue;
            }

            switch (c)
            {
                case '{':
                    EndRun(i);
                    inBraces = true;
                    break;
                case '[':
                    EndRun(i);
                    brackets = 1;
                    break;
                case '*':
                case '?':
                case '/':
                case '\\':
                case ',':
                case '}':
                case ']':
                    EndRun(i);
                    break;
                default:
                    if (start < 0) start = i;
                    break;
            }
        }
        EndRun(glob.Length);

        return best.Length >= MinimumUsefulLiteral ? best : null;
    }
}
