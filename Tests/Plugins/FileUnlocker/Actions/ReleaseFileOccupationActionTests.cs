using System;
using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.FileUnlocker.Actions;

namespace Lertaro.Plugins.FileUnlocker.Tests.Actions;

[TestClass]
public sealed class ReleaseFileOccupationActionTests
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
    public void CanExecute_RequiresExactlyOneResult()
    {
        var action = new ReleaseFileOccupationAction();
        var result = new FakeResult { FullPath = Path.Combine(Path.GetTempPath(), "missing-lertaro-file.txt") };

        Assert.IsFalse(action.CanExecute(Array.Empty<ISearchResult>()));
        Assert.IsFalse(action.CanExecute([result, result]));
    }

    [TestMethod]
    public void CanExecute_RequiresAnExistingFile()
    {
        var action = new ReleaseFileOccupationAction();
        var directory = Directory.CreateTempSubdirectory("lertaro-unlock-tests-");
        try
        {
            Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = directory.FullName, IsDir = true }]));
            Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = Path.Combine(directory.FullName, "missing.txt") }]));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void CanExecute_ReturnsTrueForAnExistingFile()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-unlock-tests-");
        var path = Path.Combine(directory.FullName, "file.txt");
        try
        {
            File.WriteAllText(path, "test");
            Assert.IsTrue(new ReleaseFileOccupationAction().CanExecute([new FakeResult { FullPath = path }]));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
