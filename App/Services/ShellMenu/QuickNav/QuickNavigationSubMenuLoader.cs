using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Application = System.Windows.Application;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
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
            var subItems = provider.GetMenuItems(result, item.SubMenuHandle).ToList();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // The whole quick-nav popup (or just this submenu's owning menu) may have already been
                // closed/torn down by the time this background fetch finishes -- updating a detached
                // MenuItem is harmless, but there's no reason to do the work.
                if (!contextMenu.IsOpen) return;

                menuItem.Tag = "loaded";
                menuItem.Items.Clear();
                foreach (var subItem in subItems)
                    // Root items already do this IsSeparator check (see QuickNavigationMenu.Show) --
                    // missing here meant a separator nested inside any submenu rendered as a real
                    // MenuItem with an empty Header instead of an actual divider line, showing up as a
                    // blank row.
                    menuItem.Items.Add(subItem.IsSeparator ? QuickNavigationMenu.CreateSeparator() : QuickNavigationMenu.CreateMenuItem(subItem, result, provider, contextMenu, trigger));

                // A provider can legitimately return nothing here -- e.g. its backing data (favorites, a
                // plugin's own cache) hasn't finished loading yet this soon after app startup, not just
                // "this folder is empty". Applies at every level (root included), since this is the one
                // shared load path every submenu -- root or nested -- goes through. Without this, the
                // popup opens with zero items: a near-invisible, oddly-sized "bubble" instead of a
                // normal-looking submenu.
                if (menuItem.Items.Count == 0)
                    menuItem.Items.Add(new MenuItem { Header = TranslationService.Get("QuickNav_EmptySubmenu"), IsEnabled = false });
            }));
        });
    }
}
