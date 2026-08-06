namespace Lertaro.Plugins.WindowSwitcher.Tests;

[TestClass]
public sealed class WindowEnumeratorTests
{
    [TestMethod]
    public void IsAltTabEligible_TypicalVisibleAppWindow_ReturnsTrue()
        => Assert.IsTrue(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: false, isCloaked: false, titleLength: 5, isToolWindow: false, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_NotVisible_ReturnsFalse()
        => Assert.IsFalse(WindowEnumerator.IsAltTabEligible(isVisible: false, hasOwner: false, isCloaked: false, titleLength: 5, isToolWindow: false, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_HasOwner_ReturnsFalse()
        // Owned windows are typically dialogs/tool popups belonging to a parent -- not real switch targets.
        => Assert.IsFalse(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: true, isCloaked: false, titleLength: 5, isToolWindow: false, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_Cloaked_ReturnsFalse()
        // A UWP window minimized to another virtual desktop reports IsWindowVisible=true but is cloaked --
        // nothing to actually switch to.
        => Assert.IsFalse(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: false, isCloaked: true, titleLength: 5, isToolWindow: false, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_EmptyTitle_ReturnsFalse()
        => Assert.IsFalse(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: false, isCloaked: false, titleLength: 0, isToolWindow: false, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_ToolWindowWithoutAppWindowFlag_ReturnsFalse()
        => Assert.IsFalse(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: false, isCloaked: false, titleLength: 5, isToolWindow: true, isAppWindow: false));

    [TestMethod]
    public void IsAltTabEligible_ToolWindowThatAlsoOptsInAsAppWindow_ReturnsTrue()
        // Some apps set both WS_EX_TOOLWINDOW and WS_EX_APPWINDOW to hide their own titlebar icon while
        // still opting back into the taskbar/Alt+Tab list.
        => Assert.IsTrue(WindowEnumerator.IsAltTabEligible(isVisible: true, hasOwner: false, isCloaked: false, titleLength: 5, isToolWindow: true, isAppWindow: true));
}
