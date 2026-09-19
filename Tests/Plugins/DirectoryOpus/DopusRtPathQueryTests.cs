using System.Diagnostics;
using System.Reflection;

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
        var tabs = DopusRtPathQuery.ParseTabs(Xml);

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
        var tabs = DopusRtPathQuery.ParseTabs(Xml);

        Assert.AreEqual(@"C:\", tabs[3].Path);
        Assert.AreEqual(@"D:\one\b", tabs[1].Path);
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

    // Opus refuses a non-ASCII output path, and a double quote cannot be escaped inside the quoted form
    // the /info argument uses, so both are rejected. A SPACE is not: the path is quoted in the argument,
    // so a temp directory containing one -- a real user name, or a redirected %TEMP% -- is usable and
    // must not be skipped.
    [TestMethod]
    public void IsOpusSafePath_AllowsSpacesAndRejectsNonAsciiAndQuotes()
    {
        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\testuser\AppData\Local\Temp\lertaro-dopusrt-1a2b.xml"));
        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\Some Name\AppData\Local\Temp\a.xml"));
        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(@"D:\tmp\new test\paths.txt"));
        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\USER~1\AppData\Local\Temp\lertaro-dopusrt-1a2b.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath(@"C:\Users\张三\AppData\Local\Temp\a.xml"));
        Assert.IsFalse(DopusRtPathQuery.IsOpusSafePath("C:\\tmp\\new\"test\\a.xml"));
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
            // No writable ASCII, space-free directory at all: the plugin degrades to the scrape
            // fallback, which the collector's own tests cover.
            Assert.IsNull(second);
            return;
        }

        Assert.IsTrue(DopusRtPathQuery.IsOpusSafePath(first));
        Assert.AreNotEqual(first, second);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(first)));

        // dopusrt only fills in a file that already exists, so the path this returns must already have
        // been created -- empty -- by the time the caller runs the tool.
        Assert.IsTrue(File.Exists(first), $"'{first}' should already exist for dopusrt to fill in");
        Assert.AreEqual(0, new FileInfo(first).Length);

        File.Delete(first);
    }

    // End-to-end over a path that CONTAINS A SPACE, which is the whole reason the /info argument quotes
    // the path. This drives the real tool against a real Opus, so it needs both installed; on a machine
    // without them there is nothing to query and the test stands down rather than failing.
    //
    // It calls the query's own tool runner directly because %TEMP% cannot be redirected in-process --
    // Path.GetTempPath() caches its answer -- so the space would otherwise never reach the argument.
    [TestMethod]
    public void RunTool_FillsInAPreCreatedFileWhosePathContainsASpace()
    {
        var output = Path.Combine(Path.GetTempPath(), "lertaro space test", $"paths-{Guid.NewGuid():N}.xml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // dopusrt only fills in a file that is already there.
            File.WriteAllBytes(output, []);

            var runner = typeof(DopusRtPathQuery).GetMethod(
                "RunTool", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(runner, "RunTool should still exist for this regression guard to work");

            // The third element receives the out parameter (whether the tool exited on its own).
            runner.Invoke(null, [FindDopusRt() ?? @"C:\Program Files\GPSoftware\Directory Opus\dopusrt.exe", output, null]);

            // Opus writes the file asynchronously, so poll exactly as the production reader does rather
            // than assuming the content is on disk the moment the process exits.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (new FileInfo(output).Length == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);

            var file = new FileInfo(output);
            Assert.IsTrue(file.Exists);
            Assert.IsGreaterThan(0, file.Length, "dopusrt wrote nothing, so the quoted space-bearing path was not accepted");

            // The XML itself is the proof, not a tab count: with no tabs open Opus legitimately answers an
            // empty <results />, which still shows the tool accepted the path and wrote its output.
            Assert.Contains("<results", File.ReadAllText(output));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(output)!, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The installed <c>dopusrt.exe</c>, taken from the running Opus where possible, or null when Opus is
    /// not installed on this machine.
    /// </summary>
    private static string? FindDopusRt()
    {
        foreach (var process in Process.GetProcessesByName("dopus"))
        {
            try
            {
                var directory = Path.GetDirectoryName(process.MainModule?.FileName);
                if (directory != null)
                {
                    var tool = Path.Combine(directory, "dopusrt.exe");
                    if (File.Exists(tool)) return tool;
                }
            }
            catch { /* elevated Opus: fall through */ }
            finally { process.Dispose(); }
        }

        return null;
    }

    // A live log caught the plugin handing dopusrt a path in a directory that passed the character rules
    // but refused the file: dopusrt then exits 0 having written nothing, so EVERY query silently fell
    // through to the scrape (which only sees the focused tab) and paid the full 2s wait. Being ASCII and
    // space-free is therefore not the contract -- being writable is.
    [TestMethod]
    public void CreateOutputPath_OnlyReturnsADirectoryThatCanActuallyHoldTheFile()
    {
        var path = DopusRtPathQuery.CreateOutputPath();
        if (path == null) return; // covered by the test above

        var directory = Path.GetDirectoryName(path)!;

        // The same test the shipped code performs, asserted independently here so the guarantee is
        // pinned by a test rather than only by the production probe.
        var probe = Path.Combine(directory, $"lertaro-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probe, []);
        }
        finally
        {
            try { File.Delete(probe); } catch { /* best effort */ }
        }
    }
}
