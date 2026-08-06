using System.Windows.Input;
using Lertaro.Core;
// This test file's namespace ends in QuickSearchWindow, which is also the window's type name, so an
// unaliased reference binds to the namespace instead of the class and fails to compile. The class itself
// lives in Lertaro.App, not in a namespace matching its folder.
using QuickWindow = Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// The gate behind the "Lock position" setting. Only the decision is covered: the drag itself needs a live
// window, which is why the decision was pulled out of the handler in the first place.
[TestClass]
public sealed class LockPositionGateTests
{
    [TestMethod]
    public void UnlockedIsTheOldBehaviour() =>
        // Off by default, so this is what every existing install keeps doing.
        Assert.IsTrue(QuickWindow.ShouldStartDrag(MouseButton.Left, lockPosition: false));

    [TestMethod]
    public void LockedRefusesTheDrag() => Assert.IsFalse(QuickWindow.ShouldStartDrag(MouseButton.Left, lockPosition: true));

    [TestMethod]
    public void OnlyTheLeftButtonEverStartsADrag()
    {
        // Unchanged by the lock: the right button belongs to the status icon's own reset-position menu,
        // and the middle one toggles Stay Open.
        foreach (var button in new[] { MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 })
        {
            Assert.IsFalse(QuickWindow.ShouldStartDrag(button, lockPosition: false), $"{button} started a drag");
            Assert.IsFalse(QuickWindow.ShouldStartDrag(button, lockPosition: true), $"{button} started a drag while locked");
        }
    }

    [TestMethod]
    public void TheLogoIsTheOtherWayToDragAndObeysTheSameSetting()
    {
        // The gap this test exists for: the window can be dragged by its border AND by its logo, and the
        // logo's handler lives in SearchBoxControl, not in the window -- so gating the border alone left
        // the logo still moving it. SearchBoxControl exposes IsIconDraggable for exactly this, and the
        // window drives it from the setting; what is pinned here is the mapping between the two, since
        // the drag itself needs a live window.
        Assert.IsTrue(QuickWindow.ShouldAllowIconDrag(lockPosition: false));
        Assert.IsFalse(QuickWindow.ShouldAllowIconDrag(lockPosition: true));
    }

    [TestMethod]
    public void BothDragPathsAgree()
    {
        // They are read at different moments by different code, so a change to one that misses the other
        // is the exact failure this pair guards.
        foreach (var locked in new[] { true, false })
        {
            Assert.AreEqual(
                QuickWindow.ShouldStartDrag(MouseButton.Left, locked),
                QuickWindow.ShouldAllowIconDrag(locked),
                $"border and logo disagree when lockPosition={locked}");
        }
    }

    [TestMethod]
    public void TheSettingDefaultsToUnlocked() =>
        // Being able to move the window is what everyone already has; turning that off for them on
        // upgrade would be a change nobody asked for.
        Assert.IsFalse(new SearchWindowSettings().LockPosition);
}
