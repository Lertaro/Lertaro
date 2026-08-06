using System.IO;
using System.Windows;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

// The keyboard-navigation half of QuickNavigationMenu.CreateMenuItem's PreviewKeyDown handler, split out
// to keep that file under the project's 300-line limit. Takes the same captured locals CreateMenuItem
// already has (itemPath, canNavigate, ...) as parameters rather than re-deriving anything.
internal static class QuickNavigationMenuKeyHandler
{
    public static void HandlePreviewKeyDown(
        KeyEventArgs e,
        MenuItem menuItem,
        DynamicMenuItem item,
        ContextMenu contextMenu,
        string? itemPath,
        bool canNavigate,
        bool enableRightClick,
        Action triggerAction)
    {
        // Action hotkeys (Ctrl+C, Ctrl+Enter, ...) fire directly on the highlighted item without
        // opening its action menu — like the full window's result list. Gated to real file/folder
        // items (same places the action menu is allowed), so nav categories don't respond.
        if (menuItem.IsFocused && enableRightClick && item.IsActionable && canNavigate && !string.IsNullOrEmpty(itemPath)
            && System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
        {
            var hotkeySelection = new[]
            {
                new AppSearchResult
                {
                    FullPath = itemPath!,
                    Name = Path.GetFileName(itemPath),
                    IsDir = item.HasSubMenu || Directory.Exists(itemPath),
                    ContextDirectory = Directory.Exists(itemPath) ? itemPath! : (Path.GetDirectoryName(itemPath) ?? string.Empty)
                }
            };
            var shim = new QuickNavShimView(() =>
            {
                contextMenu.IsOpen = false;
                (contextMenu.PlacementTarget as Window)?.Hide();
            });
            if (Helpers.HotkeyActionTrigger.TryExecute(e, hotkeySelection, shim, SearchWindowType.Main, hideOnRun: true))
            {
                e.Handled = true;
                return;
            }
        }

        if ((e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return) && menuItem.IsFocused)
        {
            e.Handled = true;
            triggerAction();
            return;
        }
        if (menuItem.IsFocused)
        {
            if (e.Key == System.Windows.Input.Key.Down)
            {
                if (NavigateToSibling(menuItem, forward: true)) { menuItem.IsSubmenuOpen = false; e.Handled = true; }
            }
            else if (e.Key == System.Windows.Input.Key.Up)
            {
                if (NavigateToSibling(menuItem, forward: false)) { menuItem.IsSubmenuOpen = false; e.Handled = true; }
            }
            else if (e.Key == System.Windows.Input.Key.Right && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
            {
                if (menuItem.Items.OfType<MenuItem>().All(c => !c.IsEnabled)) e.Handled = true;
                else
                {
                    var firstChild = menuItem.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable);
                    if (firstChild != null) { firstChild.Focus(); e.Handled = true; }
                }
            }
        }
        else if (menuItem.IsSubmenuOpen && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            if (System.Windows.Input.Keyboard.FocusedElement is MenuItem focused && menuItem.Items.Contains(focused))
            {
                if (e.Key == System.Windows.Input.Key.Left)
                {
                    menuItem.IsSubmenuOpen = false;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => menuItem.Focus()));
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up)
                {
                    var items = menuItem.Items.OfType<MenuItem>().Where(i => i.IsEnabled && i.Focusable).ToList();
                    var index = items.IndexOf(focused);
                    if (index != -1 && items.Count > 0)
                    {
                        var nextIndex = e.Key == System.Windows.Input.Key.Down ? (index + 1) % items.Count : (index - 1 + items.Count) % items.Count;
                        items[nextIndex].Focus();
                        e.Handled = true;
                    }
                }
            }
            else if ((e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down) && menuItem.Items.OfType<MenuItem>().All(c => !c.IsEnabled))
            {
                menuItem.IsSubmenuOpen = false;
                Application.Current.Dispatcher.BeginInvoke(new Action(() => menuItem.Focus()));
                e.Handled = true;
            }
        }
    }

    private static bool NavigateToSibling(MenuItem currentItem, bool forward)
    {
        var parent = System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(currentItem);
        var items = parent?.Items.OfType<MenuItem>().Where(i => i.IsEnabled && i.Focusable).ToList();
        var idx = items?.IndexOf(currentItem) ?? -1;
        if (idx == -1 || items == null || items.Count == 0) return false;
        var nextIdx = (idx + (forward ? 1 : -1) + items.Count) % items.Count;
        items[nextIdx].Focus();
        return true;
    }
}
