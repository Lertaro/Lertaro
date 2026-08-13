using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.Views.Controls.Results;

namespace Lertaro.App.Tests.Views.Controls.Results;

[TestClass]
public sealed class ResultsDragDropHelperTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = "item";
        public string ContextDirectory { get; init; } = string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestMethod]
    public void PathExists_File_OnlyUsesFileProbe()
    {
        var directoryProbed = false;

        var exists = ResultsDragDropHelper.PathExists(
            new FakeResult(),
            _ => true,
            _ => directoryProbed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(directoryProbed);
    }

    [TestMethod]
    public void PathExists_Directory_OnlyUsesDirectoryProbe()
    {
        var fileProbed = false;

        var exists = ResultsDragDropHelper.PathExists(
            new FakeResult { IsDir = true },
            _ => fileProbed = true,
            _ => true);

        Assert.IsTrue(exists);
        Assert.IsFalse(fileProbed);
    }
}
