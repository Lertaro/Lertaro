using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public sealed class MkdirActionTests
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

    private static readonly MkdirAction Action = new();

    [TestMethod]
    public void CanExecute_MultipleResults_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[]
        {
            new FakeResult { ContextDirectory = dir.Path },
            new FakeResult { ContextDirectory = dir.Path },
        };

        Assert.IsFalse(Action.CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_NonExistentContextDirectory_ReturnsFalse()
    {
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = @"Z:\definitely-not-real-lertaro-dir" } };

        Assert.IsFalse(Action.CanExecute(results));
    }

    [TestMethod]
    public void CanExecute_RealContextDirectory_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path } };

        Assert.IsTrue(Action.CanExecute(results));
    }

    [TestMethod]
    public void Execute_CreatesDirectoryNamedByFullPath()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "NewFolder" } };

        Action.Execute(results, null!);

        Assert.IsTrue(Directory.Exists(Path.Combine(dir.Path, "NewFolder")));
    }

    [TestMethod]
    public void Execute_EmptyFullPath_DoesNothing()
    {
        using var dir = new TempDirectory();
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = dir.Path, FullPath = "" } };

        Action.Execute(results, null!);

        Assert.HasCount(0, Directory.GetDirectories(dir.Path));
    }
}
