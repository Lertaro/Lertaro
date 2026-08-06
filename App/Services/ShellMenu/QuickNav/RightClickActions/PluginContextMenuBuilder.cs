using System.Windows;
using MenuItem = System.Windows.Controls.MenuItem;
using ItemsControl = System.Windows.Controls.ItemsControl;

namespace Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;

public static class PluginContextMenuBuilder
{
    public static (DependencyObject parent, List<MenuItem> items, int highlightedIndex) GetActiveMenuState(DependencyObject root, DependencyPropertyKey? key)
    {
        var current = root;
        while (true)
        {
            var items = new List<MenuItem>();
            if (current is ItemsControl ic)
                items.AddRange(ic.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled));
            var openSub = items.FirstOrDefault(mi => mi.IsSubmenuOpen);
            if (openSub != null) { current = openSub; continue; }
            var idx = -1;
            for (var i = 0; i < items.Count; i++)
                if (key != null && (bool)items[i].GetValue(key.DependencyProperty)) { idx = i; break; }
            return (current, items, idx);
        }
    }
}
