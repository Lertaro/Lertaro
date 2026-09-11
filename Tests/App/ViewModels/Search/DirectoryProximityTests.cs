using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// The ordering tier behind the inline window's Current Folder section: direct children, then descendants
// by depth, then anything outside the folder.
[TestClass]
public sealed class DirectoryProximityTests
{
    [TestMethod]
    public void Tier_DirectChild_IsZero() =>
        Assert.AreEqual(0, DirectoryProximity.Tier(@"C:\Root\file.txt", @"C:\Root"));

    [TestMethod]
    public void Tier_Descendant_DepthCountsLevelsBelowTheFolder()
    {
        Assert.AreEqual(1, DirectoryProximity.Tier(@"C:\Root\Child\file.txt", @"C:\Root"));
        Assert.AreEqual(2, DirectoryProximity.Tier(@"C:\Root\A\B\file.txt", @"C:\Root"));
        Assert.AreEqual(3, DirectoryProximity.Tier(@"C:\Root\A\B\C\file.txt", @"C:\Root"));
    }

    [TestMethod]
    public void Tier_TrailingSeparatorOnTheFolder_IsIgnored() =>
        Assert.AreEqual(0, DirectoryProximity.Tier(@"C:\Root\file.txt", @"C:\Root\"));

    [TestMethod]
    public void Tier_IsCaseInsensitiveOnTheFolderPrefix() =>
        Assert.AreEqual(0, DirectoryProximity.Tier(@"C:\ROOT\file.txt", @"c:\root"));

    [TestMethod]
    public void Tier_SiblingFolderWithSharedPrefix_IsOutside()
    {
        // "C:\Roots" merely shares a string prefix with "C:\Root" -- it is not a child of it.
        Assert.AreEqual(DirectoryProximity.Outside, DirectoryProximity.Tier(@"C:\Roots\file.txt", @"C:\Root"));
    }

    [TestMethod]
    public void Tier_UnrelatedPath_IsOutside() =>
        Assert.AreEqual(DirectoryProximity.Outside, DirectoryProximity.Tier(@"D:\Other\file.txt", @"C:\Root"));

    [TestMethod]
    public void Tier_TheFolderItself_IsOutside() =>
        Assert.AreEqual(DirectoryProximity.Outside, DirectoryProximity.Tier(@"C:\Root", @"C:\Root"));

    [TestMethod]
    public void Tier_DriveRootFolder_StillMeasuresDepth()
    {
        Assert.AreEqual(0, DirectoryProximity.Tier(@"C:\file.txt", @"C:\"));
        Assert.AreEqual(1, DirectoryProximity.Tier(@"C:\Child\file.txt", @"C:\"));
    }
}
