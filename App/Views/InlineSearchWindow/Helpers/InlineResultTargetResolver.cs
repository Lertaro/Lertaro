using System.IO;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Decides which inline-search row the window acts on, and what the host file manager should highlight.
//
// Both questions reduce to "does this row name a real file or folder", which is why they live together:
// the inline list leads with rows that are NOT files -- shortcut commands (plugin actions), instant results
// (a calculator answer, a URL) and section headers. Those are inserted ahead of every real result, so the
// naively-chosen row is one of them, and acting on it (Enter) runs the command instead of opening the file
// the user was actually looking for.
//
// Split out of InlineExplorerSelectionSync as pure functions (composition, not a partial class) so the
// decisions can be exercised without constructing a window, and to keep that file under the repo's
// per-file line limit.
internal static class InlineResultTargetResolver
{
    /// <summary>
    /// The row the inline list should land on by default.
    /// </summary>
    /// <remarks>
    /// The first selectable row, which is normally a shortcut command or instant result -- those are
    /// inserted ahead of every real result (see PluginSearchResultMapper). That is deliberate: this row is
    /// the one Enter acts on, so a query which IS a command keyword runs the command on Enter without the
    /// user having to arrow down to it. To search files alone, turn shortcut commands off in
    /// Settings → General → System (UserSettings.EnableSearchActions).
    /// </remarks>
    public static int ResolveAutoSelectIndex(IReadOnlyList<AppSearchResult> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (IsSelectable(rows[i]))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The location to mirror into the host file manager, or null when there is nothing to send.
    /// </summary>
    /// <remarks>
    /// The selected row is used when it names a real location. Otherwise the best-ranked row that does is
    /// used instead (the list is already in rank order, so the first such row is the strongest match
    /// shown) -- which is what keeps the host following the search even though the default selection sits
    /// on a shortcut command or instant result, rows the host cannot be pointed at. As a last resort the
    /// window's own folder is sent, which keeps the host on the directory the search is scoped to; that
    /// folder is one the host is already showing, so adapters either ignore it or clear their selection
    /// rather than navigating.
    /// </remarks>
    public static (string Path, bool IsDir)? ResolveMirrorTarget(
        IReadOnlyList<AppSearchResult> rows,
        int selectedIndex,
        string? windowDirectory)
    {
        var selected = selectedIndex >= 0 && selectedIndex < rows.Count ? rows[selectedIndex] : null;
        if (IsRealLocation(selected, out var path, out var isDir))
            return (path!, isDir);

        for (var i = 0; i < rows.Count; i++)
        {
            if (i == selectedIndex)
                continue;
            if (IsRealLocation(rows[i], out path, out isDir))
                return (path!, isDir);
        }

        return string.IsNullOrWhiteSpace(windowDirectory) ? null : (windowDirectory, true);
    }

    /// <summary>
    /// The row Enter should act on, or null when there is nothing to run.
    /// </summary>
    /// <remarks>
    /// Normally the selected row. When nothing selectable is selected (the list was just replaced and the
    /// selection has not been restored yet), it falls back to the same row the default selection would pick
    /// -- NOT simply index 0, which is usually a section header and would make Enter do nothing at all.
    /// </remarks>
    public static AppSearchResult? ResolveEnterTarget(IReadOnlyList<AppSearchResult> rows, int selectedIndex)
    {
        var selected = selectedIndex >= 0 && selectedIndex < rows.Count ? rows[selectedIndex] : null;
        if (IsSelectable(selected))
            return selected;

        var index = ResolveAutoSelectIndex(rows);
        return index >= 0 ? rows[index] : null;
    }

    /// <summary>
    /// Whether this row names a real location on disk.
    /// </summary>
    /// <remarks>
    /// Only a fully-qualified path qualifies. The list also carries section headers, the
    /// "no results"/"show more" placeholders and plugin tokens ("__PLUGIN_ACTION__:...",
    /// "__SEARCHABLE_ITEM__:...") plus non-file instant results (a URL, a calculator answer), none of which
    /// is a path -- sent to an adapter, one arrives as a nonsense filename that quietly does nothing.
    /// </remarks>
    public static bool IsRealLocation(AppSearchResult? row, out string? path, out bool isDir)
    {
        path = null;
        isDir = false;
        if (row is null || string.IsNullOrEmpty(row.FullPath) || !Path.IsPathFullyQualified(row.FullPath))
            return false;

        path = row.FullPath;
        isDir = row.IsDir;
        return true;
    }

    // A row the list may hold the selection on at all -- same test the arrow keys navigate by
    // (InlineSearchWindowInputHandler.IsSelectableResult), so the two cannot drift.
    private static bool IsSelectable(AppSearchResult? row) =>
        row is not null && !row.IsEmptyResult && !row.IsSearchSectionHeader;
}
