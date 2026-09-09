using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public sealed class RenameActionTests
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
    public void GetInitialEditorState_File_SelectsStem()
    {
        var state = RenameAction.GetInitialEditorState(new FakeResult
        {
            Name = "abc.ext",
            FullPath = @"C:\data\abc.ext"
        });

        Assert.AreEqual("abc.ext", state.Name);
        Assert.AreEqual(0, state.SelectionStart);
        Assert.AreEqual(3, state.SelectionLength);
    }

    [TestMethod]
    public void GetInitialEditorState_FileWithoutExtension_SelectsWholeName()
    {
        var state = RenameAction.GetInitialEditorState(new FakeResult
        {
            Name = "abc",
            FullPath = @"C:\data\abc"
        });

        Assert.AreEqual("abc", state.Name);
        Assert.AreEqual(3, state.SelectionLength);
    }

    [TestMethod]
    public void GetInitialEditorState_Directory_SelectsWholeDirectoryName()
    {
        var state = RenameAction.GetInitialEditorState(new FakeResult
        {
            Name = "folder",
            FullPath = @"C:\data\folder\",
            IsDir = true
        });

        Assert.AreEqual("folder", state.Name);
        Assert.AreEqual(6, state.SelectionLength);
    }

    [TestMethod]
    public void CanExecute_RequiresOneExistingItem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lertaro-rename-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        try
        {
            var action = new RenameAction();
            Assert.IsTrue(action.CanExecute(new ISearchResult[]
            {
                new FakeResult { Name = "item.txt", FullPath = path }
            }));
            Assert.IsFalse(action.CanExecute(Array.Empty<ISearchResult>()));
            Assert.IsFalse(action.CanExecute(new ISearchResult[]
            {
                new FakeResult { Name = "missing.txt", FullPath = path + ".missing" }
            }));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
