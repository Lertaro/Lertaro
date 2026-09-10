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
        if (_manager.Window != null) return;

        var tracker = _manager.ExplorerTracker;
        var viewModel = new QuickSearchViewModel();
        var scope = tracker.ActivePath;
        if (string.IsNullOrEmpty(scope) && tracker.ActiveHwnd != IntPtr.Zero)
        {
            // ActiveInlineAdapter is null for a plain IFileDialogAdapter host, so use the dialog adapter
            // when a recreated window needs a scope before the next poller cycle arrives.
            if (tracker.ActiveInlineAdapter != null)
                scope = tracker.ActiveInlineAdapter.GetSearchScope(tracker.ActiveHwnd);
            else if (tracker.ActiveAdapter != null)
                scope = tracker.ActiveAdapter.GetCurrentPath(tracker.ActiveHwnd);
        }
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
