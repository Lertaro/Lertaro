using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// Execute() launches a real cmd.exe process (Process.Start) with no injectable seam -- deliberately not
// exercised here. CanExecute/IsVisibleInMenu/IsVisibleInSearch are pure and safe to test directly.
[TestClass]
public sealed class OpenCommandPromptActionTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void CanExecute_RealContextDirectory_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path } };

        Assert.IsTrue(new OpenCommandPromptAction().CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_NonExistentContextDirectory_ReturnsFalse()
    {
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = @"Z:\definitely-not-real-lertaro-dir" } };

        Assert.IsFalse(new OpenCommandPromptAction().CanExecute(results));
    }

    [TestMethod]
    public void IsVisibleInMenu_SingleDirectoryResult_ReturnsTrue() =>
        Assert.IsTrue(new OpenCommandPromptAction().IsVisibleInMenu(new ISearchResult[] { new FakeResult { IsDir = true } }, SearchWindowType.Main));

    [TestMethod]
    public void IsVisibleInMenu_FileResult_ReturnsFalse() =>
        Assert.IsFalse(new OpenCommandPromptAction().IsVisibleInMenu(new ISearchResult[] { new FakeResult { IsDir = false } }, SearchWindowType.Main));

    [TestMethod]
    public void IsVisibleInSearch_InlineWindow_ReturnsTrue() =>
        Assert.IsTrue(new OpenCommandPromptAction().IsVisibleInSearch(Array.Empty<ISearchResult>(), SearchWindowType.Inline));

    [TestMethod]
    public void IsVisibleInSearch_MainWindow_ReturnsFalse() =>
        Assert.IsFalse(new OpenCommandPromptAction().IsVisibleInSearch(Array.Empty<ISearchResult>(), SearchWindowType.Main));

    [TestMethod]
    public void AdminVariant_IsVisibleInMenu_SingleDirectoryResult_ReturnsTrue() =>
        Assert.IsTrue(new OpenAdminCommandPromptAction().IsVisibleInMenu(new ISearchResult[] { new FakeResult { IsDir = true } }, SearchWindowType.Main));
}
