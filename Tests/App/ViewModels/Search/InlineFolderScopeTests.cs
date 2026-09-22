using System.IO;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Search;

// The inline window's scope is folders: a typed term selects among directories, and every other item
// type is filtered out. These cover the two row sources the window assembles its list from -- the
// folder's own direct listing, and the ranked candidate set BuildQuickResults builds from the engine's
// rows.
[TestClass]
public sealed class InlineFolderScopeTests
{
    private static SearchResult Row(string path, bool isDir) =>
        new() { Path = path, Name = Path.GetFileName(path), IsDir = isDir };

    // The "Current Folder" tier reads a real listing, so this is a real directory: a file and a folder
    // sharing the query's word must not be treated alike.
    [TestMethod]
    public async Task LoadDirectChildren_KeepsFoldersAndDropsFiles()
    {
        var root = Directory.CreateTempSubdirectory("lertaro-inline-folder-scope-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reports-2026"));
            File.WriteAllText(Path.Combine(root, "reports-draft.docx"), string.Empty);

            var localMatches = new List<AppSearchResult>();
            await ExplorerSearchHelper.LoadDirectChildrenAsync(
                "reports", 50, root, localMatches, CancellationToken.None).ConfigureAwait(false);

            CollectionAssert.AreEqual(
                new[] { "reports-2026" },
                localMatches.Select(row => row.Name).ToList());
            Assert.IsTrue(localMatches.Single().IsDir);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void BuildQuickResults_InlineWindow_KeepsOnlyFolderRows()
    {
        var fileResults = new List<SearchResult>
        {
            Row(@"C:\Notes\report.docx", isDir: false),
            Row(@"C:\Reports", isDir: true),
            Row(@"C:\Desktop\report-final", isDir: true),
        };

        var result = SearchResultMapper.BuildQuickResults(
            fileResults, "report", scope: null, contextDirectory: null, isInlineWindow: true);

        CollectionAssert.AreEqual(
            new[] { @"C:\Reports", @"C:\Desktop\report-final" },
            result.Select(row => row.FullPath).ToList());
    }

    // The quick and full windows are deliberately untouched by this scope (decided 2026-09-22: whatever
    // those windows find is what they return), so the same rows must still come back with the file in.
    [TestMethod]
    public void BuildQuickResults_QuickWindow_StillListsFiles()
    {
        var fileResults = new List<SearchResult>
        {
            Row(@"C:\Notes\report.docx", isDir: false),
            Row(@"C:\Reports", isDir: true),
        };

        var result = SearchResultMapper.BuildQuickResults(
            fileResults, "report", scope: null, contextDirectory: null, isInlineWindow: false);

        Assert.HasCount(2, result);
        Assert.IsTrue(result.Any(row => row.FullPath == @"C:\Notes\report.docx" && !row.IsDir));
    }

    // Wiring guard for the third source, which no unit test can execute: SearchStreamRenderer needs a
    // live SearchService and a WPF dispatcher, so its foldersOnly argument is only observable at the call
    // sites. It decides whether the widened inline budget fills with folders or is spent on rows the
    // window then drops, so a silently reverted named argument would take the feature's use away without
    // failing anything else here.
    [TestMethod]
    public void BothInlinePathsAskTheRendererForFoldersOnly()
    {
        var engine = Source("App/ViewModels/Search/SearchExecutionEngine.cs");

        // The folder-scoped inline path is only ever reached with isInlineSearchContext already true.
        var inlineRender = Between(engine, "private async Task RenderInlineSearchAsync(", "private void EmitInstantResults");
        Assert.Contains("foldersOnly: true", inlineRender,
            "the inline window's own-folder path must restrict the streamed rows to folders");
        Assert.Contains("foldersOnly: isInlineSearchContext", engine,
            "and so must the path taken when the inline window has no folder context yet");

        var fullWindow = Source("App/ViewModels/Search/Dispatch/SearchQueryDispatchController.cs");
        Assert.DoesNotContain("foldersOnly", fullWindow,
            "the full window keeps every type it finds -- the scope is the inline window's alone");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
