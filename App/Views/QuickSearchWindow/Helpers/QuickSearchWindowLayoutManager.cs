using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

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
            // The quick window's actions menu is an overlay now. Keep the result area at its existing
            // width, but expand its height to the normal nine-row budget so a short result set does not
            // make the floating menu unusably small.
            _window.LstActions.Height = double.NaN;
            ApplyActionsResultHeight();
            return;
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
            ApplyResultsLayoutProtected();
        }), DispatcherPriority.Normal);
    }

    // ApplyResultsLayout runs a synchronous layout pass, and a layout pass ends by walking the UI
    // Automation peer tree and raising property-changed events out of this process (every
    // ContextLayoutManager.UpdateLayout() calls fireAutomationEvents()). Once an accessibility client --
    // or any tool that attached to this window -- is listening, those outgoing calls can pump this
    // thread's message queue, dispatch a re-entrant WM_GETOBJECT, and block inside a second UIA round
    // trip; the window then sits there unresponsive while the user watches it. DisableProcessing is the
    // framework's own answer to exactly this shape: it stops CLR locks pumping messages internally and
    // refuses nested DispatcherFrames, which is what AutomationPeer's static constructor uses it for
    // ("Disable message processing to avoid re-entrancy (WM_GETOBJECT)"). While it is in force here, a
    // re-entrant WM_GETOBJECT cannot be dispatched, so it cannot nest inside the pass.
    //
    // Cost, accepted deliberately: if UIA does try to nest, that call throws instead of hanging, so this
    // swallows it and keeps one dropped layout instead of a frozen window. The queued flag is cleared
    // before this runs, so the next results update queues a fresh attempt and the geometry self-corrects.
    private void ApplyResultsLayoutProtected()
    {
        try
        {
            using (_window.Dispatcher.DisableProcessing())
            {
                ApplyResultsLayout();
            }
        }
        catch (InvalidOperationException ex)
        {
            Logger.Log($"[QuickSearchWindow] Deferred results layout was refused while dispatcher processing was disabled: {ex.Message}", LogLevel.Warn);
        }
    }

    // Runs the actual height computation immediately instead of deferring -- needed by
    // QuickSearchWindowController.ShowWindow, which forces its own synchronous UpdateLayout() right after
    // populating the startup panel, so this must run before that to avoid sizing the window to whatever
    // Height it was last left at. Safe to call synchronously there (unlike from Results.CollectionChanged
    // above) since it isn't itself running from inside that event's dispatch.
    public void ApplyResultsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            ApplyActionsResultHeight();
            return;
        }

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
        // they need refreshing on every call regardless of what's below -- but the forced layout pass is a
        // full window measure/arrange, and re-running it when the height hasn't actually moved (common
        // while typing: narrowing an already 9+-result query keeps summing to the same 9-row total) was
        // pure waste stacked on every keystroke's results update.
        UpdateShortcutHints();

        if (_window.LstResults.Height == resultsHeight && _window.ResultsPanelControl.Height == resultsHeight)
            return;

        _window.LstResults.Height = resultsHeight;
        _window.ResultsPanelControl.Height = resultsHeight;
        ForceLayoutSoTheWindowResizes();
    }

    private void ApplyActionsResultHeight()
    {
        var maxResultHeight = UiMetrics.ScaledQuickSearchMaxResultHeight;
        if (_window.LstResults.Height == maxResultHeight && _window.ResultsPanelControl.Height == maxResultHeight)
            return;

        _window.LstResults.Height = maxResultHeight;
        _window.ResultsPanelControl.Height = maxResultHeight;
        ForceLayoutSoTheWindowResizes();
    }

    // This used to spell "force layout" as a Manual -> WidthAndHeight SizeToContent round trip. That
    // toggle was only ever an indirect way to make WPF run the pending measure/arrange immediately
    // instead of letting the next paint pass pick it up; InlineSearchWindowLayoutManager does the same
    // job with a plain UpdateLayout(). Measured against the real window, an explicit UpdateLayout()
    // resizes it to the new height identically, and this window's content width is fixed (see
    // QuickSearchWindow.xaml), so dropping the WidthAndHeight half of the round trip cannot change the
    // width either way. Left as one named place so the sizing behavior is re-measurable in one spot if
    // the window's sizing model ever changes.
    private void ForceLayoutSoTheWindowResizes() => _window.UpdateLayout();

    public void UpdateShortcutHints() =>
        QuickSearchShortcutHelper.UpdateShortcutHints(_window, WpfUiHelper.GetScrollViewer(_window.LstResults));
}
