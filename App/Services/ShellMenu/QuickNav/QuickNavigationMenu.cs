using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Separator = System.Windows.Controls.Separator;
using Application = System.Windows.Application;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

public static class QuickNavigationMenu
{
    public static bool IsShowingShellMenu { get; set; }

    // Bumped once per Show() call so a menu's own Closed handler can tell whether a NEWER Show() has
    // already started by the time it runs -- see that handler's own comment for the empty-submenu bug
    // this exists to fix.
    private static int _sessionGeneration;

    public static void Show(int mouseX, int mouseY)
    {
        var generation = ++_sessionGeneration;
        var tracker = InlineSearchManager.Instance.ExplorerTracker;

        // Captured now, before anything below (the helper window grabbing foreground, the popup sitting
        // open while the user browses it) has a chance to perturb ExplorerTracker's state -- see
        // QuickNavTriggerContext's own comment for why re-reading the tracker live at click time is not safe.
        var trigger = new QuickNavTriggerContext(
            DialogHwnd: tracker.IsExplorerOrDesktopActive && tracker.IsActiveWindowDialog ? tracker.ActiveHwnd : IntPtr.Zero,
            ActiveHwnd: tracker.ActiveHwnd,
            ActiveAdapter: tracker.ActiveInlineAdapter,
            IsDesktop: tracker.IsDesktop);

        var path = tracker.ActivePath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _ = ShowAsync(mouseX, mouseY, generation, trigger, path);
    }

