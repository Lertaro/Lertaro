using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class MenuBuilderAddFolderItemsTests
{
    [TestMethod]
    public void AddFolderItems_TopLevelFolder_AddsLeafItem()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Downloads", @"C:\Downloads") };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Downloads", items[0].Text);
        Assert.IsTrue(items[0].IsActionable);
        // A leaf item's SubMenuHandle is only ever allocated (non-zero) when the path actually exists
        // and HasSubMenu is set -- the two must never disagree.
        Assert.AreEqual(items[0].HasSubMenu, items[0].SubMenuHandle != IntPtr.Zero);
    }

    [TestMethod]
    public void AddFolderItems_NestedFolder_AddsOneCategoryEntryNotALeaf()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("Router UI", @"C:\Net\Router", "Tools/Network"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Tools", items[0].Text);
        Assert.IsTrue(items[0].HasSubMenu);
        // QuickNavigationMenu's own click-suppression for HasSubMenu items only applies automatically
        // at the root level (isRootItem) -- nested submenu levels rely entirely on this flag, so a
        // category item must set it explicitly regardless of how deep it sits, or clicking it (rather
        // than hovering to expand) fires as if it were a real actionable leaf.
        Assert.IsFalse(items[0].IsActionable);
    }

    [TestMethod]
    public void AddFolderItems_TwoFoldersSameCategory_YieldsOnlyOneCategoryEntry()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("A", @"C:\A", "Tools"),
            Folder("B", @"C:\B", "Tools"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Tools", items[0].Text);
    }

    [TestMethod]
    public void AddFolderItems_ExpandingCategoryHandle_YieldsItsChildren()
    {
        var provider = new Provider();
        var rootItems = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("Router UI", @"C:\Net\Router", "Tools/Network"),
            Folder("Ping Script", @"C:\Net\Ping", "Tools/Network"),
            Folder("Top-level", @"C:\Other"),
        };

        MenuBuilder.AddFolderItems(rootItems, folders, Array.Empty<string>(), provider);
        // rootItems: one "Tools" category entry + one real top-level leaf.
        Assert.HasCount(2, rootItems);
        var toolsHandle = rootItems.Single(i => i.Text == "Tools").SubMenuHandle;
        Assert.IsTrue(MenuBuilder.TryDecodeCategoryPath(GetPath(provider, toolsHandle), out var toolsPrefix));
        CollectionAssert.AreEqual(new[] { "Tools" }, toolsPrefix);

        var toolsChildren = new List<DynamicMenuItem>();
        MenuBuilder.AddFolderItems(toolsChildren, folders, toolsPrefix, provider);

        // "Tools" itself has no direct leaf at this level (both folders are one level deeper, under
        // "Network"), so expanding it yields exactly one further "Network" category, not the two leaves.
        Assert.HasCount(1, toolsChildren);
        Assert.AreEqual("Network", toolsChildren[0].Text);

        var networkChildren = new List<DynamicMenuItem>();
        MenuBuilder.AddFolderItems(networkChildren, folders, new[] { "Tools", "Network" }, provider);

        Assert.HasCount(2, networkChildren);
        CollectionAssert.AreEquivalent(new[] { "Router UI", "Ping Script" }, networkChildren.Select(i => i.Text).ToList());
    }

    [TestMethod]
    public void AddFolderItems_SeparatorAtMatchingLevel_AddsSeparator()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("A", @"C:\A"),
            Folder("-", "-"),
            Folder("B", @"C:\B"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
    }

    [TestMethod]
    public void AddFolderItems_PathWithEnvironmentVariables_ExpandsVariablesAndAllocatesHandle()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("", @"%SystemRoot%")
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.IsTrue(items[0].HasSubMenu);
        Assert.AreNotEqual(IntPtr.Zero, items[0].SubMenuHandle);
        Assert.AreEqual(systemRoot, GetPath(provider, items[0].SubMenuHandle));
    }

    [TestMethod]
    public void AddFolderItems_InvalidVirtualFolder_DisablesItem()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("Missing", "shell:DefinitelyMissingFolder")
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.IsTrue(items[0].IsDisabled);
        Assert.IsFalse(items[0].HasSubMenu);
        Assert.AreEqual(IntPtr.Zero, items[0].SubMenuHandle);
    }

    [TestMethod]
    public void AddFolderItems_ValidVirtualFolder_RemainsEnabled()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("This PC", "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}")
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.IsFalse(items[0].IsDisabled);
        Assert.IsTrue(items[0].HasSubMenu);
        Assert.AreNotEqual(IntPtr.Zero, items[0].SubMenuHandle);
    }
}
