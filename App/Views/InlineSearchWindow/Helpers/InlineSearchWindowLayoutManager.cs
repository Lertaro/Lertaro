using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public sealed class InlineSearchWindowLayoutManager
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private int _layoutUpdateQueued;

    public InlineSearchWindowLayoutManager(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));

        // LstResults is the same shared ResultsControl.xaml markup Quick/Inline/Full all use.
        // Pixel-based scrolling, pinned for this window's whole lifetime. Tried switching this to
        // item-based (logical) scrolling alongside QueueResultsLayoutUpdate's move to a row-height SUM
        // below -- reasoning being that InlineRowHeight is now a literal constant every row genuinely
        // renders at (see ListBox.xaml's Style.Trigger for a "Lertaro Inline"-titled window), so a
        // 9-row sum is an exact whole multiple of it by construction, which is what item-based scrolling
        // needs to render without a leftover gap. Confirmed by testing that this ISN'T enough: a query
        // short enough to fit without scrolling (exactly 9 results) never showed the gap, but any query
        // that actually needs to scroll (more than 9 results, e.g. "dev") reproduced it 100% of the time
        // -- item-based scrolling estimates how many rows fit its own viewport from a container height IT
        // measures internally, not from the number this class hands it, and that internal estimate can
        // still round down by one row independent of how exact our own sum is. Pixel-based scrolling has
        // no such estimation step -- it just clips whatever doesn't fit -- so it's the only one of the two
        // that's actually robust here, which is why 78ddae91/74c73cf1 landed on it after this exact same
        // idea was tried and reverted before. QueueResultsLayoutUpdate's move away from measuring the real
        // ListBox to a direct height sum is still worth keeping independent of this -- it avoids
        // force-realizing every bound row just to measure a total, regardless of scrolling mode.
        ScrollViewer.SetCanContentScroll(_window.LstResults, false);
    }

    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            if (!_window.IsVisible) return;

            // Sums each of the first 9 rows' own InlineItemHeight instead of measuring the real ListBox
            // (what this used to do -- see git log on this file for the several rounds that predated it).
            // The old hand-summed predictions drifted out of sync with what WPF actually rendered because
            // a normal row's true container height used to come from ResultItemStyle's MinHeight, a
            // SEPARATE number (ItemHeight, the full-size 51px main-window metric) from what the sum
            // assumed -- headers being unified to InlineItemHeight alone was never enough to fix it. Now
            // that ResultItemStyle's own Style.Trigger already binds MinHeight to this SAME InlineItemHeight
            // for a "Lertaro Inline"-titled window (see ListBox.xaml), and InlineItemHeight itself is a
            // literal UiMetrics constant instead of a derived ratio, this sum and the real render are
            // reading the exact same number for every row -- there's no separate formula left to drift.
            var results = _window.ViewModel.Results;
            var count = results.Count;
            var visibleCount = Math.Min(count, 9);
            double resultsHeight = 0;
            for (var i = 0; i < visibleCount; i++)
                resultsHeight += results[i].InlineItemHeight;

            // PathPreviewBorder (the truncated-path banner above the list, Grid.Row sibling of
            // ResultsPanelControl -- see InlineSearchWindow.xaml) is never counted into this 9-row sum:
            // unlike the quick window (which sizes itself via SizeToContent and so needs its tab-strip
            // case to land on the exact same total height as its bannerless/tabstrip-less case, see
            // QuickSearchWindowLayoutManager's own ceiling), this window's shell is a fixed 550px that
            // already has headroom for content to grow inside it (see InlineSearchWindowPositioner's own
            // comment on that) -- there's no bannerless sibling state it needs to visually match, so the
            // banner can simply add its own height on top of a full, uncompromised 9-row list.
            _window.LstResults.Height = resultsHeight;
            _window.ResultsPanelControl.Height = resultsHeight;
            // Forces layout to actually run right now, synchronously, instead of leaving WPF free to
            // repaint the ListBox with whatever's now bound to ItemsSource at its next opportunity
            // (which could win the race against this callback and render new content at the stale
            // Height briefly) -- mirrors what the quick window's own SizeToContent toggle achieves.
            _window.UpdateLayout();

            if (count == 0)
            {
                _window.LstResults.SelectedIndex = -1;
            }

            UpdateShortcutHints();
            _window.Positioner.PositionWindow();
        }), DispatcherPriority.Render);
    }

    public void UpdateActionsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            _window.PathPreviewBorder?.Visibility = Visibility.Collapsed;

            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is ActionMenuItem item)
                    {
                        totalHeight += item.ItemHeight;
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
                    actualActionsHeight = 40;
                }
                else
                {
                    // Not reduced by actionsHeaderHeight: that's the panel's own top banner (its target
                    // filename), additional content stacked above the action rows, not something sharing
                    // a fixed total budget with them -- see QueueResultsLayoutUpdate's own comment on the
                    // exact same fix for the results list's path-preview banner. Subtracting it here left
                    // the actions list unable to ever reach the same 9-row height the results list gets.
                    var maxAvailableHeight = 9 * Math.Round(Services.UiMetrics.SearchResultItemHeight * 0.7);
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
            UpdatePathPreviewVisibility();
            QueueResultsLayoutUpdate();
        }

        _window.Positioner.PositionWindow();
    }

    public void UpdateShortcutHints()
    {
        var scrollViewer = GetScrollViewer(_window.LstResults);
        InlineSearchShortcutHelper.UpdateShortcutHints(_window, scrollViewer);
    }

    public ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;

            if (child is FrameworkContentElement fce)
            {
                child = fce.Parent;
            }
            else
            {
                child = VisualTreeHelper.GetParent(child);
            }
        }
        return null;
    }
    // Hovering a result now selects it (see ResultsControl.xaml.cs), so SelectedItem alone is already
    // the "active" result -- no separate hover-tracking state needed here anymore.
    public void UpdatePathPreviewVisibility() => _window.Dispatcher.BeginInvoke(new Action(() =>
                                                      {
                                                          if (_window.LstResults.SelectedItem is not AppSearchResult activeResult)
                                                          {
                                                              if (_window.PathPreviewBorder != null && _window.PathPreviewBorder.Visibility != Visibility.Collapsed)
                                                              {
                                                                  _window.PathPreviewBorder.Visibility = Visibility.Collapsed;
                                                                  QueueResultsLayoutUpdate();
                                                              }
                                                              return;
                                                          }

                                                          var isTruncated = CheckIfResultIsTruncated(activeResult);
                                                          var vm = _window.ViewModel;

                                                          var isShowMore = activeResult.FullPath == "__SHOW_MORE__";

                                                          var shouldShow = _window.ResultsPanelControl.ActionsGrid.Visibility != Visibility.Visible &&
                                                                            isTruncated &&
                                                                            vm.IsInlineSearchContext &&
                                                                            !activeResult.IsEmptyResult &&
                                                                            !activeResult.IsSearchSectionHeader &&
                                                                            !activeResult.IsListItem &&
                                                                            !activeResult.IsPluginSearchAction &&
                                                                            !activeResult.IsInstantResult &&
                                                                            (!string.IsNullOrEmpty(activeResult.FullPath) || isShowMore);

                                                          var targetVisibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                                                          if (_window.PathPreviewBorder != null)
                                                          {
                                                              if (shouldShow)
                                                              {
                                                                  _window.PathPreviewTextBlock.Text = isShowMore ? activeResult.Name : ViewModels.Search.SearchResultHelper.FormatWslPath(activeResult.FullPath);
                                                              }

                                                              if (_window.PathPreviewBorder.Visibility != targetVisibility)
                                                              {
                                                                  _window.PathPreviewBorder.Visibility = targetVisibility;
                                                                  QueueResultsLayoutUpdate();
                                                              }
                                                          }
                                                      }), DispatcherPriority.Loaded);

    private bool CheckIfResultIsTruncated(AppSearchResult result)
    {
        if (_window.LstResults.ItemContainerGenerator.ContainerFromItem(result) is not ListBoxItem container) return false;

        var scrollViewers = new List<ScrollViewer>();
        FindScrollViewers(container, scrollViewers);
        foreach (var sv in scrollViewers)
        {
            if (sv.ScrollableWidth > 0)
            {
                if (result.IsJumpToExplorerPath && Grid.GetColumn(sv) == 1)
                {
                    continue;
                }
                return true;
            }
        }
        return false;
    }

    private static void FindScrollViewers(DependencyObject depObj, List<ScrollViewer> list)
    {
        if (depObj == null) return;
        if (depObj is ScrollViewer viewer)
        {
            list.Add(viewer);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            FindScrollViewers(child, list);
        }
    }
}
