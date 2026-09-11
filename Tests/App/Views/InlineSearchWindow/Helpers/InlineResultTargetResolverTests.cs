using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// Which inline-search row the window acts on: the default selection, what Enter runs, and what the host
// file manager highlights.
//
// The reported problem, twice over: the inline list leads with rows that are NOT files (shortcut commands
// / plugin actions, instant results, section headers -- see PluginSearchResultMapper, which inserts them
// ahead of every real result). Landing on one of those meant (a) the host had no path to mirror, and
// (b) Enter ran the command instead of opening the file the user was searching for.
[TestClass]
public sealed class InlineResultTargetResolverTests
{
    private static AppSearchResult File(string path, bool isDir = false) =>
        new() { Name = System.IO.Path.GetFileName(path), FullPath = path, ResultKind = "File", IsDir = isDir };

    private static AppSearchResult PluginAction() => new()
    {
        Name = "快捷命令",
        FullPath = "__PLUGIN_ACTION__:42",
        ResultKind = "PluginAction",
    };

    private static AppSearchResult Header(string title = "Current Folder") => new()
    {
        Name = title,
        FullPath = "__SECTION_HEADER__",
        ResultKind = "SectionHeader",
    };

    private static AppSearchResult Instant() => new()
    {
        Name = "https://example.com",
        FullPath = "https://example.com",
        ResultKind = "InstantResult",
    };

    // Shortcut commands lead and the default selection lands on one, so Enter runs it -- the intended
    // behavior for a query that IS a command keyword. (Turning the commands off is what makes searches
    // purely about files; see UserSettings.EnableSearchActions.)
    [TestMethod]
    public void AutoSelect_ShortcutCommandLeads_CommandIsSelected()
    {
        List<AppSearchResult> rows = [PluginAction(), Header(), File(@"C:\Root\report.txt")];

        Assert.AreEqual(0, InlineResultTargetResolver.ResolveAutoSelectIndex(rows));
    }

    [TestMethod]
    public void AutoSelect_FirstSelectableRowIsUsed_HeadersSkipped()
    {
        // The list opens with a section header, which must not hold the selection.
        List<AppSearchResult> rows = [Header(), File(@"C:\Root\report.txt")];

        Assert.AreEqual(1, InlineResultTargetResolver.ResolveAutoSelectIndex(rows));
    }

    [TestMethod]
    public void AutoSelect_InstantResultLeads_InstantResultIsSelected()
    {
        List<AppSearchResult> rows = [Header(), Instant()];

        Assert.AreEqual(1, InlineResultTargetResolver.ResolveAutoSelectIndex(rows));
    }

    [TestMethod]
    public void AutoSelect_HeadersOnly_SelectsNothing()
    {
        Assert.AreEqual(-1, InlineResultTargetResolver.ResolveAutoSelectIndex([Header(), Header("Global")]));
        Assert.AreEqual(-1, InlineResultTargetResolver.ResolveAutoSelectIndex([]));
    }

    [TestMethod]
    public void AutoSelect_RealResultIsSelectedWhenNothingPrecedesIt()
    {
        List<AppSearchResult> rows = [Header(), File(@"C:\Root\Sub", isDir: true)];

        Assert.AreEqual(1, InlineResultTargetResolver.ResolveAutoSelectIndex(rows));
    }

    // Enter acts on the selected row when it is selectable...
    [TestMethod]
    public void Enter_SelectedRealRow_RunsThatRow()
    {
        List<AppSearchResult> rows = [PluginAction(), File(@"C:\Root\a.txt")];

        Assert.AreEqual(@"C:\Root\a.txt", InlineResultTargetResolver.ResolveEnterTarget(rows, 1)!.FullPath);
    }

    // ...and when a DELIBERATE selection is on the plugin action (the user arrowed to it), Enter runs the
    // command. The default-selection preference must not override an explicit user choice.
    [TestMethod]
    public void Enter_SelectedPluginAction_RunsTheCommand()
    {
        List<AppSearchResult> rows = [PluginAction(), File(@"C:\Root\a.txt")];

        Assert.AreEqual("__PLUGIN_ACTION__:42", InlineResultTargetResolver.ResolveEnterTarget(rows, 0)!.FullPath);
    }

    // Nothing selected (the list was just replaced): Enter must NOT run a section header at index 0 -- it
    // falls back to the row the default selection would pick, which here is the shortcut command.
    [TestMethod]
    public void Enter_NothingSelected_FallsBackToTheDefaultRowRatherThanIndexZero()
    {
        List<AppSearchResult> rows = [Header(), PluginAction(), File(@"C:\Root\a.txt")];

        Assert.AreEqual("__PLUGIN_ACTION__:42", InlineResultTargetResolver.ResolveEnterTarget(rows, -1)!.FullPath);
    }

