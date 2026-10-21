using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Executes a selected/clicked search result from the results list -- split out of QuickSearchWindow
// itself to keep that file under the line-count limit.
public class QuickSearchWindowResultExecutor
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    public QuickSearchWindowResultExecutor(Lertaro.App.QuickSearchWindow window) => _window = window;

    public void HandlePreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
        {
            e.Handled = true;
            var asAdmin = Keyboard.Modifiers == ModifierKeys.Control;
            Execute(result, asAdmin);
        }
    }

    public void HandlePreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var item = Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
        {
            e.Handled = true;
            _window.LstResults.SelectedItem = result;
            _window.MenuPresenter?.EnterActionsMode(result);
        }
    }

    public void Execute(AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsEmptyResult || result.IsSearchSectionHeader)
            return;
        // The calibration log's other half: which row the user picked. Position is not recorded, because
        // the list may already have been re-ranked by a later batch when the click lands. A row with no path
        // (a calculator answer, an instant row) would still spend a hash bucket, so it is skipped.
        if (!string.IsNullOrWhiteSpace(result.FullPath))
            RankingEventLog.RecordOpened(result.SearchQuery, result.FullPath);
        _window.RecordKeywordHistory();
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(SearchResultHelper.HistoryKeywordOf(result, _window.TxtSearch.Text), result.FullPath, SearchResultHelper.HistoryKindOf(result));
        }

        if (result.IsPluginSearchAction)
        {
            PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin);
            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin))
        {
            return;
        }

        var currentQuery = _window.TxtSearch.Text;
        if (result.FullPath == "__SHOW_MORE__")
        {
            // The full window has no concept of a per-type trigger, so one is stripped before it lands there.
            var carriedQuery = SearchResultTypePriority.StripLeadingTrigger(currentQuery);
            _window.HideWindowNoRestore();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
        }
        else
        {
            _window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, _window.HideWindow);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, _window.HideWindow);
        }
    }
}
