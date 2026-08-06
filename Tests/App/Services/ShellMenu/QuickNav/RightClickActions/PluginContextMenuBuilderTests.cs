using System.Windows.Controls;
using Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav.RightClickActions;

[TestClass]
public sealed class PluginContextMenuBuilderTests
{
    [StaTestMethod]
    public void GetActiveMenuState_NoOpenSubmenu_ReturnsRootWithItsEnabledItems()
    {
        var menu = new ContextMenu();
        var enabled = new MenuItem { IsEnabled = true };
        var disabled = new MenuItem { IsEnabled = false };
        menu.Items.Add(enabled);
        menu.Items.Add(disabled);

        var (parent, items, highlightedIndex) = PluginContextMenuBuilder.GetActiveMenuState(menu, null);

        Assert.AreSame(menu, parent);
        CollectionAssert.AreEqual(new[] { enabled }, items);
        Assert.AreEqual(-1, highlightedIndex);
    }

    // The "descends into an open submenu" branch is intentionally not covered here: MenuItem.IsSubmenuOpen
    // is coerced back to false unless the item is actually Loaded (connected to a real PresentationSource),
    // which a bare unit test can't establish without showing a real Window -- too invasive/flaky for this
    // suite. The other three tests below cover the traversal's other real branches (root enumeration,
    // non-ItemsControl root, enabled-only filtering).

    [StaTestMethod]
    public void GetActiveMenuState_NonItemsControlRoot_ReturnsEmptyItemList()
    {
        var (parent, items, highlightedIndex) = PluginContextMenuBuilder.GetActiveMenuState(new Label(), null);

        Assert.IsEmpty(items);
        Assert.AreEqual(-1, highlightedIndex);
    }

    [StaTestMethod]
    public void GetActiveMenuState_ItemsIncludeOnlyEnabledMenuItems()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { IsEnabled = false });
        var separator = new Separator();
        menu.Items.Add(separator);

        var (_, items, _) = PluginContextMenuBuilder.GetActiveMenuState(menu, null);

        Assert.IsEmpty(items);
    }
}