    private static async Task ShowAsync(int mouseX, int mouseY, int generation, QuickNavTriggerContext trigger, string path)
    {
        var hookClient = App.HookClient;
        if (hookClient?.IsConnected == true)
        {
            var snapshotArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<IReadOnlyList<string>> snapshotHandler = _ => snapshotArrived.TrySetResult();
            hookClient.OnOpenedFoldersCaptured += snapshotHandler;

            // The Explorer collector itself has a two-second COM safety cap. Waiting slightly longer
            // keeps every menu entry point consistent while still falling back if its response is lost.
            try
            {
                hookClient.SendMessage(new Core.Wire.IpcMessage { Id = Core.Wire.IpcMessageId.RequestOpenedFolders });
                await Task.WhenAny(snapshotArrived.Task, Task.Delay(2100));
            }
            finally
            {
                hookClient.OnOpenedFoldersCaptured -= snapshotHandler;
            }
        }

        if (generation != _sessionGeneration)
            return;

        var dummyResult = new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), IsDir = true };
        var contextMenu = new ContextMenu();
        contextMenu.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { contextMenu.IsOpen = false; e.Handled = true; } };

        foreach (var provider in PluginManager.Instance.QuickNavigationProviders)
        {
            if (!provider.CanProvide(dummyResult)) continue;
            provider.ClearSession();
            var providerItems = provider.GetMenuItems(dummyResult, IntPtr.Zero).ToList();
            if (providerItems.Count == 0) continue;

            // Shown even when this is the only active provider (by request) -- same "always label the
            // group, not just when there's more than one" convention the actions menu already follows.
            var headerAction = provider.HeaderAction;
            contextMenu.Items.Add(CreateGroupHeader(
                provider.GroupName,
                headerAction != null ? () => headerAction(dummyResult) : null,
                provider.HeaderActionTooltip,
                contextMenu));

            foreach (var item in providerItems)
                // Root entries are navigation categories (Favorites/History/configured folders/drives), so
                // don't attach the right-click action flyout here, and clicking/Enter must not execute or
                // navigate anywhere either -- only real files/folders in deeper levels do that.
                contextMenu.Items.Add(item.IsSeparator ? CreateSeparator() : CreateMenuItem(item, dummyResult, provider, contextMenu, trigger, enableRightClick: false, isRootItem: true));
        }

        if (contextMenu.Items.Count == 0) return;

        double dpiScaleX = 1.0, dpiScaleY = 1.0;
        var src = Application.Current.MainWindow != null ? PresentationSource.FromVisual(Application.Current.MainWindow) : null;
        if (src?.CompositionTarget != null)
        {
            dpiScaleX = src.CompositionTarget.TransformFromDevice.M11;
            dpiScaleY = src.CompositionTarget.TransformFromDevice.M22;
        }

        var helperWin = new MenuHelperWindow(mouseX * dpiScaleX, mouseY * dpiScaleY);
        helperWin.Deactivated += (s, e) => { if (!IsShowingShellMenu) contextMenu.IsOpen = false; };
        helperWin.Show();
        helperWin.Activate();

        var hwnd = new WindowInteropHelper(helperWin).Handle;
        // useAltTapBypass: false -- this call is triggered by a mouse click the Hook's own mouse hook just
        // processed, which already satisfies SetForegroundWindow's foreground-lock check on its own. See
        // ForceForeground's own comment for why simulating Alt here caused this popup to self-deactivate.
        if (hwnd != IntPtr.Zero) QuickSearchWindowController.ForceForeground(hwnd, useAltTapBypass: false);

        contextMenu.PlacementTarget = helperWin;
        contextMenu.Placement = PlacementMode.AbsolutePoint;
        contextMenu.HorizontalOffset = mouseX * dpiScaleX;
        contextMenu.VerticalOffset = mouseY * dpiScaleY;

        Action<int, int> clickOutsideHandler = (x, y) =>
        {
            if (!Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y))
                Application.Current.Dispatcher.BeginInvoke(() => contextMenu.IsOpen = false);
        };

        if (App.HookClient != null)
        {
            App.HookClient.OnMouseClick += clickOutsideHandler;
            App.HookClient.OnMouseDoubleClick += clickOutsideHandler;
            App.HookClient.OnMouseMiddleClick += clickOutsideHandler;
        }

        contextMenu.Closed += (s, e) =>
        {
            if (App.HookClient != null)
            {
                App.HookClient.OnMouseClick -= clickOutsideHandler;
                App.HookClient.OnMouseDoubleClick -= clickOutsideHandler;
                App.HookClient.OnMouseMiddleClick -= clickOutsideHandler;
            }
            helperWin.Close();

            // Every registered IQuickNavigationProvider/IDynamicActionProvider is a process-wide singleton
            // (PluginManager holds one shared instance, reused by every Show() call) -- ClearSession wipes
            // its handle->path lookup table, which the CURRENTLY OPEN menu's own submenu handles still
            // point into. Rapid re-triggering (e.g. several quick middle-clicks) can open a NEWER menu
            // before an OLDER one's Closed event has been delivered; when that stale event finally arrives
            // here and this ran unconditionally, it cleared the newer menu's still-live session data out
            // from under it, so hovering e.g. "This PC" resolved no path for its handle and rendered a
            // visibly empty submenu even though the menu itself was still open and otherwise fine. Only
            // clear when no NEWER Show() has started since this one did -- an older Closed event finding a
            // mismatch just skips cleanup this time, which is harmless (the next real Show() clears these
            // same lightweight dictionaries at its own start anyway, see the ClearSession call above).
            //
            // Release everything the menu pulled in so memory falls back immediately on close: dispose the
            // shell COM sessions (they own the native HMENU/HBITMAPs), drop the icon cache, then return
            // the freed pages to the OS. Deferred + off the UI thread so WPF first tears down the menu
            // visual tree (matching QuickSearch's hide path); otherwise the GC still sees it referenced.
            if (generation == _sessionGeneration)
            {
                foreach (var provider in PluginManager.Instance.QuickNavigationProviders) provider.ClearSession();
                foreach (var provider in PluginManager.Instance.DynamicActionProviders) provider.ClearSession();
                _ = Task.Delay(100).ContinueWith(_ =>
                {
                    try { ShellIconHelper.ClearCache(); } catch { }
                    try { Core.Win32Api.TrimWorkingSet(); } catch { }
                });
            }
        };

        contextMenu.Opened += (s, e) => contextMenu.Focus();
        contextMenu.IsOpen = true;
    }

    // Thin forwarders to QuickNavigationMenuContentExtensions (split out to keep this file under the
    // project's 300-line limit -- see that file's own header comment for what moved and why). Kept here,
    // rather than having callers reach into the extensions class directly, because QuickNavigationSubMenuLoader
    // already calls these two by this exact name (QuickNavigationMenu.CreateSeparator / .CreateMenuItem).
    internal static Separator CreateSeparator()
    {
        var separator = QuickNavigationMenuContentExtensions.CreateSeparator();
        separator.Loaded += (_, _) => separator.Height = 1 / System.Windows.Media.VisualTreeHelper.GetDpi(separator).DpiScaleY;
        return separator;
    }

    internal static MenuItem CreateGroupHeader(string groupName, Action? headerAction, string? headerActionTooltip, ContextMenu contextMenu) =>
        QuickNavigationMenuContentExtensions.CreateGroupHeader(groupName, headerAction, headerActionTooltip, contextMenu);

    internal static MenuItem CreateMenuItem(DynamicMenuItem item, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger, bool enableRightClick = true, bool isRootItem = false) =>
        QuickNavigationMenuContentExtensions.CreateMenuItem(item, result, provider, contextMenu, trigger, enableRightClick, isRootItem);

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T p) return p;
            child = child is FrameworkContentElement fce ? fce.Parent : System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

}
