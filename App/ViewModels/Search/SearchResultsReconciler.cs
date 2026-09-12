using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Reconciles a fresh result list into the live ObservableRangeCollection row-by-row instead of a full
// Clear+Add reset (only changed rows are replaced in place, recycling ListBox containers, so the list
// is never torn down and rebuilt from the top -- which is what caused the flicker this was written to
// fix), and preserves the current selection when it survives the update.
internal static class SearchResultsReconciler
{
    public static void Replace(
        ObservableRangeCollection<AppSearchResult> results,
        IEnumerable<AppSearchResult> newResults,
        AppSearchResult? currentSelection,
        Action<AppSearchResult?> setSelection)
    {
        var list = newResults as List<AppSearchResult> ?? new List<AppSearchResult>(newResults);
        results.ReconcileTo(list, ItemsEqual);
        SyncMutableDisplayState(results, list);

        // Only re-select when the current one is gone or no longer selectable, so streaming updates
        // don't yank the highlight back to the top.
        if (currentSelection != null && results.Contains(currentSelection)
            && !currentSelection.IsEmptyResult && !currentSelection.IsSearchSectionHeader)
            return;

        AppSearchResult? firstSelectable = null;
        foreach (var result in list)
        {
            if (!result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                firstSelectable = result;
                break;
            }
        }

        setSelection(firstSelectable);
    }

    // Internal (not private) so SearchViewModel.RenderFinal can reconcile FilteredResults with this
    // exact same row-identity check, instead of maintaining a second definition that could drift.
    //
    // Deliberately NOT comparing SearchQuery. Every row of a search carries that query, so including it
    // made every row compare unequal on every keystroke -- which meant every row was replaced, and every
    // realized row re-bound its icon, rebuilt its highlight Runs and restarted its marquee. Typing a
    // second character re-did all of that for a list that had mostly not changed. Identity is the row's
    // content (path + name + kind); the query is presentation state, carried across by
    // SyncMutableDisplayState below.
    internal static bool ItemsEqual(AppSearchResult a, AppSearchResult b) =>
        string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
        string.Equals(a.ResultKind, b.ResultKind, StringComparison.Ordinal);

    /// <summary>
    /// Copies the per-paint values a surviving row cannot derive on its own from <paramref name="target"/>
    /// onto the instances that were kept in place.
    /// </summary>
    /// <remarks>
    /// A kept row is the same row (same path/name/kind) but a different search's instance, so its stored
    /// query and rank position describe the previous search. SearchQuery drives the highlight, so leaving
    /// it stale would freeze the highlighting at the previous keystroke; only rows that actually survived
    /// need this -- a replaced row already carries the new values.
    /// </remarks>
    internal static void SyncMutableDisplayState(
        IReadOnlyList<AppSearchResult> live,
        IReadOnlyList<AppSearchResult> target)
    {
        var shared = Math.Min(live.Count, target.Count);
        for (var i = 0; i < shared; i++)
        {
            if (ReferenceEquals(live[i], target[i]))
                continue;

            // SearchQuery notifies (it is bound), so a no-op assignment is already filtered out by its
            // own setter. The execution payloads are plain fields, but they must follow the fresh row:
            // instant providers can derive them from the query even when the row identity stays equal.
            live[i].SearchQuery = target[i].SearchQuery;
            live[i].Index = target[i].Index;
            live[i].PluginActionId = target[i].PluginActionId;
            live[i].PluginActionArgumentText = target[i].PluginActionArgumentText;
            live[i].InstantResultActionType = target[i].InstantResultActionType;
            live[i].InstantResultActionArgument = target[i].InstantResultActionArgument;
            live[i].InstantResultOnExecute = target[i].InstantResultOnExecute;
            live[i].InstantResultOnExecuteFunc = target[i].InstantResultOnExecuteFunc;
            live[i].TabCompletion = target[i].TabCompletion;
            // Read by the full window's type filter, from the freshly-built list rather than from the
            // displayed one, so a stale value here is currently benign -- kept in step anyway so a future
            // reader of a retained row cannot be handed the previous search's answer.
            live[i].IsFullSearchFileResult = target[i].IsFullSearchFileResult;
        }
    }
}
