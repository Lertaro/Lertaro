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
    public static void AttachTo(HookIpcClient hookClient, Dispatcher dispatcher)
    {
        hookClient.OnMouseDoubleClick += (x, y) => HandleDoubleClick(dispatcher, x, y);
        hookClient.OnMouseMiddleClick += (x, y) => HandleMiddleClick(dispatcher, x, y);
    }

    private static void HandleDoubleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnDoubleClick) return;
        if (InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
        var trk = InlineSearchManager.Instance.ExplorerTracker;
        var proc = AppNativeHelper.GetProcessNameOfWindow(trk.ActiveHwnd);
        var cls = AppNativeHelper.GetClassNameOfWindow(trk.ActiveHwnd);
        var hwnd = trk.ActiveHwnd;
        var isDesktop = trk.IsDesktop;
        ShellThread.Run("QuickNavigationGate", () =>
        {
            if (QuickNavigationTriggerGate.CanShow(hwnd, proc, cls, isDesktop, x, y, MouseTriggerType.DoubleClick))
                dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        });
    }

    private static void HandleMiddleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnMiddleClick) return;
        if (InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
        var trk = InlineSearchManager.Instance.ExplorerTracker;
        var proc = AppNativeHelper.GetProcessNameOfWindow(trk.ActiveHwnd);
        var cls = AppNativeHelper.GetClassNameOfWindow(trk.ActiveHwnd);
        var hwnd = trk.ActiveHwnd;
        var isDesktop = trk.IsDesktop;
        ShellThread.Run("QuickNavigationGate", () =>
        {
            if (QuickNavigationTriggerGate.CanShow(hwnd, proc, cls, isDesktop, x, y, MouseTriggerType.MiddleClick)
                || FileDialogQuickNavGate.CanShow(hwnd, proc, cls, x, y))
                dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        });
    }
}
