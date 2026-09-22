using System.Windows;
using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Owns the inline card's geometry: how many rows it shows and how tall the window shell has to be.
//
// The card is sized from settled content only. While a search is running, its current result area is
// frozen and intermediate snapshots cannot expand or shrink the window. Once the search settles, the card
// is trimmed to the rows it is actually showing. This covers completed searches, empty results, and the
// empty-query startup panel without exposing a transient full-budget layout.
//
// Split out of InlineSearchWindow to keep that file under the repo's per-file line limit; it holds only
// the pulse animation and always operates on the one window it is given.
internal sealed class InlineCardSizingSupport
{
    // The window's own transparent margin around the card (MainBorder's Margin in the XAML).
    private const double CardMargin = 12;

    private readonly Lertaro.App.InlineSearchWindow _window;
    private int? _searchAreaRows;

    // The row budget that currently fits on screen: never more than the Ctrl+1..9 range, and never more than
    // the space this card actually has (see InlineCardSpace.AvailableHeight). Starts at the full budget so the
    // first sizing pass, which runs before any screen query has happened, behaves exactly as it always did.
    private int _rowBudget = InlineCardMetrics.DefaultRows;

    internal InlineCardSizingSupport(Lertaro.App.InlineSearchWindow window) => _window = window;

    /// <summary>The row budget in force right now: what the results area, and the actions area, size to.</summary>
    internal int RowBudget => _rowBudget;

