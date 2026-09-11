using System.IO;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search;

// Fast one-level locator for the inline window's "Current Folder" section: lists the files sitting
// directly in the window's own folder and matches the query against their names.
//
// Why this exists next to the scoped engine search (ExplorerSearchHelper.SearchLocalMatchesAsync): that
// search covers the WHOLE subtree and only ships its top-ranked candidates to the App, so in a folder
// with many matches the direct children can be crowded out of that window entirely by deeper files. The
// proximity ordering (DirectoryProximity) can only reorder what actually arrived -- it cannot bring back
// a direct child the engine never sent. Enumerating one level here is cheap, independent of the index,
// and guarantees the folder's own files are both present and first.
internal static class DirectChildrenLocator
{
    // ponytail: bounded scan. A directory holding an enormous number of entries stops being examined
    // after this many rather than stalling the inline search; anything past the cap is still delivered by
    // the scoped engine search, so nothing is permanently lost -- only this fast pass gives up on it.
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

        foreach (var entry in entries)
        {
            if (token.IsCancellationRequested || emitted >= maxMatches)
                break;
            if (!fuzzy.IsMatch(entry.Name))
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

        return emitted;
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
