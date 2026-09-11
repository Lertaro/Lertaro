using System.Windows;
using System.Windows.Media.Animation;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Owns the inline card's geometry and its skeleton rows: how many rows it shows, how tall the window shell
// has to be, which slots are still unfilled, and the pulse that marks a search as still running.
//
// The card is sized in exactly two ways, and never from an intermediate state:
//
//   * While a search is RUNNING it shows the full row budget, with skeleton rows filling the part that
//     has not arrived yet. The size does not follow the partial result count -- a card that resized on
//     every interim paint is what made it feel jumpy, and the skeleton rows already communicate "still
//     coming", so there is nothing for a resize to say.
//   * When no search is running it is trimmed to the rows it is actually showing. This covers all the
//     settled states at once, including a completed search with nothing found and the empty-query
//     startup panel, neither of which is a "search finished" event that a timer could catch -- so the
//     size is derived from the state rather than from a transition into it.
//
// Split out of InlineSearchWindow to keep that file under the repo's per-file line limit; it holds only
// the pulse animation and always operates on the one window it is given.
internal sealed class InlineCardSizingSupport
{
    // The window's own transparent margin around the card (MainBorder's Margin in the XAML).
    private const double CardMargin = 12;

    private readonly Lertaro.App.InlineSearchWindow _window;
    private DoubleAnimation? _pulse;

    internal InlineCardSizingSupport(Lertaro.App.InlineSearchWindow window) => _window = window;

    /// <summary>Re-applies the card's size once the window has been laid out at least once.</summary>
    internal void Attach()
    {
        _window.Loaded += (_, _) => ApplyCardHeight();

        // Re-derive the height whenever the SHAPE of what the card shows changes: the search state (full row
        // budget while running, trimmed once not) and which rows are actually bound.
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
                RequestCardHeight();
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
    /// fit. Both the card's height and the list/skeleton split come from this one answer.
    /// </remarks>
    internal InlineCardMetrics.CardLayout CurrentLayout()
    {
        // Actions mode shows the ACTIONS panel in the results area, not the results list. That area is a
        // fixed row budget there (see InlineSearchWindowLayoutManager.UpdateActionsLayout), so the card must
        // be sized from the same budget -- deriving it from the results collection would size the card to a
        // list that is not even on screen.
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
            return new InlineCardMetrics.CardLayout(InlineCardMetrics.DefaultRows, InlineCardMetrics.DefaultRows);

        var results = _window.ViewModel.Results;
        var isHeader = new bool[results.Count];
        for (var i = 0; i < results.Count; i++)
            isHeader[i] = results[i].IsSearchSectionHeader;

        return InlineCardMetrics.ComputeLayout(isHeader, _window.ViewModel.IsSearching);
    }

    /// <summary>Rebuilds the skeleton rows that fill the card's not-yet-filled slots.</summary>
    internal void UpdatePlaceholderSlots()
    {
        var slots = CurrentLayout().UnfilledSlots;

        // Bound to nothing: every slot looks identical, so the list is rebuilt from a count. Rebuilt only
        // when that count changes, which keeps this off the per-paint path.
        if (_window.PlaceholderSlots.Items.Count != slots)
        {
            _window.PlaceholderSlots.Items.Clear();
            for (var i = 0; i < slots; i++)
                _window.PlaceholderSlots.Items.Add(i);
        }

        RefreshPlaceholderPulse();
    }

    // The breathing pulse is the "still searching" signal. One animation on the whole block rather than one
    // per row: every slot breathes together and the cost does not scale with the row count.
    internal void RefreshPlaceholderPulse()
    {
        var slots = _window.PlaceholderSlots;
        if (!_window.ViewModel.IsSearching)
        {
            slots.BeginAnimation(UIElement.OpacityProperty, null);
            slots.Opacity = 0.16;
            return;
        }

        _pulse ??= BuildPulse();
        slots.BeginAnimation(UIElement.OpacityProperty, _pulse);
    }

    private static DoubleAnimation BuildPulse() => new()
    {
        From = 0.10,
        To = 0.26,
        Duration = new Duration(TimeSpan.FromSeconds(1.1)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
    };

    /// <summary>Sizes the window shell to the current row count, then re-runs the list/skeleton split.</summary>
    /// <remarks>
    /// The shell must be at least as tall as the card, and the card is bottom-anchored, so sizing the shell
    /// is what lets a taller card extend upward within it (the positioner pins the card's bottom edge; see
    /// InlineSearchWindowPositioner). The results area's own height belongs to
    /// InlineSearchWindowLayoutManager, which is re-queued rather than duplicated here.
    /// </remarks>
    internal void ApplyCardHeight()
    {
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

    /// <summary>The card's height: the row area, the path banner when shown, and the chrome.</summary>
    /// <remarks>
    /// Separated and taking the row count so the arithmetic is testable without a laid-out window. The
    /// fallbacks cover being asked before the first measure pass, when ActualHeight is still 0.
    ///
    /// The banner is ADDED to the card rather than given room taken from the list. The results area is a
    /// star row inside the card, so an extra Auto row above it would shrink the list by the banner's height
    /// -- which showed up as the list losing half a row and the search box shifting down. Counting it here
    /// instead grows the card upward (it is bottom-anchored), leaving both where they were.
    /// </remarks>
    internal double CardHeight(int rows)
    {
        var searchBox = _window.SearchBoxBorder.ActualHeight > 0 ? _window.SearchBoxBorder.ActualHeight : 48.0;
        var separator = _window.ResultsSeparator.ActualHeight > 0 ? _window.ResultsSeparator.ActualHeight : 1.0;
        return InlineCardMetrics.ResultsAreaHeight(rows) + searchBox + separator + PathBannerHeight();
    }

    /// <summary>The path banner's height when it is actually shown, zero when it is not.</summary>
    /// <remarks>
    /// Reads the element's explicitly-set Height rather than its ActualHeight: the banner is Collapsed
    /// whenever it is not in use, and a Collapsed element measures zero, so the card's height has to be
    /// computable from the value it WILL render at, before it is shown.
    /// </remarks>
    private double PathBannerHeight()
    {
        if (_window.PathPreviewBorder.Visibility != Visibility.Visible) return 0;
        var height = _window.PathPreviewBorder.Height;
        return double.IsNaN(height) ? 0 : height;
    }
}