    // ...and when a real file leads the list (no command/instant row ahead of it), that is what Enter runs.
    [TestMethod]
    public void Enter_NothingSelected_AndAFileLeads_RunsTheFile()
    {
        List<AppSearchResult> rows = [Header(), File(@"C:\Root\a.txt")];

        Assert.AreEqual(@"C:\Root\a.txt", InlineResultTargetResolver.ResolveEnterTarget(rows, -1)!.FullPath);
    }

    [TestMethod]
    public void Enter_NothingSelectableAtAll_ReturnsNull()
    {
        Assert.IsNull(InlineResultTargetResolver.ResolveEnterTarget([Header()], -1));
        Assert.IsNull(InlineResultTargetResolver.ResolveEnterTarget([], -1));
    }

    // Host mirroring follows the same preference, so the host highlights the row Enter would open.
    [TestMethod]
    public void Mirror_SelectedShortcutCommand_UsesTheRealResult()
    {
        List<AppSearchResult> rows = [PluginAction(), Header(), File(@"C:\Root\report.txt")];

        var target = InlineResultTargetResolver.ResolveMirrorTarget(rows, selectedIndex: 0, windowDirectory: @"C:\Root");

        Assert.IsNotNull(target);
        Assert.AreEqual(@"C:\Root\report.txt", target.Value.Path);
    }

    [TestMethod]
    public void Mirror_NothingRealMatched_FallsBackToTheWindowDirectory()
    {
        List<AppSearchResult> rows = [PluginAction(), Header(), Instant()];

        var target = InlineResultTargetResolver.ResolveMirrorTarget(rows, selectedIndex: 0, windowDirectory: @"C:\Root");

        Assert.IsNotNull(target);
        Assert.AreEqual(@"C:\Root", target.Value.Path);
        Assert.IsTrue(target.Value.IsDir);
    }

    [TestMethod]
    public void Mirror_NothingRealAndNoDirectory_ReturnsNull()
    {
        List<AppSearchResult> rows = [PluginAction(), Header()];

        Assert.IsNull(InlineResultTargetResolver.ResolveMirrorTarget(rows, 0, null));
        Assert.IsNull(InlineResultTargetResolver.ResolveMirrorTarget(rows, 0, "   "));
    }

    [TestMethod]
    public void Mirror_SelectedRealRow_IsMirroredDirectly()
    {
        List<AppSearchResult> rows = [PluginAction(), File(@"C:\Root\a.txt"), File(@"C:\Root\b.txt")];

        var target = InlineResultTargetResolver.ResolveMirrorTarget(rows, selectedIndex: 2, windowDirectory: @"C:\Root");

        Assert.AreEqual(@"C:\Root\b.txt", target!.Value.Path);
    }

    [TestMethod]
    public void Mirror_DirectoryResult_ReportsIsDir()
    {
        List<AppSearchResult> rows = [File(@"C:\Root\Sub", isDir: true)];

        var target = InlineResultTargetResolver.ResolveMirrorTarget(rows, selectedIndex: 0, windowDirectory: @"C:\Root");

        Assert.IsTrue(target!.Value.IsDir);
    }

    [TestMethod]
    public void Mirror_EmptyList_FallsBackToTheWindowDirectory()
    {
        Assert.AreEqual(@"C:\Root", InlineResultTargetResolver.ResolveMirrorTarget([], -1, @"C:\Root")!.Value.Path);
    }

    // Only a real, fully-qualified path may be handed to an adapter.
    [TestMethod]
    public void IsRealLocation_RejectsEveryNonPathRow()
    {
        foreach (var row in new[]
        {
            PluginAction(), Header(), Instant(),
            File("__SHOW_MORE__"), File("__NO_RESULTS__"), File("__SEARCHABLE_ITEM__:App:Title"),
        })
            Assert.IsFalse(InlineResultTargetResolver.IsRealLocation(row, out _, out _), row.Name);
    }

    [TestMethod]
    public void IsRealLocation_RejectsNullEmptyAndRelativePaths()
    {
        Assert.IsFalse(InlineResultTargetResolver.IsRealLocation(null, out _, out _));
        Assert.IsFalse(InlineResultTargetResolver.IsRealLocation(File(""), out _, out _));
        Assert.IsFalse(InlineResultTargetResolver.IsRealLocation(File(@"Root\a.txt"), out _, out _));
    }

    [TestMethod]
    public void IsRealLocation_AcceptsDriveAndUncPaths()
    {
        Assert.IsTrue(InlineResultTargetResolver.IsRealLocation(File(@"C:\Root\a.txt"), out var path, out var isDir));
        Assert.AreEqual(@"C:\Root\a.txt", path);
        Assert.IsFalse(isDir);
        Assert.IsTrue(InlineResultTargetResolver.IsRealLocation(File(@"\\server\share\a.txt"), out _, out _));
    }
}
