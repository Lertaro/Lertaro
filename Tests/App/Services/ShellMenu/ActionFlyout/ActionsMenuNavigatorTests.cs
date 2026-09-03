using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.ShellMenu.ActionFlyout;

namespace Lertaro.App.Tests.Services.ShellMenu.ActionFlyout;

[TestClass]
public sealed class ActionsMenuNavigatorTests
{
    private sealed class FakeSearchWindow : ISearchWindow
    {
        public UIElement ResultsPanel { get; } = new Grid();
        public ListBox LstResults { get; } = new();
        public Grid GridSearchResults { get; } = new();
        public Grid GridActions { get; } = new();
        public TextBlock TxtActionsTarget { get; } = new();
        public ListBox LstActions { get; } = new();
        public string SearchText => "";
        public TextBox SearchTextBox { get; } = new();
        public bool IsInActionsMode { get; set; }
        public void UpdateActionsLayout() { }
        public void FocusSearch() { }

        public void LocateInExplorerExternal(string path) { }
        public void OpenFileOrFolderExternal(string path) { }
        public void OpenFileOrFolderAsAdminExternal(string path) { }
        public void HideWindow() { }
    }

    private static ActionMenuItem Normal(string text) => new() { Text = text };
    private static ActionMenuItem Separator() => new() { IsSeparator = true };
    private static ActionMenuItem Header(string title) => new() { IsSectionHeader = true, SectionTitle = title };
    private static ActionMenuItem SubMenu(string text, IntPtr handle) => new() { Text = text, HasSubMenu = true, SubMenuHandle = handle };

    [StaTestMethod]
    public void NavigateActionsList_EmptyList_DoesNothing()
    {
        var view = new FakeSearchWindow();
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => { });

        navigator.NavigateActionsList(1);

        Assert.AreEqual(-1, view.LstActions.SelectedIndex);
    }

    [StaTestMethod]
    public void NavigateActionsList_MovesForwardSkippingSeparatorsAndDisabled()
    {
        var view = new FakeSearchWindow();
        view.LstActions.Items.Add(Normal("A"));
        view.LstActions.Items.Add(Separator());
        view.LstActions.Items.Add(new ActionMenuItem { Text = "B", IsDisabled = true });
        view.LstActions.Items.Add(Normal("C"));
        view.LstActions.SelectedIndex = 0;
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => { });

        navigator.NavigateActionsList(1);

        Assert.AreEqual(3, view.LstActions.SelectedIndex);
    }

    [StaTestMethod]
    public void NavigateActionsList_MovesBackwardWrappingAround()
    {
        var view = new FakeSearchWindow();
        view.LstActions.Items.Add(Normal("A"));
        view.LstActions.Items.Add(Normal("B"));
        view.LstActions.SelectedIndex = 0;
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => { });

        navigator.NavigateActionsList(-1);

        Assert.AreEqual(1, view.LstActions.SelectedIndex);
    }

    [StaTestMethod]
    public void NavigateActionsList_OnlySelectableIsCurrent_StaysInPlace()
    {
        var view = new FakeSearchWindow();
        view.LstActions.Items.Add(Normal("A"));
        view.LstActions.Items.Add(Separator());
        view.LstActions.SelectedIndex = 0;
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => { });

        navigator.NavigateActionsList(1);

        Assert.AreEqual(0, view.LstActions.SelectedIndex);
    }

    [StaTestMethod]
    public void EnterSubMenu_SelectedItemHasSubMenu_PushesStackAndLoadsIt()
    {
        var view = new FakeSearchWindow();
        var handle = new IntPtr(42);
        view.LstActions.Items.Add(SubMenu("Send to", handle));
        view.LstActions.SelectedIndex = 0;
        var loaded = new List<IntPtr>();
        var navigator = new ActionsMenuNavigator(view, h => loaded.Add(h), () => { });

        navigator.EnterSubMenu();

        CollectionAssert.AreEqual(new[] { handle }, loaded);
        Assert.AreEqual("Send to", navigator.CurrentSubMenuTitle);
    }

    [StaTestMethod]
    public void EnterSubMenu_SelectedItemHasNoSubMenu_DoesNothing()
    {
        var view = new FakeSearchWindow();
        view.LstActions.Items.Add(Normal("Copy"));
        view.LstActions.SelectedIndex = 0;
        var loadCount = 0;
        var navigator = new ActionsMenuNavigator(view, _ => loadCount++, () => { });

        navigator.EnterSubMenu();

        Assert.AreEqual(0, loadCount);
        Assert.IsNull(navigator.CurrentSubMenuTitle);
    }

    [StaTestMethod]
    public void GoBackMenuOrExit_EmptyStack_CallsExitActionsMode()
    {
        var view = new FakeSearchWindow();
        var exited = false;
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => exited = true);

        navigator.GoBackMenuOrExit();

        Assert.IsTrue(exited);
    }

    [StaTestMethod]
    public void GoBackMenuOrExit_NonEmptyStack_LoadsParentAndRestoresSelection()
    {
        var view = new FakeSearchWindow();
        var handle = new IntPtr(42);
        view.LstActions.Items.Add(SubMenu("Send to", handle));
        view.LstActions.SelectedIndex = 0;
        var loaded = new List<IntPtr>();
        var navigator = new ActionsMenuNavigator(view, h => loaded.Add(h), () => { });
        navigator.EnterSubMenu();

        // Simulate the submenu being loaded with its own items before going back.
        view.LstActions.Items.Clear();
        view.LstActions.Items.Add(Normal("Nested"));

        navigator.GoBackMenuOrExit();

        CollectionAssert.AreEqual(new[] { handle, IntPtr.Zero }, loaded);
        Assert.IsNull(navigator.CurrentSubMenuTitle);
    }

    [StaTestMethod]
    public void Reset_ClearsNavigationStack()
    {
        var view = new FakeSearchWindow();
        var handle = new IntPtr(42);
        view.LstActions.Items.Add(SubMenu("Send to", handle));
        view.LstActions.SelectedIndex = 0;
        var exited = false;
        var navigator = new ActionsMenuNavigator(view, _ => { }, () => exited = true);
        navigator.EnterSubMenu();

        navigator.Reset();
        navigator.GoBackMenuOrExit();

        Assert.IsTrue(exited);
        Assert.IsNull(navigator.CurrentSubMenuTitle);
    }
}
