using System.Windows;
using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowControllerTests
{
    // The pre-existing cases, all with the window focused, which is the only way it is normally visible:
    // losing focus otherwise hides it.
    private static QuickSearchWindowController.ToggleAction Toggle(
        bool isVisible, WindowState windowState, bool reopenAsFullWindowSetting, bool isActive = true, bool stayOpen = false)
        => QuickSearchWindowController.DetermineToggleAction(isVisible, isActive, windowState, reopenAsFullWindowSetting, stayOpen);

    [TestMethod]
    public void DetermineToggleAction_WindowNotVisible_ReturnsShow() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.Show,
            Toggle(isVisible: false, WindowState.Normal, reopenAsFullWindowSetting: true));

    [TestMethod]
    public void DetermineToggleAction_WindowMinimized_ReturnsShow() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.Show,
            Toggle(isVisible: true, WindowState.Minimized, reopenAsFullWindowSetting: true));

    [TestMethod]
    public void DetermineToggleAction_VisibleAndSettingDisabled_ReturnsHide() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.Hide,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: false));

    [TestMethod]
    public void DetermineToggleAction_VisibleAndSettingEnabled_ReturnsReopenAsFullWindow() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.ReopenAsFullWindow,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: true));

    [TestMethod]
    public void DetermineToggleAction_MaximizedAndSettingEnabled_ReturnsReopenAsFullWindow() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.ReopenAsFullWindow,
            Toggle(isVisible: true, WindowState.Maximized, reopenAsFullWindowSetting: true));

    // Stay Open is the one state where the window can sit visible without focus, and there the summon
    // hotkey has to mean "bring me back to it". Hiding it, or expanding it into the full window, is the
    // opposite of what was asked: the window is on screen, the user is looking at it, and the query they
    // have been assembling out of other windows is in it.
    [TestMethod]
    public void DetermineToggleAction_StayOpenAndUnfocused_ReturnsFocus() => Assert.AreEqual(QuickSearchWindowController.ToggleAction.Focus,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: false, isActive: false, stayOpen: true));

    [TestMethod]
    public void DetermineToggleAction_StayOpenAndUnfocused_BeatsTheReopenAsFullWindowSetting() =>
        // Expanding into the full window would carry the query over but abandon the window the user is
        // still feeding, so refocusing wins over that setting too.
        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Focus,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: true, isActive: false, stayOpen: true));

    [TestMethod]
    public void DetermineToggleAction_StayOpenButStillFocused_StillHides() =>
        // The hotkey is also how the window is dismissed. Once it has focus back, pressing it again must
        // put it away as usual, or Stay Open would be a state with no way out.
        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Hide,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: false, isActive: true, stayOpen: true));

    [TestMethod]
    public void DetermineToggleAction_UnfocusedWithoutStayOpen_StillHides() =>
        // Narrowed to Stay Open on purpose. The window can be briefly visible-but-unfocused for other
        // reasons (the deactivate debounce, an out-of-process preview holding foreground), and a rule
        // that stopped the hotkey dismissing it in those states would be worse than the gap it fixes.
        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Hide,
            Toggle(isVisible: true, WindowState.Normal, reopenAsFullWindowSetting: false, isActive: false, stayOpen: false));

    [TestMethod]
    public void DetermineToggleAction_StayOpenAndMinimized_StillReturnsShow() =>
        // Minimized is not "visible but unfocused" -- there is nothing on screen to come back to, so this
        // is a full summon.
        Assert.AreEqual(QuickSearchWindowController.ToggleAction.Show,
            Toggle(isVisible: true, WindowState.Minimized, reopenAsFullWindowSetting: false, isActive: false, stayOpen: true));

    // Any distinct non-zero values will do; only their equality matters here.
    private const int OwnWindow = 0x1111, PreviousWindow = 0x2222, ForeignWindow = 0x3333, OtherOwnWindow = 0x4444;

    private static bool ShouldRestore(bool restoreFocus = true, bool suppressed = false,
        int lastActiveHwnd = PreviousWindow, int foregroundAtHide = OwnWindow)
        => QuickSearchWindowController.ShouldRestoreForeground(restoreFocus, suppressed,
            new IntPtr(lastActiveHwnd), new IntPtr(foregroundAtHide), new IntPtr(OwnWindow));

    [TestMethod]
    public void ShouldRestoreForeground_StillOurForeground_Restores() =>
        // The ordinary dismiss: nothing came forward while the window faded, so the pre-summon window is
        // the one that deserves the foreground back.
        Assert.IsTrue(ShouldRestore());

    [TestMethod]
    public void ShouldRestoreForeground_ForeignWindowAlreadyForeground_DoesNotRestore() =>
        // The Ctrl+Enter locate: Explorer reused a window already showing the folder and took the
        // foreground a beat before this deferred hide landed. Restoring now is the bug being fixed -- it
        // yanks the foreground straight back off the window the user just asked to be shown.
        Assert.IsFalse(ShouldRestore(foregroundAtHide: ForeignWindow));

    [TestMethod]
    public void ShouldRestoreForeground_OwnOtherWindowForeground_DoesNotRestore() =>
        // The other half of that rule: Settings or the full window holding the foreground is also not
        // something to steal back, and it is not this window's handle either.
        Assert.IsFalse(ShouldRestore(foregroundAtHide: OtherOwnWindow));

    [TestMethod]
    public void ShouldRestoreForeground_NoPreviousWindow_DoesNotRestore() =>
        // Summoned over another of our own windows, so there was never a prior foreground to return to.
        Assert.IsFalse(ShouldRestore(lastActiveHwnd: 0));

    [TestMethod]
    public void ShouldRestoreForeground_Suppressed_DoesNotRestore() =>
        // A caller that says it is taking the foreground itself still gets its way even when that has not
        // landed yet -- a cold browser or a UAC-blocked launch is still behind this hide.
        Assert.IsFalse(ShouldRestore(suppressed: true));

    [TestMethod]
    public void ShouldRestoreForeground_RestoreFocusNotRequested_DoesNotRestore() =>
        Assert.IsFalse(ShouldRestore(restoreFocus: false));
}
