using System.Windows.Media;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.Plugin;

[TestClass]
[DoNotParallelize]
public sealed class PluginActionExecutorTests
{
    [TestMethod]
    public void TryExecute_PluginSearchAction_HidesWindowAndExecutesAction()
    {
        var fakeAction = new FakeSearchAction();
        var fakePlugin = new FakeActionProviderPlugin(fakeAction);

        PluginManager.Instance.RegisterPlugin(fakePlugin);

        var reg = PluginManager.Instance.AllActions.FirstOrDefault(a => a.Action == fakeAction);
        Assert.IsNotNull(reg);

        var result = new AppSearchResult
        {
            Name = "touch test.txt",
            FullPath = "test.txt",
            ContextDirectory = "C:\\TestDir",
            ResultKind = "PluginAction",
            PluginActionId = reg.RuntimeActionId,
            PluginActionArgumentText = "test.txt"
        };

        var fakeView = new FakePluginSearchWindow();
        var success = PluginActionExecutor.TryExecute(result, fakeView);

        Assert.IsTrue(success);
        Assert.IsTrue(fakeView.HideWindowCalled);
        Assert.IsTrue(fakeAction.Executed);
        Assert.AreEqual("test.txt", fakeAction.LastArgument);
    }

    [TestMethod]
    public void TryExecute_UnknownActionId_ReturnsFalseAndDoesNotHide()
    {
        var result = new AppSearchResult
        {
            Name = "unknown",
            ResultKind = "PluginAction",
            PluginActionId = uint.MaxValue
        };

        var fakeView = new FakePluginSearchWindow();
        var success = PluginActionExecutor.TryExecute(result, fakeView);

        Assert.IsFalse(success);
        Assert.IsFalse(fakeView.HideWindowCalled);
    }

    [TestMethod]
    public void TryExecute_SearchSectionHeader_ReturnsFalse()
    {
        var result = new AppSearchResult
        {
            Name = "Header",
            ResultKind = "SectionHeader"
        };

        var fakeView = new FakePluginSearchWindow();
        var success = PluginActionExecutor.TryExecute(result, fakeView);

        Assert.IsFalse(success);
        Assert.IsFalse(fakeView.HideWindowCalled);
    }

    private sealed class FakeSearchAction : ISearchResultAction
    {
        public bool Executed { get; private set; }
        public string? LastArgument { get; private set; }

        public string GroupName => "Test";
        public string DisplayName => "Fake";
        public string Description => "Fake";
        public IReadOnlyList<string> Keywords => ["fake"];
        public ImageSource? Icon => null;

        public bool CanExecute(IReadOnlyList<ISearchResult> results) => true;

        public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
        {
            Executed = true;
            LastArgument = results.Count > 0 ? results[0].FullPath : null;
        }
    }

    private sealed class FakeActionProviderPlugin : IPlugin, IActionProvider
    {
        private readonly ISearchResultAction _action;

        public FakeActionProviderPlugin(ISearchResultAction action) => _action = action;

        public string Name => "TestPlugin";

        public IEnumerable<ISearchResultAction> GetActions() => [_action];
        public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => [];
    }

    private sealed class FakePluginSearchWindow : IPluginSearchWindow
    {
        public bool HideWindowCalled { get; private set; }

        public void HideWindow() => HideWindowCalled = true;
        public void ShowWindow() { }
        public void SetSearchText(string text) { }
        public void SelectSearchText() { }
        public void LocateInExplorerExternal(string path) { }
        public void OpenFileOrFolderExternal(string path) { }
        public void OpenFileOrFolderAsAdminExternal(string path) { }
    }
}
