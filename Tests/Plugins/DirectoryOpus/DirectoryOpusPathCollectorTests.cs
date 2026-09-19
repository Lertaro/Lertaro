namespace Lertaro.Plugins.DirectoryOpus.Tests;

// The two policies behind the opened-folder snapshot, pinned without a Directory Opus window to measure.
//
// Reported bug these exist for: a lister with five tabs contributed two entries, because only the tab in
// front owns a VISIBLE container window -- so the collector's snapshot has to be built from all of them,
// and each inactive tab's folder has to come from a different control than the active tab's.
[TestClass]
public sealed class DirectoryOpusPathCollectorTests
{
    private static readonly IntPtr Lister = (IntPtr)0x1234;

    // ---------------------------------------------------------------------------------------------
    // Which control reports a container's folder
    // ---------------------------------------------------------------------------------------------

    // The active tab's location bar is the folder its display is showing, so it wins when it has text.
    [TestMethod]
    public void ChooseReportedPath_LocationBarWithText_Wins() => Assert.AreEqual(@"C:\Program Files (x86)",
            DirectoryOpusPathCollector.ChooseReportedPath(@"C:\Program Files (x86)", @"C:\"));

    // Every inactive tab has NO location bar, and that is the case that used to yield nothing at all.
    [TestMethod]
    public void ChooseReportedPath_NoLocationBar_FallsBackToTheContainerText() => Assert.AreEqual(@"D:\LanguageLearning",
            DirectoryOpusPathCollector.ChooseReportedPath(null, @"D:\LanguageLearning"));

    // A bar that exists but is empty must not shadow a container caption that does have the path.
    [TestMethod]
    public void ChooseReportedPath_EmptyLocationBar_FallsBackToTheContainerText() => Assert.AreEqual(@"D:\BCUninstaller",
            DirectoryOpusPathCollector.ChooseReportedPath("   ", @"D:\BCUninstaller"));

    // Nothing to report: the caller's own normalization turns this into "no path", not an empty entry.
    [TestMethod]
    public void ChooseReportedPath_NothingToReport_ReturnsTheContainerTextAsIs()
    {
        Assert.IsNull(DirectoryOpusPathCollector.ChooseReportedPath(null, null));
        Assert.AreEqual(string.Empty, DirectoryOpusPathCollector.ChooseReportedPath(string.Empty, string.Empty));
    }

    // ---------------------------------------------------------------------------------------------
    // How one lister's containers become the snapshot
    // ---------------------------------------------------------------------------------------------

    // The tabs in front come first, so the entries a consumer already showed stay at the head of the list
    // even though the enumeration order behind them is Directory Opus's own creation order.
    [TestMethod]
    public void BuildOpenedFolders_ListsTheActiveTabsFirst()
    {
        var folders = DirectoryOpusPathCollector.BuildOpenedFolders(new[]
        {
            (Path: (string?)@"D:\LanguageLearning", IsActive: false, Window: Lister),
            (Path: (string?)@"C:\Program Files (x86)", IsActive: true, Window: Lister),
            (Path: (string?)@"D:\BCUninstaller", IsActive: false, Window: Lister),
            (Path: (string?)@"Z:\trip", IsActive: true, Window: Lister),
        });

        CollectionAssert.AreEqual(
            new[] { @"C:\Program Files (x86)", @"Z:\trip", @"D:\LanguageLearning", @"D:\BCUninstaller" },
            folders.Select(folder => folder.Path).ToArray());
    }

    // A container with no readable folder contributes nothing, but the rest of the snapshot survives.
    [TestMethod]
    public void BuildOpenedFolders_SkipsContainersWithoutAPath()
    {
        var folders = DirectoryOpusPathCollector.BuildOpenedFolders(new[]
        {
            (Path: null, IsActive: true, Window: Lister),
            (Path: (string?)string.Empty, IsActive: false, Window: Lister),
            (Path: (string?)@"C:\", IsActive: false, Window: Lister),
        });

        Assert.HasCount(1, folders);
        Assert.AreEqual(@"C:\", folders[0].Path);
    }

    // Two tabs on the same folder are one folder to offer. Case-insensitively and regardless of a trailing
    // separator -- Windows paths are both -- and the FIRST occurrence wins, which, after the ordering
    // above, is the active tab's entry.
    [TestMethod]
    public void BuildOpenedFolders_CollapsesTheSameFolderToTheFirstEntry()
    {
        var folders = DirectoryOpusPathCollector.BuildOpenedFolders(new[]
        {
            (Path: (string?)@"C:\Windows", IsActive: true, Window: Lister),
            (Path: (string?)@"c:\windows", IsActive: false, Window: Lister),
            (Path: (string?)@"C:\Windows\", IsActive: false, Window: Lister),
        });

        Assert.HasCount(1, folders);
        Assert.AreEqual(@"C:\Windows", folders[0].Path);
        Assert.AreEqual(Lister, folders[0].WindowHandle);
    }
}
