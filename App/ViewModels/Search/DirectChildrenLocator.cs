using System.IO;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search;

// Fast one-level locator for the inline window's "Current Folder" section: lists the files sitting
// directly in the window's own folder and matches the query against their names.
//
// Why this exists next to the global search: the engine covers the WHOLE subtree and only ships its
// top-ranked candidates to the App, so in a folder
// with many matches the direct children can be crowded out of that window entirely by deeper files. The
// proximity ordering (DirectoryProximity) can only reorder what actually arrived -- it cannot bring back
// a direct child the engine never sent. Enumerating one level here is cheap, independent of the index,
// and guarantees the folder's own files are both present and first.
internal static class DirectChildrenLocator
{
    // ponytail: bounded scan. A directory holding an enormous number of entries stops being examined
    // after this many rather than stalling the inline search; anything past the cap can still arrive
    // through the global search, whose merge re-orders this folder's descendants first, so nothing is
    // permanently lost -- only this fast pass gives up on it.
    internal const int MaxExaminedEntries = 20_000;

    // Emits every direct child of `directory` whose name matches `query` (case-insensitively, through the
    // same alias-aware matcher the rest of the app uses), as a plain SearchResult for the caller to map.
    // Hidden/system entries are skipped to match the engine's own always-on filter. Returns the number
    // emitted; `maxMatches` bounds both the emitted count and the work on a broad query.
    //
    // Matches against a LISTING the caller already has, rather than walking the folder itself: the walk is
    // the part that grows with the folder's size, and this is called once per keystroke (and per streaming
    // paint). See DirectChildrenListingCache, which owns the walk and hands the same listing to every call
    // for one folder.
    public static int MatchInto(IReadOnlyList<Entry> entries, string directory, string query, int maxMatches, Action<SearchResult> onMatch, CancellationToken token)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(query) || maxMatches <= 0)
            return 0;

        var emitted = 0;
        var drive = Path.GetPathRoot(directory) ?? string.Empty;
        // Parsed once for the whole listing: every entry would otherwise re-parse the query.
        var fuzzy = FuzzyQuery.Parse(query);

        // A one-letter query needs its own path. IAliasLiveMatcherProvider states that "single letters are
        // not this provider's words" -- CreateMatcher returns null for them, so IsAliasMatch answers false
        // for every han-character name and the folder the user is looking at stays unreachable at one
        // character. The initials spelling has no such floor, so ask the provider for it directly.
        // ponytail: one entry per name, no per-name cache. A folder listing is at most a few thousand names
        // and this only runs for single letters; the ceiling is the reading lookup per entry per keystroke.
        // Upgrade path: fold it into DirectChildrenListingCache alongside the names it already keeps.
        var initialsLetter = query.Length == 1 && char.IsAsciiLetter(query[0])
            ? char.ToLowerInvariant(query[0])
            : '\0';
        var aliveProviders = initialsLetter == '\0'
            ? null
            : Lertaro.Core.SearchIndex.AliasProviderRegistry.GetActiveProviders()
                .OfType<Lertaro.PluginSdk.Abstractions.Plugins.IAliasLiveMatcherProvider>()
                .ToArray();
        var spellingScratch = aliveProviders is { Length: > 0 } ? new List<string>(4) : null;

        // TEMP diagnostics: what each entry actually matched on, so a han-character name that should have
        // been reachable but was not can be told apart from a ranking problem. Remove once this is settled.
        var probe = Lertaro.Core.SearchIndex.PerfTrace.IsEnabled && entries.Count < 14
            ? new System.Text.StringBuilder()
            : null;

        foreach (var entry in entries)
        {
            if (token.IsCancellationRequested || emitted >= maxMatches)
                break;
            // IsMatch compares characters, which is all an English name needs. A han-character name is not
            // reachable that way at all, so an initials query returned nothing from the folder the user was
            // looking at while the same query worked in the global results -- the index rows carry a baked
            // alias and these enumeration rows do not. The alias check is second on purpose: it is the
            // expensive one, and an English name never reaches it.
            var byChar = fuzzy.IsMatch(entry.Name);
            var byAlias = !byChar && fuzzy.IsAliasMatch(entry.Name);
            var byInitial = !byChar && !byAlias && spellingScratch != null && StartsWithInitial(entry.Name, initialsLetter, aliveProviders!, spellingScratch);
            if (probe != null)
                probe.Append(entry.Name).Append(byChar ? "+c " : byAlias ? "+a " : byInitial ? "+i " : "- ");
            if (!byChar && !byAlias && !byInitial)
                continue;

            onMatch(new SearchResult
            {
                Name = entry.Name,
                Path = entry.Path,
                IsDir = entry.IsDir,
                Drive = drive,
                Attributes = entry.Attributes
            });
            emitted++;
        }

        if (probe != null)
            Logger.Log($"[DirectMatch] q='{query}' dir={directory} entries={entries.Count} emitted={emitted} [{probe}]", LogLevel.Info);

        return emitted;
    }

    // Whether the name's initials spelling starts with `letter` -- "动漫" answers 'd' through "dm", which
    // the reading matcher refuses to do for a one-letter term.
    private static bool StartsWithInitial(string name, char letter,
        Lertaro.PluginSdk.Abstractions.Plugins.IAliasLiveMatcherProvider[] providers, List<string> scratch)
    {
        foreach (var provider in providers)
        {
            scratch.Clear();
            provider.AppendSpellings(name, scratch);
            foreach (var spelling in scratch)
            {
                if (spelling.Length > 0 && char.ToLowerInvariant(spelling[0]) == letter)
                    return true;
            }
        }

        return false;
    }

    // Convenience for callers that have no listing to reuse: walk, then match. The inline search goes
    // through the listing overload instead, so its walk is not repeated per keystroke.
    public static int MatchInto(string directory, string query, int maxMatches, Action<SearchResult> onMatch, CancellationToken token) =>
        MatchInto(Enumerate(directory, token), directory, query, maxMatches, onMatch, token);

    /// <summary>One name from a folder listing -- everything a match needs, without a SearchResult.</summary>
    internal readonly record struct Entry(string Name, string Path, bool IsDir, FileAttributes Attributes);

    /// <summary>
    /// Walks one level of <paramref name="directory"/>, returning its visible entries.
    /// </summary>
    /// <remarks>
    /// This is the folder-size-dependent half, kept separate from matching so it can be done once per
    /// folder instead of once per keystroke. Still bounded by <see cref="MaxExaminedEntries"/> so a
    /// directory with an absurd number of entries cannot stall the caller.
    /// </remarks>
    public static List<Entry> Enumerate(string directory, CancellationToken token)
    {
        var entries = new List<Entry>();
        if (string.IsNullOrWhiteSpace(directory))
            return entries;

        // A virtual path that is not a real directory simply has no direct children to list.
        if (!Directory.Exists(directory))
            return entries;

        var examined = 0;
        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (token.IsCancellationRequested || examined >= MaxExaminedEntries)
                    break;
                examined++;

                FileAttributes attributes;
                try { attributes = entry.Attributes; } catch { continue; }
                if (FileSystemItemFilter.IsHiddenOrSystem(attributes))
                    continue;

                entries.Add(new Entry(entry.Name, entry.FullName, attributes.HasFlag(FileAttributes.Directory), attributes));
            }
        }
        catch
        {
            // A directory that cannot be enumerated (permissions, removed mid-scan, an unsupported
            // virtual path) simply contributes nothing here; the scoped search still covers it.
        }

        return entries;
    }
}
