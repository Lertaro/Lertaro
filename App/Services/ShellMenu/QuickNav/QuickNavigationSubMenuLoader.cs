using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Application = System.Windows.Application;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.App.Services.Plugin;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Loads a quick-nav submenu's items in the background -- split out of QuickNavigationMenu purely to
// keep that file under the file-length limit.
internal static class QuickNavigationSubMenuLoader
{
    // A cloud-sync placeholder folder (or just a very large one) can make provider.GetMenuItems take
    // an unpredictably long time -- it used to run right here on the UI thread, freezing the entire
    // app (every window, not just this menu) for as long as that took. Now it runs on a background
    // task instead, so the app stays responsive; the tradeoff is this specific submenu keeps showing
    // the "Loading..." placeholder (added in QuickNavigationMenu.CreateMenuItem) for that same
    // duration, rather than popping open instantly with stale/incomplete data.
    public static void EnsureLoaded(MenuItem menuItem, ISearchResult result, DynamicMenuItem item, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger)
    {
        // MouseEnter/GotKeyboardFocus/SubmenuOpened can all fire for the same item in quick succession;
        // Tag doubles as a synchronous "already loading or already loaded" guard so only the first one
        // actually kicks off a background fetch -- the others just see it's already in flight and no-op.
        if (menuItem.Tag is string) return;
        menuItem.Tag = "loading";

        Task.Run(() =>
        {
            var subItems = PluginPerformanceMonitor.Measure(provider,
                () => provider.GetMenuItems(result, item.SubMenuHandle)?.ToList() ?? new List<DynamicMenuItem>());
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // The whole quick-nav popup (or just this submenu's owning menu) may have already been
                // closed/torn down by the time this background fetch finishes -- updating a detached
                // MenuItem is harmless, but there's no reason to do the work.
                if (!contextMenu.IsOpen) return;

                menuItem.Tag = "loaded";
                var continuation = subItems.LastOrDefault(subItem => subItem.IsContinuation);
                menuItem.Items.Clear();
                AddItems(menuItem, subItems.Where(subItem => !subItem.IsContinuation), result, provider, contextMenu, trigger);

                // A provider can legitimately return nothing here -- e.g. its backing data (favorites, a
                // plugin's own cache) hasn't finished loading yet this soon after app startup, not just
                // "this folder is empty". Applies at every level (root included), since this is the one
                // shared load path every submenu -- root or nested -- goes through. Without this, the
                // popup opens with zero items: a near-invisible, oddly-sized "bubble" instead of a
                // normal-looking submenu.
                if (menuItem.Items.Count == 0)
                    menuItem.Items.Add(new MenuItem { Header = TranslationService.Get("QuickNav_EmptySubmenu"), IsEnabled = false });

                if (continuation != null)
                    AttachContinuationLoader(menuItem, continuation, result, provider, contextMenu, trigger);

                // The popup has already materialized the original Loading row. A layout pass refreshes
                // it with the new items without toggling IsSubmenuOpen, which would visibly flash the
                // entire cascading branch while it closes and reopens.
                RefreshSubmenuLayout(menuItem);
            }));
        });
    }

    private static void AddItems(MenuItem menuItem, IEnumerable<DynamicMenuItem> items, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger)
    {
        foreach (var subItem in items)
            // Root items already do this IsSeparator check (see QuickNavigationMenu.Show) -- missing
            // here meant a separator nested inside any submenu rendered as a real MenuItem with an
            // empty Header instead of an actual divider line, showing up as a blank row.
            menuItem.Items.Add(subItem.IsSeparator ? QuickNavigationMenu.CreateSeparator() : QuickNavigationMenu.CreateMenuItem(subItem, result, provider, contextMenu, trigger));
    }

    private static void AttachContinuationLoader(MenuItem menuItem, DynamicMenuItem initialContinuation, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger) => Application.Current.Dispatcher.BeginInvoke(() =>
                                                                                                                                                                                                                                   {
                                                                                                                                                                                                                                       if (menuItem.Template.FindName("SubMenuScrollViewer", menuItem) is not ScrollViewer scrollViewer) return;

                                                                                                                                                                                                                                       var continuation = initialContinuation;
                                                                                                                                                                                                                                       var isLoading = false;
                                                                                                                                                                                                                                       scrollViewer.ScrollChanged += (_, _) =>
                                                                                                                                                                                                                                       {
                                                                                                                                                                                                                                           if (isLoading || continuation == null || scrollViewer.VerticalOffset + scrollViewer.ViewportHeight < scrollViewer.ExtentHeight - 96)
                                                                                                                                                                                                                                               return;

                                                                                                                                                                                                                                           var nextHandle = continuation.SubMenuHandle;
                                                                                                                                                                                                                                           isLoading = true;
                                                                                                                                                                                                                                           Task.Run(() => PluginPerformanceMonitor.Measure(provider,
                                                                                                                                                                                                                                               () => provider.GetMenuItems(result, nextHandle)?.ToList() ?? new List<DynamicMenuItem>()))
                                                                                                                                                                                                                                               .ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
                                                                                                                                                                                                                                               {
                                                                                                                                                                                                                                                   isLoading = false;
                                                                                                                                                                                                                                                   if (!contextMenu.IsOpen || task.IsFaulted || task.IsCanceled) return;

                                                                                                                                                                                                                                                   var nextItems = task.Result;
                                                                                                                                                                                                                                                   continuation = nextItems.LastOrDefault(nextItem => nextItem.IsContinuation);
                                                                                                                                                                                                                                                   AddItems(menuItem, nextItems.Where(nextItem => !nextItem.IsContinuation), result, provider, contextMenu, trigger);
                                                                                                                                                                                                                                               }), TaskScheduler.Default);
                                                                                                                                                                                                                                       };
                                                                                                                                                                                                                                   }, DispatcherPriority.Loaded);

    private static void RefreshSubmenuLayout(MenuItem menuItem) => Application.Current.Dispatcher.BeginInvoke(() =>
                                                                        {
                                                                            menuItem.InvalidateMeasure();
                                                                            menuItem.InvalidateArrange();
                                                                            menuItem.UpdateLayout();
                                                                        }, DispatcherPriority.Loaded);
}
