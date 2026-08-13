using Lertaro.Plugins.CoreExtensions.Actions;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public class LocalSendActionTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string ContextDirectory { get; init; } = string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestMethod]
    public void CanExecute_EmptyList_ReturnsFalse()
    {
        var action = new LocalSendAction();
        var canExec = action.CanExecute(Array.Empty<ISearchResult>());
        Assert.IsFalse(canExec);
    }

    [TestMethod]
    public void DisplayName_IsNotEmpty()
    {
        var action = new LocalSendAction();
        Assert.IsFalse(string.IsNullOrWhiteSpace(action.DisplayName));
    }

    [TestMethod]
    public void CanExecute_DirectoryDeclaredAsFile_ReturnsFalse()
    {
        var result = new FakeResult { FullPath = Path.GetTempPath(), IsDir = false };

        Assert.IsFalse(new LocalSendAction().CanExecute(new[] { result }));
    }
}
