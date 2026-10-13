using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellMenu.ActionFlyout;

using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.ActionFlyout;

[TestClass]
[DoNotParallelize]
public sealed class ActionsMenuExecutorTests
{
    // The quick window starts a deferred hide when an action is chosen. A registered action that
    // opens a modal prompt must run while its owner is still alive; otherwise the prompt can flash
    // away with the owner before the user can answer it.
    [StaTestMethod]
    public void Execute_RegisteredAction_KeepsTheWindowVisibleUntilTheActionReturns()
    {
        var calls = new List<string>();
        var view = new FakeSearchWindow(calls);
        var action = new RecordingSearchAction(() =>
        {
            Assert.IsFalse(view.HideWindowCalled, "the window was hidden before the action returned");
            calls.Add("execute");
        });
        PluginManager.Instance.RegisterPlugin(new SingleActionPlugin(action));
        var registration = PluginManager.Instance.AllActions.FirstOrDefault(x => x.Action == action);
        Assert.IsNotNull(registration);

        view.LstActions.Items.Add(new ActionMenuItem { CommandId = registration.RuntimeActionId });
        view.LstActions.SelectedIndex = 0;

        var executor = new ActionsMenuExecutor(
            view,
            new Dictionary<uint, IDynamicActionProvider>(),
            new ActionsMenuNavigator(view, _ => { }, () => { }),
            () => { });
        var result = new AppSearchResult { Name = "result", FullPath = "result" };

        executor.Execute(result, [result]);

        CollectionAssert.AreEqual(new[] { "execute", "hide" }, calls);
    }

    [StaTestMethod]
    public void Execute_RegisteredActionThrows_StillHidesTheWindow()
    {
        var calls = new List<string>();
        var view = new FakeSearchWindow(calls);
        var action = new RecordingSearchAction(() =>
        {
            calls.Add("execute");
            throw new InvalidOperationException("action failed");
        });
        PluginManager.Instance.RegisterPlugin(new SingleActionPlugin(action));
        var registration = PluginManager.Instance.AllActions.First(x => x.Action == action);

        view.LstActions.Items.Add(new ActionMenuItem { CommandId = registration.RuntimeActionId });
        view.LstActions.SelectedIndex = 0;
        var executor = new ActionsMenuExecutor(
            view,
            new Dictionary<uint, IDynamicActionProvider>(),
            new ActionsMenuNavigator(view, _ => { }, () => { }),
            () => { });
        var result = new AppSearchResult { Name = "result", FullPath = "result" };

        Assert.ThrowsExactly<InvalidOperationException>(() => executor.Execute(result, [result]));

        CollectionAssert.AreEqual(new[] { "execute", "hide" }, calls);
    }
    private sealed class RecordingSearchAction(Action onExecute) : ISearchResultAction
    {
        public string GroupName => "Test";
        public string DisplayName => "Test action";
        public string Description => "Test action";
        public ImageSource? Icon => null;
        public bool CanExecute(IReadOnlyList<ISearchResult> results) => true;

        public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => onExecute();
    }

    private sealed class SingleActionPlugin(ISearchResultAction action) : IPlugin, IActionProvider
    {
        public string Name => "ActionsMenuExecutorTests";
        public IEnumerable<ISearchResultAction> GetActions() => [action];
        public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => [];
    }

    private sealed class FakeSearchWindow(List<string> calls) : ISearchWindow
    {
        public bool HideWindowCalled { get; private set; }

        public UIElement ResultsPanel { get; } = new Grid();
        public ListBox LstResults { get; } = new();
        public Grid GridSearchResults { get; } = new();
        public Grid GridActions { get; } = new();
        public TextBlock TxtActionsTarget { get; } = new();
        public ListBox LstActions { get; } = new();
        public TextBox ActionsSearchTextBox { get; } = new();
        public TextBox SearchTextBox { get; } = new();
        public string SearchText => string.Empty;
        public bool UsesFloatingActionsMenu => false;
        public bool KeepWindowOpenAfterActionsHotkey => false;
        public bool IsInActionsMode { get; set; }

        public void UpdateActionsLayout() { }
        public void FocusSearch() { }
        public void LocateInExplorerExternal(string path) { }
        public void OpenFileOrFolderExternal(string path) { }
        public void OpenFileOrFolderAsAdminExternal(string path) { }
        public void HideWindow()
        {
            HideWindowCalled = true;
            calls.Add("hide");
        }

        public void ShowWindow() { }
        public void SetSearchText(string text) { }
        public void SelectSearchText() { }
    }
}
