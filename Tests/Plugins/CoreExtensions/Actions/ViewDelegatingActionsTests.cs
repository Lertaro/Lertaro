using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// OpenResultAction/OpenResultAsAdminAction/LocateInExplorerAction's Execute has no side effect beyond
// calling the injected IPluginSearchWindow, so a fake view makes it safe to test end to end (unlike every
// other built-in action's Execute, which hits real Clipboard/Shell APIs).
[TestClass]
public sealed class ViewDelegatingActionsTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class FakeWindow : IPluginSearchWindow
    {
        public List<string> LocatedPaths { get; } = [];
        public List<string> OpenedPaths { get; } = [];
        public List<string> OpenedAsAdminPaths { get; } = [];

        public void LocateInExplorerExternal(string path) => LocatedPaths.Add(path);
        public void OpenFileOrFolderExternal(string path) => OpenedPaths.Add(path);
        public void OpenFileOrFolderAsAdminExternal(string path) => OpenedAsAdminPaths.Add(path);
        public void HideWindow() { }
    }

    [TestMethod]
    public void OpenResultAction_Execute_OpensEachResultInOrder()
    {
        var view = new FakeWindow();
        var results = new ISearchResult[]
        {
            new FakeResult { FullPath = @"C:\a.txt" },
            new FakeResult { FullPath = @"C:\b.txt" },
        };

        new OpenResultAction().Execute(results, view);

        CollectionAssert.AreEqual(new[] { @"C:\a.txt", @"C:\b.txt" }, view.OpenedPaths);
        Assert.IsEmpty(view.OpenedAsAdminPaths);
        Assert.IsEmpty(view.LocatedPaths);
    }

    [TestMethod]
    public void OpenResultAsAdminAction_Execute_OpensEachResultAsAdminInOrder()
    {
        var view = new FakeWindow();
        var results = new ISearchResult[]
        {
            new FakeResult { FullPath = @"C:\a.exe" },
            new FakeResult { FullPath = @"C:\b.exe" },
        };

        new OpenResultAsAdminAction().Execute(results, view);

        CollectionAssert.AreEqual(new[] { @"C:\a.exe", @"C:\b.exe" }, view.OpenedAsAdminPaths);
        Assert.IsEmpty(view.OpenedPaths);
        Assert.IsEmpty(view.LocatedPaths);
    }

    [TestMethod]
    public void LocateInExplorerAction_Execute_LocatesEachResultInOrder()
    {
        var view = new FakeWindow();
        var results = new ISearchResult[]
        {
            new FakeResult { FullPath = @"C:\a.txt" },
            new FakeResult { FullPath = @"C:\sub\b.txt" },
        };

        new LocateInExplorerAction().Execute(results, view);

        CollectionAssert.AreEqual(new[] { @"C:\a.txt", @"C:\sub\b.txt" }, view.LocatedPaths);
        Assert.IsEmpty(view.OpenedPaths);
        Assert.IsEmpty(view.OpenedAsAdminPaths);
    }

    [TestMethod]
    public void OpenResultAction_Execute_EmptyResults_CallsViewNothing()
    {
        var view = new FakeWindow();

        new OpenResultAction().Execute(Array.Empty<ISearchResult>(), view);

        Assert.IsEmpty(view.OpenedPaths);
    }
}
