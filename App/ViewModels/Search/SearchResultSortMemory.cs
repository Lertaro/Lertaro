namespace Lertaro.App.ViewModels.Search;

// Remembers the full window's active sort-by-column state for as long as the app process keeps
// running -- deliberately NOT backed by UserSettings/disk (unlike UiMetrics' load-once-then-live-
// in-memory pattern): a fresh SearchViewModel is constructed every time the full window is reopened
// (see FileExecutor's "__SHOW_MORE__" path, which always does `new SearchWindow(...)`), so a plain
// instance field would reset on every reopen even within the same session. A process-wide static is
// the only way to carry "what did I last click" across those separate instances without writing it
// to the settings file, which would make it survive an app restart too -- something nothing asked for.
internal static class SearchResultSortMemory
{
    public static string CurrentSortColumn { get; set; } = string.Empty;
    public static bool IsSortAscending { get; set; } = true;
}
