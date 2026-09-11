namespace Lertaro.Plugins.DirectoryOpus.Tests;

[TestClass]
public sealed class DirectoryOpusInlineSearchAdapterTests
{
    private static readonly IntPtr SomeHwnd = (IntPtr)1;

    [TestMethod]
    public void CanTrigger_FileDisplayClass_ReturnsTrue()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.filedisplay"));
    }

    [TestMethod]
    public void CanTrigger_FileDisplayContainerClass_ReturnsTrue()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.filedisplaycontainer"));
    }

    [TestMethod]
    public void CanTrigger_IconFileDisplayClass_ReturnsTrue()
    {
        // Thumbnails/Tiles/Large Icons view modes focus this class instead of "dopus.filedisplay" --
        // previously unrecognized, so inline search never triggered in those view modes.
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.iconfiledisplay"));
    }

    [TestMethod]
    public void CanTrigger_UnrelatedClass_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(SomeHwnd, "dopus.lister"));
    }

    [TestMethod]
    public void CanTrigger_ZeroHwnd_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(IntPtr.Zero, "dopus.filedisplay"));
    }

    [TestMethod]
    public void CanTrigger_EmptyClassName_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(SomeHwnd, ""));
    }

    // Regression guard for live selection mirroring: DOpus's `Select` verb only selects; without
    // MAKEVISIBLE the item can stay outside the visible rows, so a sync appears to do nothing. Explorer's
    // adapter gets the equivalent for free from svsiEnsureVisible, which is why only DOpus looked broken.
    [TestMethod]
    public void SelectArguments_IncludeMakeVisible()
    {
        Assert.Contains("MAKEVISIBLE", DirectoryOpusInlineSearchAdapter.SelectArguments);
        Assert.Contains("DESELECTNOMATCH", DirectoryOpusInlineSearchAdapter.SelectArguments);
    }

    // The live mirror must NOT take focus. It fires while the user is still typing in the search box, and
    // SETFOCUS moves Directory Opus's file display focus onto the item -- after which its own type-ahead
    // "quick find" can pick up the next keystroke and jump the listing. Explorer's adapter only ever sets
    // the selection, never the focus.
    [TestMethod]
    public void SelectArguments_LiveSync_DoNotStealFocus() =>
        Assert.DoesNotContain("SETFOCUS", DirectoryOpusInlineSearchAdapter.SelectArguments);

    // The commit path is the opposite case: the window is already hidden and the user asked to land on this
    // item in Directory Opus, so focus SHOULD move to it.
    [TestMethod]
    public void SelectAndFocusArguments_CommitPath_DoMoveFocus()
    {
        Assert.Contains("SETFOCUS", DirectoryOpusInlineSearchAdapter.SelectAndFocusArguments);
        Assert.Contains("MAKEVISIBLE", DirectoryOpusInlineSearchAdapter.SelectAndFocusArguments);
    }

    // A folder result arrives with a trailing separator (the sender marks directories that way), and
    // Path.GetDirectoryName reads "C:\Root\Sub\" as the folder ITSELF rather than its parent -- so the
    // naive compare put every folder outside the open folder and no folder was ever mirrored. Files, which
    // carry no separator, were unaffected, which is exactly the reported split.
    [TestMethod]
    public void IsInFolder_FolderWithTrailingSeparator_IsInTheOpenFolder() =>
        Assert.IsTrue(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root", @"C:\Root\Sub\"));

    [TestMethod]
    public void IsInFolder_FolderWithoutTrailingSeparator_IsInTheOpenFolder() =>
        Assert.IsTrue(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root", @"C:\Root\Sub"));

    [TestMethod]
    public void IsInFolder_File_IsInTheOpenFolder() =>
        Assert.IsTrue(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root", @"C:\Root\file.txt"));

    [TestMethod]
    public void IsInFolder_TrailingSeparatorOnTheOpenFolder_IsIgnored() =>
        Assert.IsTrue(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root\", @"C:\Root\file.txt"));

    [TestMethod]
    public void IsInFolder_IsCaseInsensitive() =>
        Assert.IsTrue(DirectoryOpusInlineSearchAdapter.IsInFolder(@"c:\root", @"C:\ROOT\file.txt"));

    [TestMethod]
    public void IsInFolder_ItemInASubfolder_IsNotInTheOpenFolder() =>
        Assert.IsFalse(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root", @"C:\Root\Sub\file.txt"));

    [TestMethod]
    public void IsInFolder_ItemOnAnotherDrive_IsNotInTheOpenFolder() =>
        Assert.IsFalse(DirectoryOpusInlineSearchAdapter.IsInFolder(@"C:\Root", @"D:\Other\file.txt"));

    [TestMethod]
    public void IsInFolder_EmptyScope_IsNeverInFolder() =>
        Assert.IsFalse(DirectoryOpusInlineSearchAdapter.IsInFolder("", @"C:\Root\file.txt"));
}
