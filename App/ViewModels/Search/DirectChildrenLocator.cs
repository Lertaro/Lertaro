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
    public static int MatchInto(string directory, string query, int maxMatches, Action<SearchResult> onMatch, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(query) || maxMatches <= 0)
            return 0;

        // A virtual path that is not a real directory simply has no direct children to list.
        if (!Directory.Exists(directory))
            return 0;

        var emitted = 0;
        var examined = 0;
        var drive = Path.GetPathRoot(directory) ?? string.Empty;
        // Parsed once for the whole directory listing: every entry would otherwise re-parse the query.
        var fuzzy = FuzzyQuery.Parse(query);

        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (token.IsCancellationRequested || emitted >= maxMatches || examined >= MaxExaminedEntries)
                    break;
                examined++;

                FileAttributes attributes;
                try { attributes = entry.Attributes; } catch { continue; }
                if (FileSystemItemFilter.IsHiddenOrSystem(attributes))
                    continue;
                if (!fuzzy.IsMatch(entry.Name))
                    continue;

                onMatch(new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    IsDir = attributes.HasFlag(FileAttributes.Directory),
                    Drive = drive,
                    Attributes = attributes
                });
                emitted++;
            }
        }
        catch
        {
            // A directory that cannot be enumerated (permissions, removed mid-scan, an unsupported
            // virtual path) simply contributes nothing here; the scoped search still covers it.
        }

        return emitted;
    }
}
