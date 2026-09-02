using System.Windows;
using System.Windows.Interop;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using MenuItem = System.Windows.Controls.MenuItem;
using Application = System.Windows.Application;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

/// <summary>
/// Shared content + execution core for the action flyout, used by both the quick-nav right-click menu
/// (<see cref="PluginContextMenuHelper"/>) and the full-window <see cref="ActionFlyout"/>. Keeping the
/// item rendering and dispatch here guarantees the two hosts never drift; each host only owns its own
/// popup placement, keyboard and close plumbing.
/// </summary>
internal static class ActionFlyoutItems
{
    // MenuItem.IsHighlighted is set (by WPF on hover, by the hosts' keyboard nav via reflection) and the
    // flyout style highlights on it. Cached once; used by both hosts to move the keyboard highlight.
    private static DependencyPropertyKey? _isHighlightedKey;
    private static bool _isHighlightedKeyResolved;

    public static DependencyPropertyKey? IsHighlightedKey
    {
        get
        {
            if (_isHighlightedKeyResolved) return _isHighlightedKey;
            _isHighlightedKeyResolved = true;
            var keyField = typeof(MenuItem).GetField("IsHighlightedPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            _isHighlightedKey = keyField?.GetValue(null) as DependencyPropertyKey;
            return _isHighlightedKey;
        }
    }

    /// <summary>
    /// Renders the finalized action items into <paramref name="menu"/> (headers/separators as
    /// non-interactive rows, actions as clickable rows) and returns the interactive rows for navigation.
    /// </summary>
    public static List<MenuItem> PopulateMenu(
        System.Windows.Controls.Menu menu,
        List<ActionMenuItem> finalItems,
        IReadOnlyList<AppSearchResult> selection,
        Dictionary<uint, IDynamicActionProvider> cmdMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMap,
        SearchWindowType windowType,
        IPluginSearchWindow view,
        Style flyoutStyle,
        Action closeFlyout)
    {
        var menuItems = new List<MenuItem>();

        foreach (var item in finalItems)
        {
            if (item.IsSectionHeader || item.IsSeparator)
            {
                menu.Items.Add(CreateNonInteractiveItem(item, flyoutStyle));
                continue;
            }

            var mItem = CreateActionItem(item, selection, cmdMap, subMap, windowType, view, flyoutStyle, closeFlyout);
            menuItems.Add(mItem);

            mItem.MouseEnter += (s, ev) =>
            {
                foreach (var child in menu.Items)
                {
                    if (child is MenuItem childItem && childItem != mItem && childItem.IsSubmenuOpen)
                    {
                        childItem.IsSubmenuOpen = false;
                    }
                }
                if (mItem.HasItems && !mItem.IsSubmenuOpen)
                {
                    mItem.IsSubmenuOpen = true;
                }
            };

            menu.Items.Add(mItem);
        }

        return menuItems;
    }

    // A section header / separator row: same style + template as the actions list, but disabled and
    // non-hit-testable so it is purely visual and skipped by navigation.
    public static MenuItem CreateNonInteractiveItem(ActionMenuItem item, Style flyoutStyle)
    {
        item.IsCompact = true;
        return new MenuItem
        {
            Style = flyoutStyle,
            DataContext = item,
            Header = item,
            // MinHeight, not Height: a fixed Height is a hard cap that clips/squeezes content taller
            // than it, and the separator's 1px line plus its own margin plus this template's ItemBorder
            // margin (see ActionFlyoutMenuItemStyle) adds up to more than the compacted row height ever
            // budgeted for -- the line was getting arranged into a space smaller than it needed and
            // never actually painted. The plain actions list's own ActionItemStyle already uses MinHeight
            // for exactly this reason (a row is free to grow to fit content that needs more); mirroring
            // that here instead of special-casing the separator's arithmetic is what actually fixes it.
            // No extra multiplier here: item.ItemHeight already carries UiMetrics.ActionMenuCompactRowScale
            // (applied in ActionMenuBuilder), so applying another one here would double-shrink this row.
            MinHeight = item.ItemHeight,
            IsEnabled = false,
            IsHitTestVisible = false
        };
    }

    // Renders one ActionMenuItem as a flyout row (icon, text, hotkey hint, submenu arrow all come from
    // ActionMenuItemTemplate). Shell submenus lazy-load via the shared ActionMenuBuilder; a leaf's click
    // closes the flyout and runs the action.
    public static MenuItem CreateActionItem(
        ActionMenuItem item,
        IReadOnlyList<AppSearchResult> selection,
        Dictionary<uint, IDynamicActionProvider> cmdMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMap,
        SearchWindowType windowType,
        IPluginSearchWindow view,
        Style flyoutStyle,
        Action closeFlyout)
    {
        item.IsCompact = true;
        var menuItem = new MenuItem
        {
            Style = flyoutStyle,
            DataContext = item,
            Header = item,
            // No extra multiplier here either -- see CreateNonInteractiveItem's own comment on why:
            // item.ItemHeight is already compacted at the source (UiMetrics.ActionMenuCompactRowScale).
            Height = item.ItemHeight,
            IsEnabled = !item.IsDisabled
        };

        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            menuItem.Items.Add(CreateNonInteractiveItem(new ActionMenuItem { Text = "Loading..." }, flyoutStyle));
            var loaded = false;
            menuItem.SubmenuOpened += (s, e) =>
            {
                if (loaded) return;
                loaded = true;
                menuItem.Items.Clear();
                foreach (var subItem in ActionMenuBuilder.Build(selection, item.SubMenuHandle, windowType, cmdMap, subMap))
                {
                    menuItem.Items.Add(subItem.IsSectionHeader || subItem.IsSeparator
                        ? CreateNonInteractiveItem(subItem, flyoutStyle)
                        : CreateActionItem(subItem, selection, cmdMap, subMap, windowType, view, flyoutStyle, closeFlyout));
                }
            };
        }
        else
        {
            menuItem.Click += (s, e) =>
            {
                if (e.Source != menuItem) return;
                closeFlyout();
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => DispatchExecute(item, selection, cmdMap, view)),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        return menuItem;
    }

    // Runs an item like ShellMenuPresenter.ExecuteSelectedAction's non-navigation branches: direct
    // delegate, built-in Lertaro action, or dynamic (shell) provider command. Content parity with the
    // actions list is guaranteed upstream by sharing ActionMenuBuilder.
    public static void DispatchExecute(
        ActionMenuItem item,
        IReadOnlyList<AppSearchResult> selection,
        Dictionary<uint, IDynamicActionProvider> cmdMap,
        IPluginSearchWindow view)
    {
        if (item.OnExecute != null)
        {
            item.OnExecute();
            return;
        }

        var registration = PluginManager.Instance.GetActionByRuntimeId(item.CommandId);
        if (registration != null)
        {
            PluginPerformanceMonitor.Measure(registration.Action, () => registration.Action.Execute(selection, view));
            return;
        }

        if (cmdMap.TryGetValue(item.CommandId, out var provider))
        {
            var hwnd = Application.Current.MainWindow != null
                ? new WindowInteropHelper(Application.Current.MainWindow).Handle
                : IntPtr.Zero;
            PluginPerformanceMonitor.Measure(provider, () => provider.ExecuteCommand(selection, item.CommandId, hwnd));
        }
    }
}
