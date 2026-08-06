namespace Lertaro.Core.Tests.Settings;

// The two defaults the settings page and the panel both have to agree on: what an unnamed source is
// called, and what order it starts in. Both are shared statics precisely because a second copy of
// either would only be visible as the two disagreeing on screen.
[TestClass]
public sealed class QuickPanelSourceDefaultsTests
{
    [TestMethod]
    public void DefaultName_IsTheFoldersOwnName()
        => Assert.AreEqual("Downloads", QuickPanelFolderSource.DefaultName(@"C:\Users\me\Downloads"));

    [TestMethod]
    public void DefaultName_IgnoresATrailingSeparator()
        => Assert.AreEqual("Downloads", QuickPanelFolderSource.DefaultName(@"C:\Users\me\Downloads\"));

    // A drive root has no last segment, so it stands as its own name rather than as the empty string
    // trimming it would otherwise give.
    [TestMethod]
    public void DefaultName_DriveRoot_IsThePathItself()
        => Assert.AreEqual(@"D:\", QuickPanelFolderSource.DefaultName(@"D:\"));

    [TestMethod]
    public void DefaultName_NoPath_IsEmpty()
        => Assert.AreEqual(string.Empty, QuickPanelFolderSource.DefaultName("   "));

    // The kind is itself an order choice, so a group with no stored preference must not contradict the
    // dropdown that configured it.
    [TestMethod]
    public void DefaultSort_All_IsByName()
        => Assert.AreEqual(QuickPanelSortMode.NameAscending,
            QuickPanelGroupPreference.DefaultSortFor(QuickPanelSourceKind.All));

    [TestMethod]
    public void DefaultSort_EveryOtherKind_IsNewestFirst()
    {
        Assert.AreEqual(QuickPanelSortMode.ModifiedDescending,
            QuickPanelGroupPreference.DefaultSortFor(QuickPanelSourceKind.RecentFiles));
        Assert.AreEqual(QuickPanelSortMode.ModifiedDescending,
            QuickPanelGroupPreference.DefaultSortFor(QuickPanelSourceKind.AllByModified));
    }
}
