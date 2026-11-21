using System.Windows.Threading;
using Lertaro.App.Services;

using Lertaro.App.Services.ShellMenu.QuickNav;
namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Opens Quick Navigation from the inline search-box icon.
internal static class InlineSearchWindowQuickNavWiring
{
    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        // Left-clicking the search box's own logo opens Quick Navigation, but only when this window is
        // actually docked to a file picker dialog (matching the existing middle-click trigger's own gate
        // in FileDialogQuickNavGate) -- there's nothing useful to navigate to otherwise, and the hover hint
        // that promises this click would be a lie for the common case (docked to a plain Explorer
        // window/desktop). Checked once at construction: this window is a fresh instance per dock session,
        // not reused across different hosts, so the docked target can't change out from under this decision.
        if (window.Manager.ExplorerTracker.IsActiveWindowDialog)
        {
            // IsIconClickable/IsIconDraggable are the window's own (see its constructor): set for every host,
            // because the logo drags the card wherever it is docked. What is dialog-only is what clicking it
            // DOES, and the hint that says so.
            window.SearchBox.IconClickHint = TranslationManager.Instance["InlineSearch_QuickNavTooltip"];
            // IconLeftClicked supplies physical screen coordinates, as QuickNavigationMenu.Show expects.
            // Defer opening until the click handler returns, then restore the host dialog's foreground focus.
            // StandardFileDialogAdapter confirms navigation only if that dialog is still foreground about 300 ms later.
            //
            // ResetInlineSearchAndFocusDialog requests focus through the elevated Hook process asynchronously.
            // Wait for GetForegroundWindow to confirm the handoff before opening the popup; a late handoff would
            // deactivate and close it. Keep the inline window open, since closing it can cause the same focus race.
            window.SearchBox.IconLeftClicked += (x, y) =>
            {
                window.ResetInlineSearchAndFocusDialog();
                ShowQuickNavWhenDialogForeground(window, window.Manager.ExplorerTracker.ActiveHwnd, x, y);
            };
        }
    }

    // Mirrors QuickSearchWindowFocusHelper.FocusWhenForeground's own polling pattern for the
    // identical problem: a requested SetForegroundWindow (here, ResetInlineSearchAndFocusDialog's) doesn't
    // land synchronously with the call that asked for it. Same 10ms/200ms cadence -- if the deadline is
    // hit without the dialog ever reporting foreground (something else is holding it), still shows the
    // menu rather than silently doing nothing.
    private static void ShowQuickNavWhenDialogForeground(Lertaro.App.InlineSearchWindow window, IntPtr dialogHwnd, int x, int y)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, e) =>
        {
            var isForeground = dialogHwnd == IntPtr.Zero || InlineSearchWindowNativeMethods.GetForegroundWindow() == dialogHwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            QuickNavigationMenu.Show(x, y);
        };
        timer.Start();
    }
}
