using System.Windows.Threading;
using Lertaro.App.Helpers.App;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.Core;
using Lertaro.Core.Hook.Ipc;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Split out from App.xaml.cs to keep that file under the repo's per-file line limit. These handlers
// move the Quick Navigation gate evaluation off the hook-event thread onto ShellThread (STA), then
// marshal only the resulting "show menu" call back to the WPF Dispatcher.
internal static class QuickNavigationHookHandlers
{
    private static int _triggerGeneration;

    public static void AttachTo(HookIpcClient hookClient, Dispatcher dispatcher)
    {
        hookClient.OnMouseDoubleClick += (x, y) => HandleDoubleClick(dispatcher, x, y);
        hookClient.OnMouseMiddleClick += (x, y) => HandleMiddleClick(dispatcher, x, y);
    }

    private static void HandleDoubleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnDoubleClick) return;
        QueueTrigger(dispatcher, x, y, MouseTriggerType.DoubleClick);
    }

    private static void HandleMiddleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnMiddleClick) return;
        QueueTrigger(dispatcher, x, y, MouseTriggerType.MiddleClick);
    }

    private static void QueueTrigger(Dispatcher dispatcher, int x, int y, MouseTriggerType triggerType)
    {
        var generation = Interlocked.Increment(ref _triggerGeneration);
        if (InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
        var trk = InlineSearchManager.Instance.ExplorerTracker;
        var hwnd = trk.ActiveHwnd;
        var path = trk.ActivePath;
        var proc = AppNativeHelper.GetProcessNameOfWindow(hwnd);
        var cls = AppNativeHelper.GetClassNameOfWindow(hwnd);
        var isDesktop = trk.IsDesktop;

        bool IsCurrent()
        {
            if (generation != Volatile.Read(ref _triggerGeneration)
                || !IsSameContext(hwnd, path, trk.ActiveHwnd, trk.ActivePath)) return false;
            var foreground = InlineSearchWindowNativeMethods.GetForegroundWindow();
            return foreground == hwnd || isDesktop && Core.Hook.ExplorerNativeHooks.IsDesktopWindow(foreground, out _);
        }

        ShellThread.Run("QuickNavigationGate", () =>
        {
            if (!IsCurrent()) return;
            if (QuickNavigationTriggerGate.CanShow(hwnd, proc, cls, isDesktop, x, y, triggerType)
                || triggerType == MouseTriggerType.MiddleClick && FileDialogQuickNavGate.CanShow(hwnd, proc, cls, x, y))
                dispatcher.BeginInvoke(() =>
                {
                    if (IsCurrent()) QuickNavigationMenu.Show(x, y, IsCurrent);
                });
        });
    }

    // A click belongs to the host and directory it was made in, not whichever view a late COM read finds.
    internal static bool IsSameContext(IntPtr expectedHwnd, string? expectedPath, IntPtr actualHwnd, string? actualPath)
        => expectedHwnd != IntPtr.Zero && expectedHwnd == actualHwnd
            && string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase);
}
