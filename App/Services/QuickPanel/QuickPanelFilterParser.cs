using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.App.Services.QuickPanel;

/// <summary>
/// Parsed QuickPanel source filter: the glob patterns the index enumerator already understands, plus
/// the search-syntax "@" token filters that must be applied after enumeration. Positive glob and token
/// entries are OR-ed together, matching the existing wildcard-only filter semantics; entries prefixed
/// with "!" (e.g. "!*.xxx") are exclusions removed after the positive set is computed.
/// </summary>
public sealed class QuickPanelFilterSpec
{
    public static QuickPanelFilterSpec MatchAll { get; } = new(new[] { "*" }, Array.Empty<string>(), Array.Empty<string>());

    public QuickPanelFilterSpec(string[] globPatterns, string[] tokenFilters, string[] excludedGlobPatterns)
    {
        GlobPatterns = globPatterns;
        TokenFilters = tokenFilters;
        ExcludedGlobPatterns = excludedGlobPatterns;
    }

    /// <summary>Filename patterns for the index enumerator / glob matching. Empty when only token filters exist.</summary>
    public string[] GlobPatterns { get; }

    /// <summary>Search-syntax "@" tokens without the global ":" prefix, e.g. "@doc" or "@doc|img".</summary>
    public string[] TokenFilters { get; }

    /// <summary>"!"-prefixed glob entries, e.g. "*.xxx" for "!*.xxx"; removed after positive matching.</summary>
    public string[] ExcludedGlobPatterns { get; }

    public bool HasTokenFilters => TokenFilters.Length > 0;
    public bool HasExcludedGlobPatterns => ExcludedGlobPatterns.Length > 0;

    /// <summary>Whether parsing produced no filter at all (only the match-all "*" glob).</summary>
    public bool IsMatchAll => GlobPatterns.Length == 1 && GlobPatterns[0] == "*"
        && !HasTokenFilters && !HasExcludedGlobPatterns;

    /// <summary>Whether the loader must run <c>ApplyFilterAsync</c> after enumeration.</summary>
    public bool NeedsPostFilter => HasTokenFilters || HasExcludedGlobPatterns;
}

/// <summary>
/// Splits a QuickPanel source filter into positive globs, "@" token filters, and "!"-negated globs.
/// Each entry must fully match one of those syntaxes; a ":" entry that is not a valid "@" token is
/// deliberately left as a glob pattern, where the colon can never match a real file name and the entry
/// is effectively ignored. A bare "!" is invalid and ignored.
/// </summary>
public static class QuickPanelFilterParser
{
    public static QuickPanelFilterSpec Parse(string? filterPattern, char globalTokenPrefix = ':')
    {
        var entries = FilterPatternHelper.Split(filterPattern ?? string.Empty);
        var globs = new List<string>();
        var tokens = new List<string>();
        var excludedGlobs = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.StartsWith('!'))
            {
                var excluded = entry[1..];
                if (excluded.Length == 0)
                    continue; // invalid entry -- ignore

                // First one wins when the same exclusion is listed twice.
                if (!excludedGlobs.Contains(excluded, StringComparer.OrdinalIgnoreCase))
                    excludedGlobs.Add(excluded);
                continue;
            }

            if (TryParseTokenFilter(entry, globalTokenPrefix, out var token))
            {
                // Conflicting/duplicate token filters: first one wins.
                if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                    tokens.Add(token);
            }
            else
            {
                globs.Add(entry);
            }
        }

        // Match-all is an enumeration signal for globs, not a pattern to match after the fact.
        if (globs.Count == 0 && tokens.Count == 0 && excludedGlobs.Count == 0)
            globs.Add("*");

        if (globs.Count > 1)
            globs = globs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (excludedGlobs.Count > 1)
            excludedGlobs = excludedGlobs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new QuickPanelFilterSpec(globs.ToArray(), tokens.ToArray(), excludedGlobs.ToArray());
    }

    internal static bool TryParseTokenFilter(string entry, char globalTokenPrefix, out string token)
    {
        token = string.Empty;
        // Search-syntax @ filters are spelled ":@doc" or ":@doc|img" in the QuickPanel filter field
        // (the global ":" prefix, then the @ token the search box would dispatch to the
        // CustomFilterQueryTokenProvider).
        if (entry.Length < 3 || entry[0] != globalTokenPrefix || entry[1] != '@')
            return false;

        var raw = entry[2..];
        if (raw.Length == 0 || raw.Any(char.IsWhiteSpace))
            return false;

        var keywords = raw.Split('|');
        if (keywords.Any(string.IsNullOrEmpty))
            return false;

        // Repeated keywords inside one token conflict; the first occurrence wins ("@doc|doc" -> "@doc").
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>(keywords.Length);
        foreach (var keyword in keywords)
        {
            if (seen.Add(keyword))
                deduped.Add(keyword);
        }

        token = "@" + string.Join('|', deduped);
        return true;
    }
}
