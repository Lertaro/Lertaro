using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Manages panel-height layout math for QuickSearchWindow -- actions list height and results list
/// height. Ctrl+N shortcut-hint labeling is a separate concern handled by QuickSearchShortcutHelper
/// (still triggered from here after a resize, since that's when rows scroll into/out of view).
/// </summary>
internal sealed class QuickSearchWindowLayoutManager
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private int _layoutUpdateQueued;

    internal QuickSearchWindowLayoutManager(Lertaro.App.QuickSearchWindow window) => _window = window;

    public void UpdateActionsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is ActionMenuItem item)
                    {
                        totalHeight += item.ScaledItemHeight;
                    }
                }

                double actionsHeaderHeight = 28;
                if (_window.LstResults.SelectedItem is AppSearchResult selectedResult)
                {
                    actionsHeaderHeight = selectedResult.ActionsHeaderHeight;
                }

                double actualActionsHeight;
                if (items.Count == 0)
                {
                    // Collapse to a small neat height for "No Search Results"
                    actualActionsHeight = 40;
                }
                else
                {
                    // A flat pixel ceiling, not a "cap at item #9" rule: the actions list has no reason
                    // to match the results list's row COUNT, only the same overall height BUDGET it tops
                    // out at, so a full-length actions list with several half-height separators can still
                    // show more than 9 rows as long as the total stays within that same pixel ceiling.
                    // ScaledNormalRowHeight (not ScaledSearchResultItemHeight) matches what the results
                    // list's own rows actually render at once the icon-size floor kicks in -- using the
                    // unfloored value here would cap the actions panel shorter than the results list's own
                    // 9-row ceiling. Reduced by actionsHeaderHeight (the panel's own top banner, the target
                    // filename) so a full actions list's total height (banner + rows) still tops out at
                    // that same budget -- otherwise the window visibly grows taller the moment actions
                    // mode's own banner is added on top of a full-height list.
                    var maxAvailableHeight = UiMetrics.ScaledQuickSearchMaxResultHeight - actionsHeaderHeight;
                    // Let the height naturally fit the items count (free size dynamic resize)
                    actualActionsHeight = Math.Max(0.0, Math.Min(totalHeight, maxAvailableHeight));
                }
                _window.LstActions.Height = double.NaN;
                _window.ResultsPanelControl.Height = actualActionsHeight + actionsHeaderHeight;
            }
            else
            {
                _window.LstActions.Height = 40;
                _window.ResultsPanelControl.Height = 40 + 28;
            }
        }
        else
        {
            _window.LstActions.Height = double.NaN;
            QueueResultsLayoutUpdate();
        }

        _window.SizeToContent = SizeToContent.Manual;
        _window.SizeToContent = SizeToContent.WidthAndHeight;
    }

    // MUST stay deferred (never call ApplyResultsLayout synchronously from a Results.CollectionChanged
    // handler -- see QuickSearchWindow.xaml.cs's own comment on that subscription for why: LstResults's
    // ItemContainerGenerator is also a CollectionChanged subscriber, and forcing a layout pass before it
    // finishes reconciling the same notification throws). Render alone let a fully composited, wrong-
    // height frame slip through in real frame-level diagnostics, so this needs to sit above it -- but
    // Send (WPF's own input-dispatch priority) turned out to be one tier too aggressive: every keystroke's
    // results update now competed directly with the NEXT keystroke's own dispatch, which is what made
    // typing feel laggy. Normal sits between the two: still higher than Render (should still win the race
    // against that same paint), but no longer contends with Send-tier input processing. Needs the same
    // kind of real-world confirmation Send originally got -- re-add frame-level diagnostics if this turns
    // out not to be high enough after all.
    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            ApplyResultsLayout();
        }), DispatcherPriority.Normal);
    }

    // Runs the actual height computation immediately instead of deferring -- needed by
    // QuickSearchWindowController.ShowWindow, which forces its own synchronous UpdateLayout() right after
    // populating the startup panel, so this must run before that to avoid sizing the window to whatever
    // Height it was last left at. Safe to call synchronously there (unlike from Results.CollectionChanged
    // above) since it isn't itself running from inside that event's dispatch.
    public void ApplyResultsLayout()
    {
        // Sum each visible row's own height rather than assuming a uniform row size -- a section
        // header, the "show more" row, or a row whose icon forces it to grow past the base height
        // (see MinHeight in ListBox.xaml) would otherwise throw off a single-height-times-count guess,
        // leaving stray blank space (or clipping) at the bottom of the list.
        var results = _window.ViewModel.Results;
        var visibleCount = Math.Min(results.Count, UiMetrics.QuickSearchMaxVisibleResultRows);

        double resultsHeight = 0;
        for (var i = 0; i < visibleCount; i++)
        {
            resultsHeight += results[i].ScaledItemHeight;
        }

        // Item-based virtualization throughout, which realizes only the ~9 visible rows rather than the
        // full ~50-row result set. It was switched to pixel-based scrolling for one case: the startup
        // panel's tab strip sat stacked above this list, and the height left over after it was rarely a
        // whole multiple of the row height, so the boundary row had to be clipped rather than dropped.
        // Nothing sits above the list any more.
        ScrollViewer.SetCanContentScroll(_window.LstResults, true);

        // Shortcut hints (Ctrl+1..9) depend on which rows are visible, not on the panel's own height, so
        // they need refreshing on every call regardless of what's below -- but the SizeToContent toggle is
        // a full window measure/arrange, and re-running it when the height hasn't actually moved (common
        // while typing: narrowing an already 9+-result query keeps summing to the same 9-row total) was
        // pure waste stacked on every keystroke's results update.
        UpdateShortcutHints();

        if (_window.LstResults.Height == resultsHeight && _window.ResultsPanelControl.Height == resultsHeight)
            return;

        _window.LstResults.Height = resultsHeight;
        _window.ResultsPanelControl.Height = resultsHeight;
        _window.SizeToContent = SizeToContent.Manual;
        _window.SizeToContent = SizeToContent.WidthAndHeight;
    }

    public void UpdateShortcutHints() =>
        QuickSearchShortcutHelper.UpdateShortcutHints(_window, WpfUiHelper.GetScrollViewer(_window.LstResults));
}
