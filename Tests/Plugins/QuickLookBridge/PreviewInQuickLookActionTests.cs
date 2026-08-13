using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.QuickLookBridge.Actions;

namespace Lertaro.Plugins.QuickLookBridge.Tests.Actions;

// CanExecute additionally depends on QuickLookPipeClient.IsAvailable(), which is environment-dependent
// (can't control whether the test machine has QuickLook running) -- so only the guards that must
// short-circuit to false before ever touching the pipe are asserted here. Execute() fires a real pipe
// invoke as a side effect and isn't exercised directly, same as CoreExtensions' OpenCommandPromptAction.
[TestClass]
public sealed class PreviewInQuickLookActionTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestMethod]
    public void CanExecute_MultipleResults_ReturnsFalse()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { FullPath = Path.GetTempPath() },
            new FakeResult { FullPath = Path.GetTempPath() }
        };

        Assert.IsFalse(new PreviewInQuickLookAction().CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_NoResults_ReturnsFalse() =>
        Assert.IsFalse(new PreviewInQuickLookAction().CanExecute(Array.Empty<ISearchResult>()));

    [TestMethod]
    public void CanExecute_SingleResultWithNonExistentPath_ReturnsFalse()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"Z:\definitely-not-a-real-lertaro-path" } };

        Assert.IsFalse(new PreviewInQuickLookAction().CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_DirectoryDeclaredAsFile_ReturnsFalseBeforeCheckingQuickLook()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = Path.GetTempPath(), IsDir = false } };

        Assert.IsFalse(new PreviewInQuickLookAction().CanExecute(results));
    }

    [TestMethod]
    public void DisplayName_IsNotEmpty() =>
        Assert.IsFalse(string.IsNullOrWhiteSpace(new PreviewInQuickLookAction().DisplayName));

    [TestMethod]
    public void Icon_IsNotNull() =>
        Assert.IsNotNull(new PreviewInQuickLookAction().Icon);
}
