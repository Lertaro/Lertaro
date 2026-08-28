using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class ModifierDoubleTapDetectorTests
{
    private const int VkCtrl = 0x11;

    [TestMethod]
    public void OnModifierKeyDown_SingleTap_DoesNotTrigger()
    {
        var detector = new ModifierDoubleTapDetector();

        var triggered = detector.OnModifierKeyDown(VkCtrl, 1000);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_DoubleTapWithinWindow_Triggers()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();

        var triggered = detector.OnModifierKeyDown(VkCtrl, 1200); // 200ms later, inside (100, 500)

        Assert.IsTrue(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_SecondTapTooSoon_DoesNotTrigger()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();

        // Only 50ms later -- below the 100ms floor (guards against key-repeat/bounce).
        var triggered = detector.OnModifierKeyDown(VkCtrl, 1050);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_SecondTapTooLate_DoesNotTrigger()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();

        // 400ms later -- past the 300ms ceiling.
        var triggered = detector.OnModifierKeyDown(VkCtrl, 1400);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_SecondTapAtUpperBoundary_DoesNotTrigger()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();

        // The upper boundary is exclusive, so exactly 300ms is outside the window.
        var triggered = detector.OnModifierKeyDown(VkCtrl, 1300);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_KeyRepeatWithoutKeyUp_IsIgnored()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);

        // No OnModifierKeyUp() in between -- this is OS key-repeat, must not count as a second tap.
        var triggered = detector.OnModifierKeyDown(VkCtrl, 1200);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_DifferentModifierKey_ResetsSequence()
    {
        const int vkShift = 0x10;
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();

        // A different vk code breaks the double-tap sequence rather than completing it.
        var triggered = detector.OnModifierKeyDown(vkShift, 1200);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void ResetOnOtherKey_ClearsInProgressSequence()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();
        detector.ResetOnOtherKey();

        // Without the reset this would complete the double-tap (200ms, same vk); after resetting, it
        // starts a fresh single-tap count instead.
        var triggered = detector.OnModifierKeyDown(VkCtrl, 1200);

        Assert.IsFalse(triggered);
    }

    [TestMethod]
    public void OnModifierKeyDown_AfterTriggering_StateIsResetForNextSequence()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyUp();
        detector.OnModifierKeyDown(VkCtrl, 1200); // triggers
        detector.OnModifierKeyUp();

        // A fresh double-tap sequence afterward must work again, not be short-circuited by leftover state.
        detector.OnModifierKeyDown(VkCtrl, 5000);
        detector.OnModifierKeyUp();
        var triggeredAgain = detector.OnModifierKeyDown(VkCtrl, 5200);

        Assert.IsTrue(triggeredAgain);
    }
}
