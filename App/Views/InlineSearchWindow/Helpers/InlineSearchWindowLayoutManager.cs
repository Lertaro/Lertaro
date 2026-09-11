using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public sealed class InlineSearchWindowLayoutManager
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private int _layoutUpdateQueued;

    public InlineSearchWindowLayoutManager(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));

        // Item-based scrolling ENABLES virtualization; pixel-based (CanContentScroll=false) disables it
        // outright in WPF, realizing and rendering EVERY bound row even though the card only ever shows
        // about nine of them. That was the whole cost: measured on a large folder, the UI-thread time of a
        // paint tracked the number of bound rows rather than the number visible (0.6ms at 2 rows, 119.8ms
        // at 103), which is why a result-rich folder felt heavy here while the quick window -- virtualizing
        // all along -- stayed smooth. Verified that this really does virtualize: a 100-row list realizes 17
        // containers, not 100.
        //
        // ScrollUnit is deliberately NOT set alongside this: it is an attached property on the ItemsControl,
        // but its value does not reach the internal VirtualizingStackPanel that actually scrolls (measured:
        // setting Pixel on the ListBox leaves the panel on Item), so it would look like a safeguard while
        // doing nothing. Item-based scrolling is also what the inline list wants now -- the row area is a
        // fixed whole number of rows (InlineCardMetrics.ComputeLayout) at a constant row height, so there is
        // no partial row left for pixel scrolling to clip. That partial row is the reason this was pinned to
        // pixel scrolling before; it was a real problem when the height was a sum over however many results
        // had arrived, and it no longer exists.
        ScrollViewer.SetCanContentScroll(_window.LstResults, true);
    }

    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            if (!_window.IsVisible) return;

            // The results area is a CONSTANT height -- the configured row count times the row height --
            // instead of a sum over however many results have arrived. Growing the card as results
            // streamed in is what made it feel restless while typing, and it also invalidated the
            // positioner's cached inputs on every result count change, so a settled height is cheaper too.
            // The row height itself is still the same literal UiMetrics constant the ListBox's own
            // Style.Trigger binds MinHeight to for a "Lertaro Inline"-titled window (see ListBox.xaml), so
            // this height and the rendered rows cannot drift apart.
            var results = _window.ViewModel.Results;
            var count = results.Count;
            // One source of truth for the split. The result budget is 9 regardless of the titles, and the
            // titles ride on top of it -- see InlineCardMetrics.ComputeLayout. That is what makes the number
            // of visible results the same whether both "Current Folder" and "Global Search" are showing or
            // neither is, instead of dropping to 7 whenever both appear.
            var layout = _window.CardSizing.CurrentLayout();

            // The list is only as tall as the items that exist, and the skeleton rows underneath it fill
            // the rest of the fixed area -- together they are always the whole area, which is what keeps
            // the card's height constant while a search is still filling in. Sizing the list to the whole
            // area instead would put the skeleton rows BELOW it and grow the card.
            _window.LstResults.Height = layout.ShownItems * UiMetrics.InlineRowHeight;
            _window.ResultsPanelControl.Height = layout.ShownItems * UiMetrics.InlineRowHeight;
            _window.UpdatePlaceholderSlots();
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
            SetPathBannerVisible(false);

            // The actions panel lives in the same fixed results area the list uses, so opening or closing it
            // cannot change the card's height -- that was half of the resize churn this window is rid of. It
            // does NOT size itself to its own content either: the panel scrolls inside the area.
            //
            // Sized from the ROW BUDGET, not from CurrentLayout(): that helper reads the RESULTS collection,
            // which is the wrong list here (the results list is hidden while the actions panel is up, so its
            // item count is meaningless for how tall the panel should be). Reading it would size the panel
            // to whatever the hidden list happened to contain -- a one-row panel whenever the search had
            // been cleared -- and the fixed area's own height would no longer match it.
            var actionsAreaHeight = InlineCardMetrics.ResultsAreaHeight(InlineCardMetrics.DefaultRows);
            _window.LstActions.Height = double.NaN;
            _window.ResultsPanelControl.Height = actionsAreaHeight;

            // No skeleton slots behind the actions panel: the placeholder rows are for results still
            // arriving, and the panel is not a half-full result list.
            _window.UpdatePlaceholderSlots();
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
                                                              SetPathBannerVisible(false);
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

                                                          if (shouldShow)
                                                          {
                                                              _window.PathPreviewTextBlock.Text = isShowMore ? activeResult.Name : ViewModels.Search.SearchResultHelper.FormatWslPath(activeResult.FullPath);
                                                          }

                                                          SetPathBannerVisible(shouldShow);
                                                      }), DispatcherPriority.Loaded);

    // The single place that changes the banner's visibility, because the card's height is computed from it.
    // The banner is ADDED to the card's height, so showing it has to re-size the window shell -- not just
    // re-run the list layout. Leaving the individual call sites to set Visibility themselves is exactly how
    // it ended up taking its height out of the results area instead, squeezing the list by half a row.
    private void SetPathBannerVisible(bool visible)
    {
        var border = _window.PathPreviewBorder;
        if (border == null) return;

        var target = visible ? Visibility.Visible : Visibility.Collapsed;
        if (border.Visibility == target) return;

        border.Visibility = target;
        // Deferred, like every other height change: ApplyCardHeight runs UpdateLayout synchronously, and
        // this is reached from a selection change that can land while the list's items are being
        // reconciled. See InlineCardSizingSupport.RequestCardHeight.
        _window.CardSizing.RequestCardHeight();
    }

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
