using System.IO;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Search;

// The card offers folders only over a dialog whose target field takes nothing but a folder. Over an Open/Save
// dialog, where the name box wants a file, and typed into an Explorer window's own search box, where the card
// is that window's search, it keeps finding files -- so these cover both halves: what the scope filters out,
// and what everything else must still get.
[TestClass]
public sealed class InlineFolderScopeTests
{
    private static SearchResult Row(string path, bool isDir) =>
        new() { Path = path, Name = Path.GetFileName(path), IsDir = isDir };

    private static List<SearchResult> OneFileAndTwoFolders() =>
    [
        Row(@"C:\Notes\report.docx", isDir: false),
        Row(@"C:\Reports", isDir: true),
        Row(@"C:\Desktop\report-final", isDir: true),
    ];

    [TestMethod]
    public void BuildQuickResults_FolderScope_KeepsOnlyFolderRows()
    {
        var result = SearchResultMapper.BuildQuickResults(
            OneFileAndTwoFolders(), "report", scope: null, contextDirectory: null,
            isInlineWindow: true, folderScope: true);

        CollectionAssert.AreEqual(
            new[] { @"C:\Reports", @"C:\Desktop\report-final" },
            result.Select(row => row.FullPath).ToList());
    }

    // The reported regression: an Explorer window's own search box lost files when the dialog scope was
    // applied to every inline search. isInlineWindow alone must not filter anything.
    [TestMethod]
    public void BuildQuickResults_ExplorerWindowSearch_KeepsFiles()
    {
        var result = SearchResultMapper.BuildQuickResults(
            OneFileAndTwoFolders(), "report", scope: null, contextDirectory: null,
            isInlineWindow: true, folderScope: false);

        Assert.HasCount(3, result);
        Assert.IsTrue(result.Any(row => row.FullPath == @"C:\Notes\report.docx"));
    }

    // The quick and full windows are deliberately untouched by the scope (decided 2026-09-22: whatever those
    // windows find is what they return).
    [TestMethod]
    public void BuildQuickResults_QuickWindow_KeepsFiles()
    {
        var result = SearchResultMapper.BuildQuickResults(
            OneFileAndTwoFolders(), "report", scope: null, contextDirectory: null, isInlineWindow: false);

        Assert.HasCount(3, result);
        Assert.IsTrue(result.Any(row => row.FullPath == @"C:\Notes\report.docx" && !row.IsDir));
    }

    // The "Current Folder" tier reads a real listing, so this is a real directory: a file and a folder
    // sharing the query's word must be treated differently by the two scopes and only by the dialog one.
    [TestMethod]
    public async Task LoadDirectChildren_FolderScope_KeepsFoldersAndDropsFiles()
    {
        var root = Directory.CreateTempSubdirectory("lertaro-inline-folder-scope-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reports-2026"));
            File.WriteAllText(Path.Combine(root, "reports-draft.docx"), string.Empty);

            var scoped = await Locate(root, folderScope: true);
            CollectionAssert.AreEqual(new[] { "reports-2026" }, scoped.Select(row => row.Name).ToList());
            Assert.IsTrue(scoped.Single().IsDir);

            var unscoped = await Locate(root, folderScope: false);
            CollectionAssert.AreEquivalent(
                new[] { "reports-2026", "reports-draft.docx" },
                unscoped.Select(row => row.Name).ToList());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<List<AppSearchResult>> Locate(string directory, bool folderScope)
    {
        var localMatches = new List<AppSearchResult>();
        await ExplorerSearchHelper.LoadDirectChildrenAsync(
            "reports", 50, directory, localMatches, CancellationToken.None, folderScope: folderScope).ConfigureAwait(false);
        return localMatches;
    }

    // Wiring guards. Both consumers need the same answer and neither can be executed here: the renderer needs
    // a live SearchService and a WPF dispatcher, and the scope itself comes from the window classification the
    // hook reports. A silently reverted named argument would take the folder scope away from the pickers that
    // need it, or put it back onto every dialog and Explorer window -- both of which are the bug this fixes.
    [TestMethod]
    public void TheFolderScopeIsAskedOfTheDialogTargetAndNotOfTheInlineWindow()
    {
        var engine = Source("App/ViewModels/Search/SearchExecutionEngine.cs");

        Assert.Contains("folderScope = isInlineSearchContext && dialogAdapter?.TargetIsFolderOnly == true", engine,
            "the engine's scope must be inline AND a folder-only target");
        Assert.HasCount(2, LinesWith(engine, "foldersOnly: folderScope"),
            "and both renderer paths must take that answer rather than the bare inline flag");
        Assert.IsEmpty(LinesWith(engine, "foldersOnly: isInlineSearchContext"),
            "no path may fall back to scoping every inline search");

        var dispatch = Source("App/ViewModels/Search/Dispatch/SearchDispatchController.cs");

        Assert.Contains("_getIsInlineSearchContext() && InlineSearchManager.Instance.ExplorerTracker.ActiveAdapter?.TargetIsFolderOnly == true", dispatch,
            "the dispatcher must derive the same scope, not its own wider one");
        Assert.Contains("folderScope: folderScope", dispatch,
            "and hand it to the mapper, whose history and favorite rows bypass the engine filter");
        Assert.Contains("hasTokens || hasScope || folderScope", dispatch,
            "the widened budget belongs to the scope alone, so an Explorer search keeps its 51 rows");
    }

    private static List<string> LinesWith(string source, string needle) =>
        source.Split('\n').Where(line => line.Contains(needle, StringComparison.Ordinal)).Select(line => line.Trim()).ToList();

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
