namespace Lertaro.Plugins.DirectoryOpus.Tests;

// The two pure halves of reading Opus's own paths output (dopusrt.exe /info <file>,paths): the XML
// attributes Opus writes, and the order the opened-folder list wants them in.
//
// The XML below is a trimmed copy of real output (two listers, two sides each), including the "0x"
// handle prefix Opus uses -- which NumberStyles.HexNumber rejects, so getting that wrong silently
// collapsed every lister into one group.
[TestClass]
public sealed class DopusRtPathQueryTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <results command="paths" result="1">
        	<path active_lister="0" display_path="F:\one\a" lister="0x8c0a44" side="1" tab="0x51109c">F:\one\a</path>
        	<path active_lister="0" display_path="F:\one\b" lister="0x8c0a44" side="1" tab="0x161f20">F:\one\b</path>
        	<path active_lister="0" active_tab="1" display_path="F:\one\active" lister="0x8c0a44" side="1" tab="0x4a0a72" tab_state="1">F:\one\active</path>
        	<path active_lister="0" display_path="C:\" lister="0x8c0a44" side="2" tab="0x520de8">C:\</path>
        	<path active_lister="0" active_tab="2" display_path="E:\two\active" lister="0x8c0a44" side="2" tab="0x3b0948" tab_state="2">E:\two\active</path>
        	<path active_lister="1" active_tab="1" display_path="C:\Windows" lister="0x2a019c" side="1" tab="0x990c48" tab_state="1">C:\Windows</path>
        	<path active_lister="1" display_path="C:\Users" lister="0x2a019c" side="1" tab="0x690b8e">C:\Users</path>
        	<path active_lister="1" active_tab="2" display_path="E:\other" lister="0x2a019c" side="2" tab="0xef0b26" tab_state="2">E:\other</path>
        </results>
        """;

    [TestMethod]
    public void ParseTabs_ReadsEveryTabWithItsListerSideAndActiveFlag()
    {
        var tabs = DopusRtPathQuery.ParseTabs(Xml);

        Assert.HasCount(8, tabs);
        Assert.AreEqual(new IntPtr(0x8c0a44), tabs[0].Lister);
        Assert.AreEqual(1, tabs[0].Side);
        Assert.AreEqual(@"F:\one\a", tabs[0].Path);
        Assert.IsFalse(tabs[0].IsActive);
        // The active tab of a group is the only one carrying active_tab.
        Assert.IsTrue(tabs[2].IsActive);
        Assert.AreEqual(2, tabs[4].Side);
        Assert.IsTrue(tabs[4].IsActive);
        // A second lister is a different group even where the side number matches.
        Assert.AreEqual(new IntPtr(0x2a019c), tabs[5].Lister);
        Assert.IsTrue(tabs[5].IsActive);
        Assert.IsFalse(tabs[6].IsActive);
    }

    // A drive root keeps its trailing separator (matching the rest of the collector); a path Opus reports
    // without one is left exactly as reported.
    [TestMethod]
    public void ParseTabs_NormalizesADriveRootButLeavesOtherPathsAlone()
    {
        var tabs = DopusRtPathQuery.ParseTabs(Xml);

        Assert.AreEqual(@"C:\", tabs[3].Path);
        Assert.AreEqual(@"F:\one\b", tabs[1].Path);
    }

    [TestMethod]
    public void ParseTabs_EmptyResult_IsNoTabsRatherThanAnError() => Assert.IsEmpty(DopusRtPathQuery.ParseTabs("""<?xml version="1.0"?><results command="paths" result="1" />"""));

    // Regression guard for the bug that made every tab under a localized folder name unusable: on a
    // non-English Windows, Opus localizes the display_path ATTRIBUTE while the element TEXT stays the
    // real path. The XML below is the verbatim shape measured on a live install, where the plugin's
    // folder list showed only the tab whose display_path happened to be language-neutral.
    [TestMethod]
    public void ParseTabs_ReadsTheRealPathFromTheElementTextNotTheLocalizedDisplayPath()
    {
        var tabs = DopusRtPathQuery.ParseTabs("""
            <?xml version="1.0" encoding="UTF-8"?>
            <results command="paths" result="1">
            	<path active_lister="1" display_path="C:\" lister="0xcb07c2" side="2" tab="0x160f4e">C:\</path>
            	<path active_lister="1" active_tab="2" display_path="C:\用户\testuser\AppData\Local\Temp" lister="0xcb07c2" side="2" tab="0x120fda" tab_state="2">C:\Users\testuser\AppData\Local\Temp</path>
            </results>
            """);

        Assert.HasCount(2, tabs);
        Assert.AreEqual(@"C:\Users\testuser\AppData\Local\Temp", tabs[1].Path);
        Assert.IsTrue(tabs[1].IsActive);
    }

    // The fallback only matters for output that carries no element text; the attribute is still read
    // rather than dropped, so such an entry is not silently lost.
    [TestMethod]
    public void ChooseReportedPath_PrefersElementTextAndFallsBackToTheDisplayPath()
    {
        Assert.AreEqual(
            @"C:\Users\testuser\AppData\Local\Temp",
            DopusRtPathQuery.ChooseReportedPath(@"C:\Users\testuser\AppData\Local\Temp", @"C:\用户\testuser\AppData\Local\Temp"));
        Assert.AreEqual(@"C:\Windows", DopusRtPathQuery.ChooseReportedPath(@"C:\Windows", null));
        Assert.AreEqual(@"C:\Windows", DopusRtPathQuery.ChooseReportedPath(null, @"C:\Windows"));
        Assert.AreEqual(@"C:\Windows", DopusRtPathQuery.ChooseReportedPath("   ", @"C:\Windows"));
        Assert.IsNull(DopusRtPathQuery.ChooseReportedPath(null, null));
    }

    // The requested order: each group's ACTIVE tab first (one per group, groups in Opus's order), then
    // each group's remaining tabs in that same group order.
    [TestMethod]
    public void OrderTabs_ActiveTabOfEveryGroupComesFirst()
    {
        var ordered = DopusRtPathQuery.OrderTabs(DopusRtPathQuery.ParseTabs(Xml));

        CollectionAssert.AreEqual(
            new[]
            {
                @"F:\one\active",   // lister 1, side 1 -- active
                @"E:\two\active",   // lister 1, side 2 -- active
                @"C:\Windows",      // lister 2, side 1 -- active
                @"E:\other",        // lister 2, side 2 -- active
                @"F:\one\a", @"F:\one\b",   // lister 1, side 1 -- the rest, in Opus's order
                @"C:\",                      // lister 1, side 2 -- the rest
                @"C:\Users",                 // lister 2, side 1 -- the rest
            },
            ordered.Select(tab => tab.Path).ToArray());
    }

    // Same folder twice in one lister is one entry; the same folder in two listers stays two entries.
    [TestMethod]
    public void ToOpenedFolders_CollapsesDuplicatesPerListerOnly()
    {
        var tabs = DopusRtPathQuery.ParseTabs("""
            <results command="paths" result="1">
            	<path display_path="C:\Windows" lister="0x1" side="1" tab="0x11">C:\Windows</path>
            	<path display_path="c:\windows" lister="0x1" side="1" tab="0x12">c:\windows</path>
            	<path display_path="C:\Windows" lister="0x2" side="1" tab="0x21">C:\Windows</path>
            </results>
            """);

        var folders = DopusRtPathQuery.ToOpenedFolders(tabs);

        Assert.HasCount(2, folders);
        Assert.AreEqual(@"C:\Windows", folders[0].Path);
        Assert.AreEqual(new IntPtr(0x1), folders[0].WindowHandle);
        Assert.AreEqual(new IntPtr(0x2), folders[1].WindowHandle);
    }

    // Opus refuses an output path containing a space, Chinese or other special character, so the path
    // handed to it has to be validated rather than assumed: %TEMP% is per-user and often is not usable.
    [TestMethod]
    public void IsOpusSafePath_RejectsSpacesAndNonAscii()
    {
        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\USER~1\AppData\Local\Temp\lertaro-dopusrt-1a2b.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\Some Name\AppData\Local\Temp\a.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\张三\AppData\Local\Temp\a.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(@"C:\temp\a b.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(null));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(string.Empty));
    }

    // Every call gets a name nothing else can be holding, so a file left behind by an earlier run can
    // never be read back as this run's folders, and no concurrent query can be writing it.
    [TestMethod]
    public void CreateOutputPath_IsSafeAndFreshPerCall()
    {
        var first = DopusRtPathQuery.CreateOutputPath();
        var second = DopusRtPathQuery.CreateOutputPath();

        if (first == null)
        {
            // A temp directory with neither an ASCII short form nor an ASCII long form: the plugin
            // degrades to the scrape fallback, which the collector's own tests cover.
            Assert.IsNull(second);
            return;
        }

        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(first));
        Assert.AreNotEqual(first, second);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(first)));
    }
}
