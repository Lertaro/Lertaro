using Lertaro.Core.Hook.Ipc;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook.Ipc;

// The opened-folder snapshot's order decides Ctrl+1..9 in the inline card and the cascader's opened-folders
// submenu. It used to be the collectors' own (DLL-name order), so a row's position said which plugin answered
// first rather than which window the user was in last -- PR #352.
[TestClass]
public sealed class OpenedFolderSnapshotPublisherTests
{
    private static readonly IntPtr Recent = new(0x1000);
    private static readonly IntPtr Older = new(0x2000);
    private static readonly IntPtr Oldest = new(0x3000);
    private static readonly IntPtr Gone = new(0x4000);

    private static OpenedFolder Folder(IntPtr hwnd, string path) => new(path, hwnd);

    // Which window is in front, expressed the way the publisher asks for it.
    private static Func<IntPtr, int> Ranks() => hwnd =>
        hwnd == Recent ? 1 : hwnd == Older ? 2 : hwnd == Oldest ? 3 : int.MaxValue;

    [TestMethod]
    public void OrderByWindowRank_WindowsAreOrderedByTheRankTheyWereGiven()
    {
        var folders = new[] { Folder(Oldest, @"C:\Third"), Folder(Recent, @"C:\Front"), Folder(Older, @"C:\Second") };

        var paths = OpenedFolderSnapshotPublisher.OrderByWindowRank(folders, Ranks());

        CollectionAssert.AreEqual(new[] { @"C:\Front", @"C:\Second", @"C:\Third" }, paths);
    }

    // Directory Opus reports its active tab first and its other panels after it, all under one window handle:
    // the order it chose has to survive, which is why the sort is stable rather than a name comparison.
    [TestMethod]
    public void OrderByWindowRank_EntriesSharingAWindow_KeepTheirOwnOrder()
    {
        var folders = new[]
        {
            Folder(Recent, @"C:\ActiveTab"),
            Folder(Recent, @"C:\OtherPanel"),
            Folder(Older, @"D:\Behind")
        };

        var paths = OpenedFolderSnapshotPublisher.OrderByWindowRank(folders, Ranks());

        CollectionAssert.AreEqual(new[] { @"C:\ActiveTab", @"C:\OtherPanel", @"D:\Behind" }, paths);
    }

    // A window on another virtual desktop, or one closed since the collector ran, is still a folder the user
    // has open: it goes last, in its own order, instead of disappearing.
    [TestMethod]
    public void OrderByWindowRank_UnknownWindows_GoLastAndKeepTheirOrder()
    {
        var folders = new[]
        {
            Folder(Gone, @"X:\AnotherDesktop"),
            Folder(Oldest, @"C:\Third"),
            Folder(new IntPtr(0x5000), @"X:\AlsoUnknown"),
            Folder(IntPtr.Zero, @"X:\NoHandle")
        };

        var paths = OpenedFolderSnapshotPublisher.OrderByWindowRank(folders, Ranks());

        CollectionAssert.AreEqual(
            new[] { @"C:\Third", @"X:\AnotherDesktop", @"X:\AlsoUnknown", @"X:\NoHandle" }, paths);
    }

    [TestMethod]
    public void OrderByWindowRank_BlankPaths_AreDroppedAndDuplicatesSurvive()
    {
        var folders = new[]
        {
            Folder(Recent, @"C:\Same"),
            Folder(Recent, "   "),
            Folder(Older, @"C:\Same")
        };

        var paths = OpenedFolderSnapshotPublisher.OrderByWindowRank(folders, Ranks());

        CollectionAssert.AreEqual(new[] { @"C:\Same", @"C:\Same" }, paths);
    }

    // The real rank table, over this desktop: every handle it was built from has a rank, and anything else --
    // including the zero handle -- ranks last.
    [TestMethod]
    public void WindowRanks_RealDesktop_AnswersForItsOwnWindowsAndNothingElse()
    {
        var ranks = OpenedFolderSnapshotPublisher.WindowRanks();
        var known = new List<int>();
        foreach (var window in Core.Hook.ExplorerNativeHooks.TopLevelWindowsInZOrder())
            known.Add(ranks(window));

        Assert.IsNotEmpty(known);
        Assert.AreEqual(0, known.Min());
        Assert.AreEqual(int.MaxValue, ranks(IntPtr.Zero));
        Assert.AreEqual(int.MaxValue, ranks(new IntPtr(0x7FFE)));
    }
}
