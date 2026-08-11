using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public sealed class CopyNameActionTests
{
    [TestMethod]
    public void CanExecute_RequiresEverySelectedItemToHaveAName()
    {
        var action = new CopyNameAction();

        Assert.IsTrue(action.CanExecute([new FakeResult("first.txt"), new FakeResult("second.txt")]));
        Assert.IsFalse(action.CanExecute([new FakeResult("first.txt"), new FakeResult("")]));
        Assert.IsFalse(action.CanExecute([]));
    }

    [TestMethod]
    public void BuildText_JoinsSelectedNamesByLine()
    {
        var text = CopyNameAction.BuildText([new FakeResult("first.txt"), new FakeResult("second.txt")]);

        Assert.AreEqual($"first.txt{Environment.NewLine}second.txt", text);
    }

    private sealed class FakeResult(string name) : ISearchResult
    {
        public string Name { get; } = name;
        public string FullPath => string.Empty;
        public string ContextDirectory => string.Empty;
        public bool IsDir => false;
        public bool IsApplication => false;
    }
}
