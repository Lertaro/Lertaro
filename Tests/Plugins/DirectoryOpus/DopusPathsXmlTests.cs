namespace Lertaro.Plugins.DirectoryOpus.Tests;

// The shape of Opus's own paths output (dopusrt.exe /info <file>,paths): the XML attributes Opus writes,
// and the order the opened-folder list wants them in.
//
// The XML below is a trimmed copy of real output (two listers, two sides each), including the "0x"
// handle prefix Opus uses -- which NumberStyles.HexNumber rejects, so getting that wrong silently
// collapsed every lister into one group.
[TestClass]
public sealed class DopusPathsXmlTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <results command="paths" result="1">
        	<path active_lister="0" display_path="D:\one\a" lister="0x8c0a44" side="1" tab="0x51109c">D:\one\a</path>
        	<path active_lister="0" display_path="D:\one\b" lister="0x8c0a44" side="1" tab="0x161f20">D:\one\b</path>
        	<path active_lister="0" active_tab="1" display_path="D:\one\active" lister="0x8c0a44" side="1" tab="0x4a0a72" tab_state="1">D:\one\active</path>
        	<path active_lister="0" display_path="C:\" lister="0x8c0a44" side="2" tab="0x520de8">C:\</path>
        	<path active_lister="0" active_tab="2" display_path="Z:\two\active" lister="0x8c0a44" side="2" tab="0x3b0948" tab_state="2">Z:\two\active</path>
        	<path active_lister="1" active_tab="1" display_path="C:\Windows" lister="0x2a019c" side="1" tab="0x990c48" tab_state="1">C:\Windows</path>
        	<path active_lister="1" display_path="C:\Users" lister="0x2a019c" side="1" tab="0x690b8e">C:\Users</path>
        	<path active_lister="1" active_tab="2" display_path="Z:\other" lister="0x2a019c" side="2" tab="0xef0b26" tab_state="2">Z:\other</path>
        </results>
        """;

    [TestMethod]
    public void ParseTabs_ReadsEveryTabWithItsListerSideAndActiveFlag()
    {
        var tabs = DopusPathsXml.ParseTabs(Xml);

        Assert.HasCount(8, tabs);
        Assert.AreEqual(new IntPtr(0x8c0a44), tabs[0].Lister);
        Assert.AreEqual(1, tabs[0].Side);
        Assert.AreEqual(@"D:\one\a", tabs[0].Path);
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
        var tabs = DopusPathsXml.ParseTabs(Xml);

        Assert.AreEqual(@"C:\", tabs[3].Path);
        Assert.AreEqual(@"D:\one\b", tabs[1].Path);
    }

    [TestMethod]
    public void ParseTabs_EmptyResult_IsNoTabsRatherThanAnError() => Assert.IsEmpty(DopusPathsXml.ParseTabs("""<?xml version="1.0"?><results command="paths" result="1" />"""));

    // Regression guard for the bug that made every tab under a localized folder name unusable: on a
    // non-English Windows, Opus localizes the display_path ATTRIBUTE while the element TEXT stays the
    // real path. The XML below is the verbatim shape measured on a live install, where the plugin's
    // folder list showed only the tab whose display_path happened to be language-neutral.
    [TestMethod]
    public void ParseTabs_ReadsTheRealPathFromTheElementTextNotTheLocalizedDisplayPath()
    {
        var tabs = DopusPathsXml.ParseTabs("""
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
            DopusPathsXml.ChooseReportedPath(@"C:\Users\testuser\AppData\Local\Temp", @"C:\用户\testuser\AppData\Local\Temp"));
        Assert.AreEqual(@"C:\Windows", DopusPathsXml.ChooseReportedPath(@"C:\Windows", null));
        Assert.AreEqual(@"C:\Windows", DopusPathsXml.ChooseReportedPath(null, @"C:\Windows"));
        Assert.AreEqual(@"C:\Windows", DopusPathsXml.ChooseReportedPath("   ", @"C:\Windows"));
        Assert.IsNull(DopusPathsXml.ChooseReportedPath(null, null));
    }

    // The requested order: each group's ACTIVE tab first (one per group, groups in Opus's order), then
    // each group's remaining tabs in that same group order.
    [TestMethod]
    public void OrderTabs_ActiveTabOfEveryGroupComesFirst()
    {
        var ordered = DopusPathsXml.OrderTabs(DopusPathsXml.ParseTabs(Xml));

        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\one\active",   // lister 1, side 1 -- active
                @"Z:\two\active",   // lister 1, side 2 -- active
                @"C:\Windows",      // lister 2, side 1 -- active
                @"Z:\other",        // lister 2, side 2 -- active
                @"D:\one\a", @"D:\one\b",   // lister 1, side 1 -- the rest, in Opus's order
                @"C:\",                      // lister 1, side 2 -- the rest
                @"C:\Users",                 // lister 2, side 1 -- the rest
            },
            ordered.Select(tab => tab.Path).ToArray());
    }

    // Same folder twice in one lister is one entry; the same folder in two listers stays two entries.
    [TestMethod]
    public void ToOpenedFolders_CollapsesDuplicatesPerListerOnly()
    {
        var tabs = DopusPathsXml.ParseTabs("""
            <results command="paths" result="1">
            	<path display_path="C:\Windows" lister="0x1" side="1" tab="0x11">C:\Windows</path>
            	<path display_path="c:\windows" lister="0x1" side="1" tab="0x12">c:\windows</path>
            	<path display_path="C:\Windows" lister="0x2" side="1" tab="0x21">C:\Windows</path>
            </results>
            """);

        var folders = DopusPathsXml.ToOpenedFolders(tabs);

        Assert.HasCount(2, folders);
        Assert.AreEqual(@"C:\Windows", folders[0].Path);
        Assert.AreEqual(new IntPtr(0x1), folders[0].WindowHandle);
        Assert.AreEqual(new IntPtr(0x2), folders[1].WindowHandle);
    }
}
