using Lertaro.App.Services.ShellMenu.Presenter;

namespace Lertaro.App.Tests.Services.ShellMenu.Presenter;

[TestClass]
public sealed class ShellMenuFilterTests
{
    [StaTestMethod]
    public void ApplyToList_LateDynamicMenuKeepsKeyboardChoiceAfterStaticItemsAreRebuilt()
    {
        var list = new System.Windows.Controls.ListBox();
        ShellMenuFilter.ApplyToList(list, [Header("Built in"),
            new() { Text = "Open", CommandId = 0x80000000 },
            new() { Text = "Copy", CommandId = 0x80000001 }], "");
        list.SelectedIndex = 2;

        // Complete the background menu after the user navigates; fresh objects and section order
        // deliberately differ, as when a slower shell extension finishes on another machine.
        var rebuiltCopy = new ActionMenuItem { Text = "Copy", CommandId = 0x80000001 };
        ShellMenuFilter.ApplyToList(list, [Header("Shell"), new() { Text = "Properties", CommandId = 1 },
            Header("Built in"), new() { Text = "Open", CommandId = 0x80000000 }, rebuiltCopy], "", preserveSelection: true);

        Assert.AreSame(rebuiltCopy, list.SelectedItem);
    }

    [StaTestMethod]
    public void ApplyToList_FilterTypingSelectsFirstMatch()
    {
        var list = new System.Windows.Controls.ListBox();
        var copyPath = new ActionMenuItem { Text = "Copy path", CommandId = 1 };
        var copyName = new ActionMenuItem { Text = "Copy name", CommandId = 2 };
        List<ActionMenuItem> items = [copyPath, copyName];
        ShellMenuFilter.ApplyToList(list, items, "");
        list.SelectedItem = copyName;

        ShellMenuFilter.ApplyToList(list, items, "copy");

        Assert.AreSame(copyPath, list.SelectedItem);
        Assert.AreEqual("copy", copyPath.SearchQuery);
    }

    [StaTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApplyToList_RemovedOrDisabledSelectionFallsBackToFirstSelectable(bool keepDisabled)
    {
        var list = new System.Windows.Controls.ListBox();
        var selected = new ActionMenuItem { Text = "Copy", CommandId = 2 };
        ShellMenuFilter.ApplyToList(list, [selected], "");
        var open = new ActionMenuItem { Text = "Open", CommandId = 1 };
        List<ActionMenuItem> refreshed = [Header("Built in"),
            new() { Text = "Unavailable", IsDisabled = true }, Separator(), open];
        if (keepDisabled)
            refreshed.Add(new() { Text = "Copy", CommandId = 2, IsDisabled = true });

        ShellMenuFilter.ApplyToList(list, refreshed, "", preserveSelection: true);

        Assert.AreSame(open, list.SelectedItem);
    }

    [StaTestMethod]
    public void ApplyToList_FilteredSelectionFallsBackToFirstMatch()
    {
        var list = new System.Windows.Controls.ListBox();
        var selected = new ActionMenuItem { Text = "Copy", CommandId = 2 };
        ShellMenuFilter.ApplyToList(list, [selected], "");
        var open = new ActionMenuItem { Text = "Open", CommandId = 1 };

        ShellMenuFilter.ApplyToList(list, [selected, open], "open", preserveSelection: true);

        Assert.AreSame(open, list.SelectedItem);
        Assert.HasCount(1, list.Items);
    }

    [StaTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApplyToList_ZeroCommandIdOnlyMatchesSameInstance(bool reuseSelected)
    {
        var list = new System.Windows.Controls.ListBox();
        var selected = Item("Copy");
        ShellMenuFilter.ApplyToList(list, [selected], "");
        var open = new ActionMenuItem { Text = "Open", CommandId = 1 };
        var refreshedCopy = reuseSelected ? selected : Item("Copy");

        ShellMenuFilter.ApplyToList(list, [open, Item("Unrelated"), refreshedCopy], "", preserveSelection: true);

        Assert.AreSame(reuseSelected ? selected : open, list.SelectedItem);
    }

    [StaTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApplyToList_NoSelectableItemsClearsSelection(bool includeDisabled)
    {
        var list = new System.Windows.Controls.ListBox();
        ShellMenuFilter.ApplyToList(list, [new() { Text = "Copy", CommandId = 1 }], "");
        List<ActionMenuItem> refreshed = includeDisabled
            ? [Header("Built in"), new() { Text = "Copy", CommandId = 1, IsDisabled = true }, Separator()]
            : [];

        ShellMenuFilter.ApplyToList(list, refreshed, "", preserveSelection: true);

        Assert.IsNull(list.SelectedItem);
        Assert.AreEqual(-1, list.SelectedIndex);
    }

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
