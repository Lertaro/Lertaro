using Lertaro.App.Services.ShellMenu.Presenter;

namespace Lertaro.App.Tests.Services.ShellMenu.Presenter;

[TestClass]
public sealed class ShellMenuFilterTests
{
    private static ActionMenuItem Item(string text) => new() { Text = text };
    private static ActionMenuItem Separator() => new() { IsSeparator = true };
    private static ActionMenuItem Header(string title) => new() { IsSectionHeader = true, SectionTitle = title };

    [TestMethod]
    public void Apply_EmptyFilter_ReturnsAllItemsWithCleanup()
    {
        var items = new List<ActionMenuItem> { Item("Copy"), Item("Paste") };

        var result = ShellMenuFilter.Apply(items, "");

        CollectionAssert.AreEqual(items, result);
    }

    [TestMethod]
    public void Apply_MatchingFilter_KeepsMatchingItems()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Item("Copy"), Item("Paste") }, "copy");

        Assert.HasCount(1, result);
        Assert.AreEqual("Copy", result[0].Text);
    }

    [TestMethod]
    public void Apply_NoMatches_ReturnsEmptyList() =>
        Assert.IsEmpty(ShellMenuFilter.Apply(new List<ActionMenuItem> { Item("Copy"), Item("Paste") }, "zzz_no_match_zzz"));

    [TestMethod]
    public void Apply_SectionHeaderWithNoMatchingItems_IsRemoved()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Header("Group"), Item("Copy") }, "zzz_no_match_zzz");

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void Apply_SectionHeaderWithMatchingItemBelow_IsKept()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Header("Group"), Item("Copy"), Item("Paste") }, "copy");

        Assert.HasCount(2, result);
        Assert.IsTrue(result[0].IsSectionHeader);
        Assert.AreEqual("Copy", result[1].Text);
    }

    [TestMethod]
    public void Apply_LeadingSeparatorAfterFiltering_IsRemoved()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Separator(), Item("Copy") }, "copy");

        Assert.HasCount(1, result);
        Assert.AreEqual("Copy", result[0].Text);
    }

    [TestMethod]
    public void Apply_TrailingSeparatorAfterFiltering_IsRemoved()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Item("Copy"), Separator() }, "copy");

        Assert.HasCount(1, result);
        Assert.AreEqual("Copy", result[0].Text);
    }

    [TestMethod]
    public void Apply_AllHeadersAndSeparatorsNoRealItems_ReturnsEmpty()
    {
        var result = ShellMenuFilter.Apply(new List<ActionMenuItem> { Header("Group"), Separator() }, "");

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void Apply_WhitespaceOnlyFilter_TreatedAsNoFilter()
    {
        var items = new List<ActionMenuItem> { Item("Copy") };

        var result = ShellMenuFilter.Apply(items, "   ");

        Assert.HasCount(1, result);
    }
}
