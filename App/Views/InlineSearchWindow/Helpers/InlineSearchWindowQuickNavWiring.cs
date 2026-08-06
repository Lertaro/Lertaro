using System.Windows.Threading;
using Lertaro.App.Services;

using Lertaro.App.Services.ShellMenu.QuickNav;
namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Wires up the search box's icon-click-to-open-Quick-Navigation behavior -- split out of
// InlineSearchWindow's constructor to keep that file under the line-count limit.
internal static class InlineSearchWindowQuickNavWiring
{
    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        // Left-clicking the search box's own logo opens Quick Navigation, but only when this window is
        // actually docked to a file picker dialog (matching the existing middle-click trigger's own gate
        // in FileDialogQuickNavGate) -- there's nothing useful to navigate to otherwise, and an always-on
        // hover hint here would be a lie for the common case (docked to a plain Explorer window/desktop).
        // Checked once at construction: this window is a fresh instance per dock session, not reused
        // across different hosts, so the docked target can't change out from under this decision.
        if (window.Manager.ExplorerTracker.IsActiveWindowDialog)
        {
            window.SearchBox.IsIconClickable = true;
            window.SearchBox.IconClickHint = TranslationManager.Instance["InlineSearch_QuickNavTooltip"];
            // Screen coordinates (physical pixels) come straight from IconLeftClicked, already in the
            // same convention QuickNavigationMenu.Show's other callers (the global mouse hook in
            // App.xaml.cs) use it in. Deferred via BeginInvoke to match those callers exactly (Show()
            // force-foregrounds a helper window as its very first move, which reads oddly from right
            // inside this click's own MouseLeftButtonUp handler).
            //
            // Restoring the dialog's own focus FIRST matters for a reason that isn't about this popup at
            // all: picking an item calls StandardFileDialogAdapter.NavigateTo, which sets the target path
            // but only actually confirms it (Enter + click into the address edit) if the dialog is back to
            // being the real foreground window ~300ms later (see that adapter's own isAllowed check) -- a
            // safety gate against confirming navigation on the wrong window if focus drifted elsewhere
            // during that wait. When this menu is triggered from inside the dialog itself (the existing
            // middle-click path), the dialog never lost foreground in the first place, so that gate is
            // always satisfied. Triggered from THIS window's own logo, the dialog had already lost
            // foreground to this window before the click even happened.
            //
            // ResetInlineSearchAndFocusDialog is the SAME mechanism Escape already uses to hand focus back
            // to the dialog without closing this window (grants the elevated Hook service one-shot
            // permission to call SetForegroundWindow past the system's foreground-lock, then asks it to).
            // Closing this window outright was tried first and made the popup vanish immediately instead:
            // whatever WPF/Win32 teardown a Window.Close() kicks off wasn't done by the time Show() went to
            // foreground its own helper window a moment later, and the new popup's Deactivated handler read
            // that as a click-away and closed it. Leaving this window open and just handing focus off does
            // not have that race -- but ResetInlineSearchAndFocusDialog's own SetForegroundWindow call
            // ALSO doesn't complete synchronously with the IPC call that requested it (round-trips to the
            // elevated Hook process): calling Show() right after via a single BeginInvoke hop was a coin
            // flip between "the dialog reclaimed foreground before Show() ran, so the popup was unaffected"
            // and "the dialog's foreground change lands AFTER Show()'s popup already opened, deactivating
            // it exactly like a real click-away would." Polling GetForegroundWindow() for real confirmation
            // first (the same pattern QuickSearchWindowController.FocusSearchBoxWhenForeground already
            // uses for the identical async-foreground-change problem) removes that race instead of hoping
            // one dispatcher hop is enough of a head start.
            window.SearchBox.IconLeftClicked += (x, y) =>
            {
                window.ResetInlineSearchAndFocusDialog();
                ShowQuickNavWhenDialogForeground(window, window.Manager.ExplorerTracker.ActiveHwnd, x, y);
            };
        }
    }

    // Mirrors QuickSearchWindowController.FocusSearchBoxWhenForeground's own polling pattern for the
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
