using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

[TestClass]
[DoNotParallelize]
public sealed class ShellIconHelperTests
{
    [TestInitialize]
    public void Setup() => ShellIconHelper.ClearCache();

    [TestCleanup]
    public void Cleanup() => ShellIconHelper.ClearCache();

    [StaTestMethod]
    public void GetIconFromCacheOnly_SpecialMarkers_ReturnsExpected()
    {
        var nullIcon = ShellIconHelper.GetIconFromCacheOnly("__NO_RESULTS__", false, out var needsLoadNoResults);
        Assert.IsNull(nullIcon);
        Assert.IsFalse(needsLoadNoResults);

        var showMoreIcon = ShellIconHelper.GetIconFromCacheOnly("__SHOW_MORE__", false, out var needsLoadShowMore);
        Assert.IsNotNull(showMoreIcon);
        Assert.IsFalse(needsLoadShowMore);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_UniqueIconTypes_RequiresAsyncLoadAndReturnsPlaceholder()
    {
        // Directory requires background load and returns generic folder placeholder
        var dirIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\TestDir", true, out var needsLoadDir);
        Assert.IsTrue(needsLoadDir);
        Assert.IsNotNull(dirIcon);

        // Executable requires background load and returns placeholder
        var exeIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\Apps\test.exe", false, out var needsLoadExe);
        Assert.IsTrue(needsLoadExe);
        Assert.IsNotNull(exeIcon);

        // Shortcut requires background load and returns placeholder
        var lnkIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\Apps\test.lnk", false, out var needsLoadLnk);
        Assert.IsTrue(needsLoadLnk);
        Assert.IsNotNull(lnkIcon);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_StandardDocumentExtension_LoadsSynchronouslyWithoutDiskProbe()
    {
        var icon1 = ShellIconHelper.GetIconFromCacheOnly(@"C:\folder\doc1.txt", false, out var needsLoad1);
        Assert.IsFalse(needsLoad1);
        Assert.IsNotNull(icon1);

        // Second file with the same extension shares the cached icon
        var icon2 = ShellIconHelper.GetIconFromCacheOnly(@"D:\other\doc2.txt", false, out var needsLoad2);
        Assert.IsFalse(needsLoad2);
        Assert.AreSame(icon1, icon2);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_UncPaths_DoesNotBlockOrThrow()
    {
        // UNC document path returns standard extension icon without probing disk
        var uncDoc = ShellIconHelper.GetIconFromCacheOnly(@"\\remote-server\share\file.txt", false, out var needsLoadDoc);
        Assert.IsFalse(needsLoadDoc);
        Assert.IsNotNull(uncDoc);

        // UNC executable path marks needsLoad = true without probing disk on UI thread
        var uncExe = ShellIconHelper.GetIconFromCacheOnly(@"\\remote-server\share\tool.exe", false, out var needsLoadExe);
        Assert.IsTrue(needsLoadExe);
        Assert.IsNotNull(uncExe);
    }
}
