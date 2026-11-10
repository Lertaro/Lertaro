using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.App.Services.QuickPanel;

/// <summary>
/// Parsed QuickPanel source filter: the glob patterns the index enumerator already understands, plus
/// the search-syntax token filters that must be applied after enumeration. Positive glob and token
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

    /// <summary>Search-syntax plugin tokens, e.g. "\doc" or "\doc|img" -- the same spelling the search box uses.</summary>
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
/// Splits a QuickPanel source filter into positive globs, plugin-token filters, and "!"-negated globs.
/// Each entry must fully match one of those syntaxes; an entry that is neither a valid token nor a
/// wildcard is kept as a glob. A bare "!" is invalid and ignored.
/// </summary>
/// <remarks>
/// "Kept as a glob" is not harmless and used to be described as if it were: a source whose positive entries
/// all fail to parse enumerates NOTHING (see QuickPanelSourceLoader's hasNoPositiveFilter), it does not fall
/// back to showing everything. That is why the retired '@' token spelling is normalised above instead of
/// being left to decay into a glob.
/// </remarks>
public static class QuickPanelFilterParser
{
    public static QuickPanelFilterSpec Parse(string? filterPattern, char globalTokenPrefix = '\\')
    {
        var entries = FilterPatternHelper.Split(filterPattern ?? string.Empty);
        var globs = new List<string>();
        var tokens = new List<string>();
        var excludedGlobs = new List<string>();

        foreach (var rawEntry in entries)
        {
            var entry = ToCurrentTokenSpelling(rawEntry, globalTokenPrefix);
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

    /// <summary>
    /// Rewrites the retired "<anywhere>@doc" spelling into today's "&lt;prefix&gt;doc", preserving a leading
    /// '!'. The release before this one documented ":@doc" and ":@doc|img" as the way to filter a source by
    /// plugin token, so saved settings really do hold that shape, and the '@' marker no longer exists.
    /// Read as a glob it becomes a pattern no file name can match, and a filter list with no positive entry
    /// left in it is not "ignored" -- hasNoPositiveFilter makes the whole source enumerate nothing, so an
    /// upgraded install lost that panel tab silently. Normalising on read rather than migrating the stored
    /// value means a settings file exported or synced from another machine is understood too.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: only a bare keyword list after the marker is rewritten, so a glob that
    /// legitimately contains '@' ("mail@*", "C:\team\bob@acme\*") stays a glob.
    /// </remarks>
    private static string ToCurrentTokenSpelling(string entry, char globalTokenPrefix)
    {
        var negated = entry.StartsWith('!');
        var body = negated ? entry[1..] : entry;
        // The marker sat after whatever prefix was configured then, and a bare "@doc" was accepted too. ':'
        // was the shipped default at the time, so it is the value these files actually hold; the current
        // prefix is also accepted so the form works if the user had already moved it.
        if (body.Length > 0 && (body[0] == globalTokenPrefix || body[0] == ':'))
            body = body[1..];
        if (body.Length < 2 || body[0] != '@')
            return entry;

        var keywords = body[1..];
        if (keywords.IndexOfAny(['*', '?', '\\', '/', ' ']) >= 0)
            return entry;

        return $"{(negated ? "!" : "")}{globalTokenPrefix}{keywords}";
    }

    internal static bool TryParseTokenFilter(string entry, char globalTokenPrefix, out string token)
    {
        token = string.Empty;
        // A plugin token is spelled "\doc" or "\doc|img" -- the global token prefix then the keywords,
        // exactly as the search box writes it.
        if (entry.Length < 2 || entry[0] != globalTokenPrefix)
            return false;

        var raw = entry[1..];
        if (raw.Length == 0 || raw.Any(char.IsWhiteSpace))
            return false;

        var keywords = raw.Split('|');
        if (keywords.Any(string.IsNullOrEmpty))
            return false;

        // Repeated keywords inside one token conflict; the first occurrence wins ("doc|doc" -> "doc").
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>(keywords.Length);
        foreach (var keyword in keywords)
        {
            if (seen.Add(keyword))
                deduped.Add(keyword);
        }

        token = globalTokenPrefix + string.Join('|', deduped);
        return true;
    }
}
