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

        // The window this card is summoned over. The mirrored ActivePath is taken as it stands, and the
        // accurate answer is asked for afterwards through the tracker's paced channel (RequestInlineScopeAsync
        // below): that mirror moves on the hook's activation event, while the card is created in the very
        // instant the host is made foreground, so it can still describe the PREVIOUS window -- measured on a
        // lister sitting in "D:\Projects\音乐" as "D:\Projects", and at other times as "D:\" or a drive root.
        // Reading the adapter right here was the previous answer to that, and it put a cross-process host read
        // on this (UI) thread for EVERY summon, before the window is even shown: Directory Opus answers such a
        // read with a bare SendMessage(WM_GETTEXT) and no SMTO_ABORTIFHUNG, so a host that stops answering
        // hangs the card, and the read also bypassed the pacing the poller uses. The late answer arrives via
        // UpdatePath -- the same OnPathCaptured path a polled answer takes -- and the manager re-runs the
        // search with it, so the first keystroke still points at the right folder.
        var scope = tracker.ActivePath;

        viewModel.SearchScope = scope;
        viewModel.IsInlineSearchContext = true;

        var window = new InlineSearchWindow(viewModel, _manager);
        _manager.Window = window;
        _manager.CurrentHostHwnd = tracker.ActiveHwnd;
        _manager.KeyboardHook.IsInlineSearchVisible = true;
        _manager.KeyboardHook.IsInlineWindowOnScreen = true;
        _manager.MouseHook.Start();

        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        window.Positioner.PositionWindowImmediate();
        window.Show();
        window.ViewModel.EnsureServiceMonitoringActive();

        // Ask for the accurate scope through the paced channel, and publish it the way a polled answer is
        // published. The floor may refuse this read (it can be inside its interval for this host); the poller's
        // own cycle then delivers the same answer, so nothing here is load-bearing for correctness.
        var summonHwnd = tracker.ActiveHwnd;
        if (summonHwnd != IntPtr.Zero)
        {
            _ = tracker.RequestInlineScopeAsync(summonHwnd).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && !string.IsNullOrEmpty(task.Result))
                    tracker.UpdatePath(task.Result, false);
            }, TaskScheduler.Default);
        }

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