    /// <summary>Re-applies the card's size once the window has been laid out at least once.</summary>
    internal void Attach()
    {
        _window.Loaded += (_, _) => ApplyCardHeight();

        // Re-derive the height whenever the SHAPE of what the card shows changes: the search state and which
        // rows are actually bound.
        //
        // The collection matters even though the state does not change, because not every content change
        // goes through a state transition. Typing "*" is the clear example: it puts a "keep typing" prompt
        // row on screen via ClearForTokenOnlyQuery, which sets IsSearching to false when it is often
        // ALREADY false -- no notification -- so nothing would re-size the shell and the extra row pushes the
        // search box past the window's bottom edge (visible as the search box being clipped). That made the
        // bug intermittent: it depended on whether a search had been running an instant earlier.
        //
        // Both go through the deferred RequestCardHeight, never applied inline: see its own comment for why
        // sizing from inside a collection notification used to crash the app.
        _window.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.Search.QuickSearchViewModel.IsSearching))
            {
                if (_window.ViewModel.IsSearching)
                    FreezeCurrentResultsArea();
                else
                    _searchAreaRows = null;
                RequestCardHeight();
            }
        };
        _window.ViewModel.Results.CollectionChanged += (_, _) => RequestCardHeight();
    }

    // Coalesced, deferred ApplyCardHeight -- see Attach's comment for why it must not run inline.
    private bool _heightRequestQueued;

    internal void RequestCardHeight()
    {
        if (_heightRequestQueued) return;
        _heightRequestQueued = true;

        // Background, not Render: Render still runs within the same layout cycle, which is the cycle a
        // synchronous UpdateLayout must not re-enter. Background is queued behind it, so the collection and
        // the layout both settle first -- the same reasoning behind the window's own deferred layout.
        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            _heightRequestQueued = false;
            if (_window.IsVisible)
                ApplyCardHeight();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>What the results area should show right now, from the result list's shape.</summary>
    /// <remarks>
    /// Derived from the collection's actual contents (which items are section titles) rather than from
    /// counts, because the titles are not always present and which of them show decides how many results
    /// fit. Both the card's height and the visible list height come from this one answer.
    /// </remarks>
    internal InlineCardMetrics.CardLayout CurrentLayout()
    {
        // Actions mode shows the ACTIONS panel in the results area, not the results list. That area is a
        // fixed row budget there (see InlineSearchWindowLayoutManager.UpdateActionsLayout), so the card must
        // be sized from the same budget -- deriving it from the results collection would size the card to a
        // list that is not even on screen.
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
            return new InlineCardMetrics.CardLayout(_rowBudget, _rowBudget);

        var results = _window.ViewModel.Results;
        var layout = ComputeResultLayout(results, isSearching: _window.ViewModel.IsSearching);
        if (!_window.ViewModel.IsSearching)
        {
            _searchAreaRows = null;
            return layout;
        }

        // Do not expose the full search budget to the visual tree while a query is in flight. The old
        // result area remains allocated until the final snapshot arrives; otherwise WPF first arranges
        // nine rows and then immediately arranges the smaller settled result set, which is the visible
        // large-list flash when deleting a no-results query.
        var areaRows = _searchAreaRows ?? layout.AreaRows;
        return new InlineCardMetrics.CardLayout(Math.Min(layout.ShownItems, areaRows), areaRows);
    }

    private void FreezeCurrentResultsArea()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            _searchAreaRows = _rowBudget;
            return;
        }

        _searchAreaRows = ComputeResultLayout(_window.ViewModel.Results, isSearching: false).AreaRows;
    }

    private InlineCardMetrics.CardLayout ComputeResultLayout(
        IReadOnlyList<AppSearchResult> results,
        bool isSearching)
    {
        var isHeader = new bool[results.Count];
        for (var i = 0; i < results.Count; i++)
            isHeader[i] = results[i].IsSearchSectionHeader;

        return InlineCardMetrics.ComputeLayout(isHeader, isSearching, _rowBudget);
    }

    /// <summary>Sizes the window shell to the settled row count.</summary>
    /// <remarks>
    /// The shell must be at least as tall as the card, and the card is bottom-anchored, so sizing the shell
    /// is what lets a taller card extend upward within it (the positioner pins the card's bottom edge; see
    /// InlineSearchWindowPositioner). The results area's own height belongs to
    /// InlineSearchWindowLayoutManager, which is re-queued rather than duplicated here.
    /// </remarks>
    internal void ApplyCardHeight()
    {
        // Searching produces intermediate result snapshots. Their full-budget layout is not a stable size,
        // so wait for IsSearching=false and resize once from the settled result set.
        if (_window.ViewModel.IsSearching) return;

        // Re-derived on every size pass, before anything is measured from it: the screen, its DPI and the
        // window this card is docked to can all change while it is open (the dialog is dragged to a smaller
        // monitor, the window shrink-wraps), and the budget the previous pass used would then be the wrong one.
        RefreshRowBudget();

        // Card plus its margin on both sides, plus a further margin so the drop shadow above the card is
        // not clipped by the window bounds.
        var shellHeight = CardHeight(CurrentLayout().AreaRows) + (CardMargin * 3);
        if (Math.Abs(_window.Height - shellHeight) <= 0.5)
        {
            // Nothing to resize. This early return is what keeps a streaming search cheap: the results
            // collection is replaced on every paint, so without it each keystroke would re-run the list
            // layout below (and, had the height differed, a synchronous UpdateLayout plus a native window
            // move) for a card that is already the right size -- which is most of the time, because the
            // height only depends on how many rows fit, not on which rows they are.
            return;
        }

        _window.Height = shellHeight;

        // Reposition synchronously, and here only: a height change moves the bottom edge (WPF anchors a
        // resize at the top-left) and it is the positioner that pins it back, so the two have to land
        // together. PositionWindowImmediate applies the new Height and then corrects the position in one
        // native call with no message pump in between, whereas the queued PositionWindow defers to the next
        // Render pass and leaves a frame where the card has resized but not yet been re-anchored -- which is
        // the upward snap.
        _window.Positioner.PositionWindowImmediate();

        _window.InputHandler.QueueResultsLayoutUpdate();
    }

    /// <summary>The card's height: stable result/path reserves, natural content, and chrome.</summary>
    /// <remarks>
    /// Separated and taking the row count so the arithmetic is testable without a laid-out window. The
    /// The search bar and path banner are measured when WPF has not produced their arranged heights yet.
    ///
    /// When result content or the path preview is visible, the shell reserves the full result budget and a
    /// five-line path estimate. This keeps the bottom-anchored search bar and native window position stable
    /// while content is changing. The path banner is still measured naturally, so a path longer than the
    /// estimate grows the shell instead of being clipped.
    /// </remarks>
    internal double CardHeight(int rows)
    {
        var hasVisibleContent = HasVisibleContent;
        if (hasVisibleContent)
            rows = Math.Max(rows, _rowBudget);

        return InlineCardMetrics.ResultsAreaHeight(rows) + ChromeHeight(hasVisibleContent, _reservePathPreview);
    }

    // Everything the card spends that is not a result row: the search bar, the separator and the path banner.
    // Shared with RefreshRowBudget, so how much of the available height the rows get and how tall the card
    // ends up cannot disagree.
    private double ChromeHeight(bool hasVisibleContent, bool reservePathPreview)
    {
        var separator = _window.ResultsSeparator.ActualHeight > 0 ? _window.ResultsSeparator.ActualHeight : 1.0;
        var pathHeight = PathBannerHeight();
        if (hasVisibleContent && reservePathPreview)
            pathHeight = Math.Max(pathHeight, EstimatedPathPreviewHeight());

        return SearchBoxHeight() + separator + pathHeight;
    }

    // Whether the shell keeps paying for the path preview estimate. Decided along with the row budget, which
    // is the pass that learns how much space there actually is.
    private bool _reservePathPreview = true;

    private bool HasVisibleContent =>
        _window.ResultsPanelControl.Visibility == Visibility.Visible
        || _window.PathPreviewBorder.Visibility == Visibility.Visible;

    /// <summary>Recomputes the rows that fit from the space the card has right now.</summary>
    private void RefreshRowBudget()
    {
        // The window's own transparent margin counts against that space as well: it is the whole shell (card
        // plus margins) that has to fit on the screen, not the card alone.
        var margin = CardMargin * 3;
        var available = InlineCardSpace.AvailableHeight(_window);
        var hasContent = HasVisibleContent;

        // Judged against the cost WITH the reserve always, never against whatever the last pass settled on:
        // reading the flag here instead would let the answer decide its own input and oscillate between two
        // sizes across passes. Once two rows no longer fit beside the five-line estimate, the estimate is
        // what is in the way, and the card becomes its rows plus the search bar.
        var chromeWithReserve = ChromeHeight(hasContent, reservePathPreview: true) + margin;
        _reservePathPreview = InlineCardMetrics.CanAffordPathReserve(available, chromeWithReserve, UiMetrics.InlineRowHeight);

        _rowBudget = InlineCardMetrics.ComputeRowBudget(
            available, ChromeHeight(hasContent, _reservePathPreview) + margin, UiMetrics.InlineRowHeight);
    }

    private double EstimatedPathPreviewHeight()
    {
        var lineHeight = _window.PathPreviewTextBlock.LineHeight;
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
            lineHeight = _window.PathPreviewTextBlock.FontSize;

        var padding = _window.PathPreviewBorder.Padding.Top + _window.PathPreviewBorder.Padding.Bottom;
        var border = _window.PathPreviewBorder.BorderThickness.Top + _window.PathPreviewBorder.BorderThickness.Bottom;
        return lineHeight * InlineCardMetrics.PathPreviewReservedRows + padding + border;
    }

    /// <summary>Measures the search bar at its natural height for the current card width.</summary>
    internal double SearchBoxHeight()
    {
        var width = _window.SearchBoxBorder.ActualWidth;
        if (width <= 0)
            width = Math.Max(0, _window.Width - (CardMargin * 2));

        _window.SearchBoxBorder.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        return _window.SearchBoxBorder.DesiredSize.Height;
    }

    /// <summary>The natural path banner height when it is actually shown, zero when it is not.</summary>
    /// <remarks>
    /// The path text is intentionally not truncated. Measure it with the card's content width when WPF has
    /// not produced ActualHeight yet, so the card can grow enough to display every wrapped line.
    /// </remarks>
    private double PathBannerHeight()
    {
        if (_window.PathPreviewBorder.Visibility != Visibility.Visible) return 0;

        var width = _window.PathPreviewBorder.ActualWidth;
        if (width <= 0)
            width = Math.Max(0, _window.Width - (CardMargin * 2));

        // ActualHeight can still belong to the previous path after TextBlock.Text changes. Re-measuring
        // every time makes the height calculation use the current wrapped content before the window is
        // resized, so the bottom-anchored search bar never gets a transiently compressed allocation.
        _window.PathPreviewBorder.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        return _window.PathPreviewBorder.DesiredSize.Height;
    }
}
