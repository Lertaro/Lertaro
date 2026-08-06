namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class QuickPanelGroupOrderingTests
{
    private static readonly string[] Available = { "downloads", "desktop", "favorites" };

    [TestMethod]
    public void Resolve_NoStoredOrder_KeepsDiscoveryOrder()
        => CollectionAssert.AreEqual(Available, QuickPanelGroupOrdering.Resolve(Available, null, null).ToArray());

    [TestMethod]
    public void Resolve_StoredOrder_LeadsInThatOrder()
    {
        var resolved = QuickPanelGroupOrdering.Resolve(Available, new[] { "favorites", "downloads" }, null);

        CollectionAssert.AreEqual(new[] { "favorites", "downloads", "desktop" }, resolved.ToArray());
    }

    // A folder just added, or a plugin source that only appeared this session, has no place in the
    // stored order yet. Appending it is the only answer that does not silently reshuffle what the user
    // arranged -- landing it at the top would.
    [TestMethod]
    public void Resolve_UnlistedSources_FollowEverythingListed_InDiscoveryOrder()
    {
        var available = new[] { "newly-added", "downloads", "also-new" };

        var resolved = QuickPanelGroupOrdering.Resolve(available, new[] { "downloads" }, null);

        CollectionAssert.AreEqual(new[] { "downloads", "newly-added", "also-new" }, resolved.ToArray());
    }

    [TestMethod]
    public void Resolve_DisabledSources_AreLeftOut()
    {
        var resolved = QuickPanelGroupOrdering.Resolve(Available, null, new[] { "desktop" });

        CollectionAssert.AreEqual(new[] { "downloads", "favorites" }, resolved.ToArray());
    }

    // An id in the order list that nothing supplies right now (a plugin switched off for the moment, a
    // folder source deleted) must not push everything after it around, and must not appear itself.
    [TestMethod]
    public void Resolve_OrderMentioningSourcesThatAreGone_IgnoresThemWithoutDisturbingTheRest()
    {
        var resolved = QuickPanelGroupOrdering.Resolve(
            new[] { "downloads", "desktop" },
            new[] { "long-gone", "desktop", "also-gone", "downloads" },
            null);

        CollectionAssert.AreEqual(new[] { "desktop", "downloads" }, resolved.ToArray());
    }

    [TestMethod]
    public void Resolve_ComparesIdsCaseInsensitively()
    {
        var resolved = QuickPanelGroupOrdering.Resolve(
            new[] { "Downloads", "Desktop" },
            new[] { "DESKTOP" },
            new[] { "DOWNLOADS" });

        CollectionAssert.AreEqual(new[] { "Desktop" }, resolved.ToArray());
    }

    [TestMethod]
    public void Resolve_DuplicateOrEmptyIds_AreDroppedRatherThanRepeated()
    {
        var resolved = QuickPanelGroupOrdering.Resolve(
            new[] { "downloads", "downloads", "", "desktop" },
            new[] { "desktop", "desktop" },
            null);

        CollectionAssert.AreEqual(new[] { "desktop", "downloads" }, resolved.ToArray());
    }

    // Three folders and nothing else. Anything beyond a folder (favorites, the system's own recent
    // list) is a plugin's to provide, not this page's.
    [TestMethod]
    public void CreateDefault_StartsWithThreeFolderSources()
    {
        var tab = QuickPanelTab.CreateDefault();

        Assert.HasCount(3, tab.Folders);
        Assert.IsTrue(tab.Folders.All(f => f.Kind == QuickPanelSourceKind.RecentFiles));
        // Distinct ids, or two sources would share one set of display preferences.
        Assert.HasCount(3, tab.Folders.Select(f => f.Id).Distinct().ToList());
        Assert.IsEmpty(tab.DisabledGroupIds);
        Assert.IsNotEmpty(tab.Id);
    }

    // The page edits clones, so anything the clone misses is an edit that silently reverts on Save.
    [TestMethod]
    public void Clone_CopiesEveryListWithoutSharingIt()
    {
        var tab = QuickPanelTab.CreateDefault();
        tab.Processes.Add("chrome");
        tab.GroupPreferences[tab.Folders[0].Id] = new QuickPanelGroupPreference { DisplayName = "素材" };

        var clone = tab.Clone();
        clone.Processes.Clear();
        clone.Folders[0].Path = @"C:\elsewhere";
        clone.GroupPreferences[tab.Folders[0].Id].DisplayName = "changed";

        Assert.HasCount(1, tab.Processes);
        Assert.AreNotEqual(@"C:\elsewhere", tab.Folders[0].Path);
        Assert.AreEqual("素材", tab.GroupPreferences[tab.Folders[0].Id].DisplayName);
    }
}
