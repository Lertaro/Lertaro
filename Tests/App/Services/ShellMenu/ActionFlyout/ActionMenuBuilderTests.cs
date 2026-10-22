using System.Windows;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.ActionFlyout;

[TestClass]
public sealed class ActionMenuBuilderTests
{
    private static ActionMenuItem Item(string text, bool hasSubMenu = false) => new() { Text = text, HasSubMenu = hasSubMenu };
    private static ActionMenuItem Separator() => new() { IsSeparator = true };
    private static ActionMenuItem Header(string title, string groupId = "") => new() { IsSectionHeader = true, SectionTitle = title, SectionGroupId = groupId };

    private sealed class FakeDynamicActionProvider : IDynamicActionProvider
    {
        public string GroupName => "Custom Actions";
        public bool CanProvide(IReadOnlyList<ISearchResult> results) => false;
        public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu) => Enumerable.Empty<DynamicMenuItem>();
        public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd) { }
        public void ClearSession() { }
    }

    [TestMethod]
    public void BuildStaticGroupId_MatchingBuiltinLabel_ReturnsBuiltinSentinel() => Assert.AreEqual("__builtin__", ActionMenuBuilder.BuildStaticGroupId("Common", "Common"));

    [TestMethod]
    public void BuildStaticGroupId_CustomGroup_ReturnsStaticPrefixedId() => Assert.AreEqual("static::Archive", ActionMenuBuilder.BuildStaticGroupId("Archive", "Common"));

    [TestMethod]
    public void BuildDynamicGroupId_IsStableAcrossCalls()
    {
        var provider = new FakeDynamicActionProvider();

        var first = ActionMenuBuilder.BuildDynamicGroupId(provider);
        var second = ActionMenuBuilder.BuildDynamicGroupId(provider);

        Assert.AreEqual(first, second);
        Assert.Contains("DynamicActionProvider", first);
        Assert.Contains(nameof(FakeDynamicActionProvider), first);
    }

    [TestMethod]
    public void FinalizeItems_NoDuplicates_ReturnsAllUnchanged()
    {
        var items = new List<ActionMenuItem> { Item("Copy"), Item("Paste") };

        var result = ActionMenuBuilder.FinalizeItems(items);

        CollectionAssert.AreEqual(items, result);
    }

    // The STA half of this class: a configured vector icon arrives as path data (the item list is built off
    // the UI thread) and only becomes the ImageSource the row binds at finalize time, which is where every
    // host is back on the UI thread. A configured icon that rendered nowhere is what this pair guards --
    // the row shows a placeholder bullet instead of silently losing the icon.
    [STATestMethod]
    public void FinalizeItems_VectorPathData_BuildsTheIconTheRowBinds()
    {
        var item = new ActionMenuItem { Text = "Compress", VectorIconPathData = "M7 2v11h3v9l7-12h-4l3-8z" };

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { item });

        Assert.IsNotNull(result[0].Icon);
        Assert.AreEqual(Visibility.Visible, result[0].IconVisibility);
        Assert.AreEqual(Visibility.Collapsed, result[0].PlaceholderVisibility);
    }

    [STATestMethod]
    public void FinalizeItems_VectorPathDataUnparseable_KeepsThePlaceholderBullet()
    {
        var item = new ActionMenuItem { Text = "Compress", VectorIconPathData = "not a path at all" };

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { item });

        Assert.IsNull(result[0].Icon, "an unusable path was turned into an icon anyway");
        Assert.AreEqual(Visibility.Visible, result[0].PlaceholderVisibility);
    }

    [STATestMethod]
    public void FinalizeItems_BitmapIconAlreadyBuilt_WinsOverPathData()
    {
        var existing = new System.Windows.Media.DrawingImage();
        var item = new ActionMenuItem { Text = "Compress", Icon = existing, VectorIconPathData = "M7 2v11h3v9l7-12h-4l3-8z" };

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { item });

        Assert.AreSame(existing, result[0].Icon, "the bitmap the provider handed over was overwritten");
    }

    [TestMethod]
    public void FinalizeItems_DuplicateText_KeepsFirstWhenNeitherHasSubMenu()
    {
        var first = Item("Open");
        var second = Item("Open");

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { first, second });

        Assert.HasCount(1, result);
        Assert.AreSame(first, result[0]);
    }

    [TestMethod]
    public void FinalizeItems_DuplicateText_PrefersTheOneWithSubMenu()
    {
        var plain = Item("Send to");
        var withSubMenu = Item("Send to", hasSubMenu: true);

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { plain, withSubMenu });

        Assert.HasCount(1, result);
        Assert.AreSame(withSubMenu, result[0]);
    }

    [TestMethod]
    public void FinalizeItems_DuplicateMatchIsCaseInsensitive()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("Copy"), Item("copy") });

        Assert.HasCount(1, result);
    }

    [TestMethod]
    public void FinalizeItems_ConsecutiveSeparators_CollapseToOne()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("A"), Separator(), Separator(), Item("B") });

        Assert.HasCount(3, result);
        Assert.IsTrue(result[1].IsSeparator);
        Assert.AreEqual("B", result[2].Text);
    }

    [TestMethod]
    public void FinalizeItems_LeadingSeparator_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Separator(), Item("A") });

        Assert.HasCount(1, result);
        Assert.AreEqual("A", result[0].Text);
    }

    [TestMethod]
    public void FinalizeItems_TrailingSeparator_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("A"), Separator() });

        Assert.HasCount(1, result);
        Assert.AreEqual("A", result[0].Text);
    }

    [TestMethod]
    public void FinalizeItems_SeparatorRightAfterHeader_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Header("Group"), Separator(), Item("A") });

        Assert.HasCount(2, result);
        Assert.IsTrue(result[0].IsSectionHeader);
        Assert.AreEqual("A", result[1].Text);
    }

    [TestMethod]
    public void FinalizeItems_EmptyList_ReturnsEmpty() =>
        Assert.IsEmpty(ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem>()));
}
