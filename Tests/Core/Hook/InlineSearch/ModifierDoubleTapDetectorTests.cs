using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class ModifierDoubleTapDetectorTests
{
    private const int VkCtrl = 0xA2;

    [TestMethod]
    [DataRow(50u, false)]
    [DataRow(100u, false)]
    [DataRow(101u, true)]
    [DataRow(200u, true)]
    [DataRow(299u, true)]
    [DataRow(300u, false)]
    [DataRow(400u, false)]
    public void OnModifierKeyUp_DownToDownInterval_UsesExistingWindow(uint interval, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        detector.OnModifierKeyDown(VkCtrl, 1000 + interval);

        Assert.AreEqual(expected, detector.OnModifierKeyUp(VkCtrl, 1010 + interval));
        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1020 + interval), "An orphan release must not trigger again.");
    }

    [TestMethod]
    public void OnModifierKeyDown_AutoRepeat_DoesNotCountAsAnotherTap()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyDown(VkCtrl, 1150);

        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1160));
        Assert.IsTrue(Tap(detector, 1200));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetOnOtherKey_DuringEitherPress_DoesNotPrimeTheNextTap(bool secondPress)
    {
        var detector = new ModifierDoubleTapDetector();
        if (secondPress) Assert.IsFalse(Tap(detector, 850));
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.ResetOnOtherKey();
        detector.OnModifierKeyDown(VkCtrl, 1050); // Repeat must not undo cancellation.

        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1060));
        Assert.IsFalse(Tap(detector, 1150));
        Assert.IsTrue(Tap(detector, 1300));
    }

    [TestMethod]
    public void ResetOnOtherKey_BetweenTaps_StartsFreshPair()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        detector.ResetOnOtherKey();

        Assert.IsFalse(Tap(detector, 1150));
        Assert.IsTrue(Tap(detector, 1300));
    }

    [TestMethod]
    [DataRow(299u, true)]
    [DataRow(300u, false)]
    [DataRow(1000u, false)]
    public void OnModifierKeyUp_SecondPressDuration_RejectsHolds(uint duration, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        detector.OnModifierKeyDown(VkCtrl, 1150);

        Assert.AreEqual(expected, detector.OnModifierKeyUp(VkCtrl, 1150 + duration));
    }

    [TestMethod]
    public void OnModifierKeyUp_FirstPressHeld_DoesNotPrimeNextPair()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1500));

        Assert.IsFalse(Tap(detector, 1600));
        Assert.IsTrue(Tap(detector, 1750));
    }

    [TestMethod]
    public void OnModifierKeyUp_DifferentSides_NeedTwoTapsOnSameSide()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        detector.OnModifierKeyDown(0xA3, 1150);
        Assert.IsFalse(detector.OnModifierKeyUp(0xA3, 1160));
        detector.OnModifierKeyDown(0xA3, 1300);
        Assert.IsTrue(detector.OnModifierKeyUp(0xA3, 1310));
    }

    [TestMethod]
    public void OnModifierKeyUp_OverlappingSides_CancelsBoth()
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.OnModifierKeyDown(0xA3, 1050);
        Assert.IsFalse(detector.OnModifierKeyUp(0xA3, 1060));
        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1070));

        Assert.IsFalse(Tap(detector, 1150));
        Assert.IsTrue(Tap(detector, 1300));
    }

    [TestMethod]
    public void OnModifierKeyUp_TimestampWrap_StillRecognizesPair()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, uint.MaxValue - 100));
        Assert.IsTrue(Tap(detector, 50));
    }

    [TestMethod]
    public void OnModifierKeyUp_ConsecutivePairs_DoNotShareTaps()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsTrue(Tap(detector, 1150));
        Assert.IsFalse(Tap(detector, 1300));
        Assert.IsTrue(Tap(detector, 1450));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Synchronize_MissingRelease_RecoversWithoutInventingATap(bool stillDown)
    {
        var detector = new ModifierDoubleTapDetector();
        detector.OnModifierKeyDown(VkCtrl, 1000);
        detector.Synchronize(_ => stillDown);

        detector.OnModifierKeyDown(VkCtrl, 1150);
        Assert.IsFalse(detector.OnModifierKeyUp(VkCtrl, 1160));
        Assert.IsTrue(Tap(detector, stillDown ? 1200u : 1300u));
    }

    private static bool Tap(ModifierDoubleTapDetector detector, uint time)
    {
        detector.OnModifierKeyDown(VkCtrl, time);
        return detector.OnModifierKeyUp(VkCtrl, unchecked(time + 10));
    }
}
