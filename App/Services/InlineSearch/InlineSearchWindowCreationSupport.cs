using System.Windows.Threading;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.App.Services;

// Split out purely to keep InlineSearchManager under the repo's per-file line limit. This class owns
// window creation only and delegates the resulting state back to its one InlineSearchManager owner.
internal sealed class InlineSearchWindowCreationSupport
{
    private readonly InlineSearchManager _manager;

    public InlineSearchWindowCreationSupport(InlineSearchManager manager) => _manager = manager;

    public void EnsureWindowCreated()
    {
        PowerThrottlingHelper.WindowShowing("inline");
        // Cancels any idle trim armed by CloseInlineSearch: trimming just before a summon is strictly
        // worse than not trimming, since those pages are about to be needed again. Mirrors the quick
        // window's own WindowShowing call.
        IdleWorkingSetTrimmer.WindowShowing();
        if (_manager.Window != null) return;

        var tracker = _manager.ExplorerTracker;
        var viewModel = new QuickSearchViewModel();

        // Show from the mirror immediately. IsInlineWindowOnScreen below asks the Hook's paced poller
        // for a fresh path; its normal PathCaptured event repairs this scope and re-runs the current query.
        viewModel.SearchScope = tracker.ActivePath;
        viewModel.IsInlineSearchContext = true;

        var window = new InlineSearchWindow(viewModel, _manager);
        _manager.Window = window;
        _manager.CurrentHostHwnd = tracker.ActiveHwnd;
        _manager.KeyboardHook.IsInlineSearchVisible = true;
        _manager.KeyboardHook.IsInlineWindowOnScreen = true;
        _manager.MouseHook.Start();

        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        // Sized before it is shown, not from its Loaded handler like every later pass: the window is opaque
        // now, so the card's height has to be the window's height before the first frame or the frame that
        // goes up is the XAML's own 550 DIP with a small card pinned inside one edge of it -- a slab where
        // the transparent window used to show nothing. ApplyCardHeight is safe this early by its own
        // measurement comment (it charges the search bar and banner at their natural sizes until WPF has
        // arranged them).
        window.CardSizing.ApplyCardHeight();
        window.Positioner.PositionWindowImmediate();
        window.Show();
        window.ViewModel.EnsureServiceMonitoringActive();

        var foreground = ExplorerNativeHooks.GetForegroundWindow();
        var isTextInputFocused = foreground != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(foreground);
        if (!isTextInputFocused && !tracker.IsActiveWindowDialog)
        {
            if (window.ActivateAndFocusSearchBox())
            {
                _manager.KeyboardHook.IsInlineSearchVisible = false;
                _manager.KeyboardHook.Stop();
            }
            else
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_manager.Window != window || !window.IsVisible) return;
                    if (window.ActivateAndFocusSearchBox())
                    {
                        _manager.KeyboardHook.IsInlineSearchVisible = false;
                        _manager.KeyboardHook.Stop();
                    }
                }), DispatcherPriority.Input);
            }
        }
        else
        {
            var dialogHwnd = tracker.ActiveHwnd;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dialogHwnd == IntPtr.Zero) return;
                ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                var editBox = ExplorerNativeHooks.FindSubEditBox(dialogHwnd);
                if (editBox != IntPtr.Zero) ExplorerNativeHooks.SetFocus(editBox);
            }), DispatcherPriority.Input);
        }

        Logger.Log($"[InlineSearchManager] Created and shown new InlineSearchWindow. Scope: {viewModel.SearchScope}", LogLevel.Debug);
    }
}
