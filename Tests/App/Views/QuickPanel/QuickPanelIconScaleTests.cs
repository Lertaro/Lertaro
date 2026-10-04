using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickPanel;

// Ctrl+wheel over the panel, which is the only place the tile scale is set from. The arithmetic is here
// rather than in the handler so the two things that are easy to get wrong -- hitting an end of the range,
// and not drifting over repeated notches -- can be checked without a mouse.
//
// The scale multiplies whichever QuickPanelThumbnailSize tier the settings page chose; it never replaces
// it. One is a fine adjustment under the pointer, the other a decision worth opening the settings page
// for, and the tests below pin down that the wheel leaves the tier alone.
//
// QuickPanelWindow is spelled out in full rather than imported: this namespace ends in QuickPanel too, so
// a using for the panel's own namespace would leave every reference here ambiguous with it.
[TestClass]
public sealed class QuickPanelIconScaleTests
{
    private static double Scale(double current, int wheelDelta)
        => Lertaro.App.Views.QuickPanel.QuickPanelWindow.ScaledIconScale(current, wheelDelta);

    [TestMethod]
    public void ANotchUpAndDown_AreOneStepEach()
    {
        Assert.AreEqual(0.9, Scale(0.8, -120), 0.0001);
        Assert.AreEqual(0.9, Scale(0.8, 120), 0.0001, "0.8 is the top of the range bar one step");
    }

    // The trip from the floor to the ceiling has to land exactly on the ceiling: adding a tenth eight
    // times in binary floating point stops just short of 1.0, and the last notch then did nothing.
    [TestMethod]
    public void RollingAllTheWayUp_ReachesTheCeilingExactly()
    {
        var scale = QuickPanelSettings.MinIconScale;
        for (var notch = 0; notch < 20; notch++)
            scale = Scale(scale, 120);

        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, scale, 0.0001);
    }

    [TestMethod]
    public void RollingAllTheWayDown_ReachesTheFloorExactly()
    {
        var scale = QuickPanelSettings.DefaultIconScale;
        for (var notch = 0; notch < 20; notch++)
            scale = Scale(scale, -120);

        Assert.AreEqual(QuickPanelSettings.MinIconScale, scale, 0.0001);
    }

    // The bug this replaced: one notch below the floor rounds the step count to zero, and zero passed
    // through the settings clamp reads as "nothing was ever stored" -- which answers the DEFAULT. So
    // rolling down to the smallest size made it jump back to the top and the floor was unreachable.
    [TestMethod]
    public void TheFloorIsReachable_AndOneNotchPastItStaysThere()
    {
        var atFloor = Scale(QuickPanelSettings.MinIconScale, -120);

        Assert.AreEqual(QuickPanelSettings.MinIconScale, atFloor, 0.0001,
            "a notch past the floor must stay on the floor, not fall through to the default");

        var again = atFloor;
        for (var notch = 0; notch < 5; notch++)
            again = Scale(again, -120);

        Assert.AreEqual(QuickPanelSettings.MinIconScale, again, 0.0001);
    }

    [TestMethod]
    public void TheCeilingIsReachable_AndOneNotchPastItStaysThere()
    {
        var atCeiling = Scale(QuickPanelSettings.DefaultIconScale, 120);

        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, atCeiling, 0.0001);

        var again = atCeiling;
        for (var notch = 0; notch < 5; notch++)
            again = Scale(again, 120);

        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, again, 0.0001);
    }

    // Every notch has to move by exactly one step, or the panel would creep away from the round values a
    // settings file is meant to hold.
    [TestMethod]
    public void EveryNotchIsExactlyOneStep()
    {
        var scale = QuickPanelSettings.MinIconScale;
        for (var tenths = 3; tenths <= 10; tenths++)
        {
            scale = Scale(scale, 120);
            Assert.AreEqual(tenths / 10.0, scale, 0.0001, $"after {tenths - 2} notches");
        }

        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, scale, 0.0001, "eight notches reach the ceiling");
    }

    // A settings file edited by hand is not guaranteed to hold a value in range. The out-of-range value is
    // brought in FIRST and the notch applied to that, so a stored 9.0 steps down from the ceiling rather
    // than from 9.0 -- one visible notch, which is what a wheel press has to mean.
    [TestMethod]
    public void AStoredValueOutOfRange_IsBroughtBackIn()
    {
        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, Scale(9.0, 120), 0.0001,
            "already at the ceiling, so it stays");
        Assert.AreEqual(QuickPanelSettings.DefaultIconScale - Lertaro.App.Views.QuickPanel.QuickPanelWindow.IconScaleStep,
            Scale(9.0, -120), 0.0001, "clamped to the ceiling first, then one notch down from it");
    }

    // A stored value that is exactly nothing means the setting was never written, so it answers as the
    // default rather than as the floor: a panel opening with every icon at its smallest would read as a
    // bug, and 1.0 is also what keeps the tier behaving exactly as the settings page describes it.
    [TestMethod]
    public void NothingStored_AnswersAsTheDefault()
    {
        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, QuickPanelSettings.ClampIconScale(0), 0.0001);
        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, QuickPanelSettings.ClampIconScale(-3), 0.0001);
        Assert.AreEqual(QuickPanelSettings.DefaultIconScale, QuickPanelSettings.ClampIconScale(double.NaN), 0.0001);
    }

    [TestMethod]
    public void Clamp_LeavesAnInRangeValueAlone()
        => Assert.AreEqual(0.6, QuickPanelSettings.ClampIconScale(0.6), 0.0001);

    // What the code around these numbers depends on, rather than the literals themselves: the default
    // sits inside the range, and the range divides into whole notches from the floor to it. Transcribing
    // the constants would be a test that cannot fail; this one can.
    [TestMethod]
    public void TheScaleRange_IsCoherent()
    {
        Assert.IsGreaterThan(QuickPanelSettings.MinIconScale, QuickPanelSettings.DefaultIconScale,
            "the default has to be inside the range the user can reach");

        var steps = QuickPanelSettings.DefaultIconScale / Lertaro.App.Views.QuickPanel.QuickPanelWindow.IconScaleStep;
        Assert.AreEqual(Math.Round(steps), steps, 0.0001, "the range has to divide into whole notches");
    }

    // The ceiling is the tier exactly as chosen, not something above it: past 1.0 the picture would be
    // given more room than it can fill, which is the one thing the tile metrics exist to avoid.
    [TestMethod]
    public void TheCeiling_IsTheTierAsChosen() => Assert.AreEqual(1.0, QuickPanelSettings.DefaultIconScale, 0.0001);
}
