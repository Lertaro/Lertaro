using Lertaro.App.Helpers;
using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Keeps shortcut execution separate from the input event router so the router remains below the
// repository's per-file line limit. This helper mirrors InlineSearchShortcutHelper's viewport-relative
// slot accounting.
internal static class InlineSearchShortcutLauncher
{
    public static void Launch(Lertaro.App.InlineSearchWindow window, InlineSearchWindowLayoutManager layoutManager, int num)
    {
        if (num < 1 || num > 9) return;

        var scrollViewer = layoutManager.GetScrollViewer(window.LstResults);
        var firstVisible = WpfUiHelper.GetFirstVisibleIndex(scrollViewer, UiMetrics.InlineRowHeight);
        var shortcutIndex = 1;
        for (var i = 0; i < window.LstResults.Items.Count; i++)
        {
            if (window.LstResults.Items[i] is not AppSearchResult item
                || item.IsEmptyResult || item.IsSearchSectionHeader)
                continue;

            if (item.IsJumpToExplorerPath)
            {
                if (i >= firstVisible && shortcutIndex == num)
                {
                    window.ExecuteSearchResult(item);
                    return;
                }

                if (i >= firstVisible)
                    shortcutIndex++;
                continue;
            }

            if (i < firstVisible)
                continue;

            if (shortcutIndex == num)
            {
                window.ExecuteSearchResult(item);
                return;
            }

            shortcutIndex++;
        }
    }
}
