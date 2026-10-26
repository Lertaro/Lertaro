using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

/// <summary>
/// The app-side scroll rules (AppScrollModel): travel from the speed budget, the release model's window,
/// and the payout shape. Each case pins a rule that was measured, not a preference.
/// </summary>
[TestClass]
public class AppScrollModelTests
{
    // -- Travel: how far a notch goes, from the budget. --

    [TestMethod]
    public void Travel_AtAStandstill_MovesOnlyTheStartAmount()
    {
        // budget 0 -> u = 0 -> travel is startDeltas, never the whole notch
        Assert.AreEqual(5.0, AppScrollModel.Travel(120.0, 0.0, 1000.0, 5.0, 1.5), 1e-9);
    }

    [TestMethod]
    public void Travel_AtFullBudget_MovesTheMessagesOwnSize()
    {
        // u = 1 -> travel = cap; and with speedMul = 1 the ceiling is exactly cap
        Assert.AreEqual(120.0, AppScrollModel.Travel(120.0, 1000.0, 1000.0, 5.0, 1.0), 1e-9);
        Assert.AreEqual(120.0, AppScrollModel.Travel(120.0, 99999.0, 1000.0, 5.0, 1.0), 1e-9);
    }

    [TestMethod]
    public void Travel_SpeedMultiplier_RaisesOnlyTheCeiling()
    {
        // Past cap the same line keeps climbing: 1.5 -> 1.5x the message, and nothing below changes.
        Assert.AreEqual(180.0, AppScrollModel.Travel(120.0, 100000.0, 1000.0, 5.0, 1.5), 1e-9);
        Assert.AreEqual(5.0 + (120.0 - 5.0) * 0.5, AppScrollModel.Travel(120.0, 500.0, 1000.0, 5.0, 1.5), 1e-9);
    }

    [TestMethod]
    public void Travel_NeverMovesMoreThanTheNotch_AndHandlesNoRamp()
    {
        Assert.AreEqual(8.0, AppScrollModel.Travel(8.0, 0.0, 1000.0, 5.0, 1.0), 1e-9);   // start clamped to cap
        Assert.AreEqual(120.0, AppScrollModel.Travel(120.0, 0.0, 0.0, 5.0, 1.0), 1e-9);   // no ramp: full size
    }

    // -- SpeedBudget: the decay that makes the ramp follow the speed. --

    [TestMethod]
    public void SpeedBudget_FirstMessageJustAdds()
    {
        var budget = new AppScrollModel.SpeedBudget();
        Assert.AreEqual(120.0, budget.Add(120.0, 0.0), 1e-9);
    }

    [TestMethod]
    public void SpeedBudget_LongGapForgetsThePast()
    {
        var budget = new AppScrollModel.SpeedBudget();
        budget.Add(120.0, 0.0);
        Assert.AreEqual(15.0, budget.Add(15.0, AppScrollModel.DecayMs), 1e-9);
        Assert.AreEqual(15.0, budget.Add(15.0, AppScrollModel.DecayMs * 10), 1e-9);
    }

    [TestMethod]
    public void SpeedBudget_DecaysByTheEulerStep_NotAnExponentialOfTheGap()
    {
        var budget = new AppScrollModel.SpeedBudget();
        budget.Add(120.0, 0.0);
        // db = b*(1 - gap/tau) + mag, NOT b*exp(-gap/tau) + mag
        Assert.AreEqual(120.0 * 0.5 + 10.0, budget.Add(10.0, AppScrollModel.DecayMs / 2.0), 1e-9);
    }

    [TestMethod]
    public void SpeedBudget_IsDeviceIndependent_SameDeltasSameBudget()
    {
        // 120 in one message vs eight free-spin messages of 15: the same hand movement, the same budget.
        var one = new AppScrollModel.SpeedBudget();
        one.Add(120.0, 0.0);
        var eight = new AppScrollModel.SpeedBudget();
        for (var i = 0; i < 8; i++) eight.Add(15.0, 0.0);
        Assert.AreEqual(one.Value, eight.Value, 1e-9);
    }

    // -- The release model: how long a message's window is. --

    [TestMethod]
    public void Release_LoneMessageKeepsTheGlideWindow_RollAddsTheFixedRelease()
    {
        const double baseWindow = 200.0;
        Assert.IsFalse(AppScrollModel.IsRoll(200.0, baseWindow));
        Assert.AreEqual(baseWindow, AppScrollModel.RollWindowMs(0.0), 1e-9);   // guard: nothing to lengthen
        var rolling = AppScrollModel.IsRoll(50.0, baseWindow);
        Assert.IsTrue(rolling);
        Assert.AreEqual(baseWindow + AppScrollModel.ReleaseMs, AppScrollModel.RollWindowMs(baseWindow), 1e-9);
        Assert.AreEqual(200.0, AppScrollModel.ReleaseMs, 1e-9); // a constant, and this is its value
    }

    [TestMethod]
    public void BaseWindow_IsClampedToTheModelsEnvelope()
    {
        Assert.AreEqual(100.0, AppScrollModel.BaseWindowMs(10.0), 1e-9);
        Assert.AreEqual(400.0, AppScrollModel.BaseWindowMs(900.0), 1e-9);
        Assert.AreEqual(200.0, AppScrollModel.BaseWindowMs(200.0), 1e-9);
    }

    // -- The payout shape: the app's own rule, not the model's. --

    [TestMethod]
    public void AppEase_IsFullForALoneMessage_AndZeroForATilingRoll()
    {
        Assert.AreEqual(1.0, AppScrollModel.AppEaseFor(0.0, 200.0), 1e-9);      // first message of a gesture
        Assert.AreEqual(1.0, AppScrollModel.AppEaseFor(300.0, 200.0), 1e-9);   // no overlap: must settle
        Assert.AreEqual(0.0, AppScrollModel.AppEaseFor(200.0, 200.0), 1e-9);   // tiling: easing is worse
        Assert.AreEqual(1.0, AppScrollModel.AppEaseFor(50.0, 200.0), 1e-9);    // overlap: fully eased
        Assert.AreEqual(0.0, AppScrollModel.AppEaseFor(200.0, 0.0), 1e-9);     // no window: nothing to shape
    }
}

/// <summary>
/// The timing model itself (MODEL 3.0): one window per amount, handed over across its window. Its promise is
/// that the total handed over equals the total fed -- that is what "take how much, give how much" means.
/// </summary>
[TestClass]
public class Anim3CoreTests
{
    private static Anim3.Params Window(double ms, double ease = 0.0)
        => new() { WindowMs = ms, PayoutEase = ease };

    [TestMethod]
    public void Conservation_WhateverIsFedComesOut()
    {
        var glide = new Anim3.Glide();
        var p = Window(200.0, Anim3.EaseAmount);
        var random = new Random(7);
        var fed = 0.0;
        for (var i = 0; i < 20; i++)
        {
            var amount = (random.NextDouble() - 0.5) * 240.0;
            glide.Feed(amount, p);
            fed += amount;
        }

        var outSum = 0.0;
        for (var i = 0; i < 4000 && glide.Active; i++) outSum += glide.Tick(0.001, p);
        Assert.AreEqual(fed, outSum, 1e-9);
    }

    [TestMethod]
    public void AWindowEndsAtExactlyItsOwnLength()
    {
        var glide = new Anim3.Glide();
        var p = Window(150.0);
        glide.Feed(60.0, p);
        var total = 0.0;
        for (var i = 0; i < 200; i++) total += glide.Tick(0.001, p);   // 200 ms of 1 ms steps
        Assert.AreEqual(60.0, total, 1e-9);
        Assert.IsFalse(glide.Active);
    }

    [TestMethod]
    public void TheTotalDoesNotDependOnTheFrameRate()
    {
        // The tail is coarser at 15 ms, not longer: 1 ms frames deliver the same total.
        var fine = new Anim3.Glide();
        var coarse = new Anim3.Glide();
        var p = Window(200.0, Anim3.EaseAmount);
        fine.Feed(30.0, p);
        coarse.Feed(30.0, p);
        var a = 0.0;
        for (var i = 0; i < 400; i++) a += fine.Tick(0.001, p);
        var b = 0.0;
        for (var i = 0; i < 27; i++) b += coarse.Tick(0.015, p);
        Assert.AreEqual(30.0, a, 1e-9);
        Assert.AreEqual(30.0, b, 1e-9);
    }

    [TestMethod]
    public void AnEasedWindowStartsSlowerThanAFlatOne()
    {
        // At a quarter of the window the eased shape has delivered less; both end at the full amount.
        var flat = new Anim3.Glide();
        var eased = new Anim3.Glide();
        var pf = Window(200.0);
        var pe = Window(200.0, 1.0);
        flat.Feed(100.0, pf);
        eased.Feed(100.0, pe);
        var flatQuarter = 0.0;
        var easedQuarter = 0.0;
        for (var i = 0; i < 50; i++) { flatQuarter += flat.Tick(0.001, pf); easedQuarter += eased.Tick(0.001, pe); }
        Assert.IsTrue(easedQuarter < flatQuarter);
        Assert.AreEqual(25.0, flatQuarter, 1e-9);
    }

    [TestMethod]
    public void OverlappingWindowsAddUp()
    {
        var glide = new Anim3.Glide();
        var p = Window(200.0);
        glide.Feed(10.0, p);
        glide.Feed(10.0, p);      // two in flight
        Assert.AreEqual(2, glide.InFlight);   // both windows are in flight at once
        Assert.AreEqual(20.0, Remaining(glide, p), 1e-9);
    }

    private static double Remaining(Anim3.Glide glide, Anim3.Params p)
    {
        var rest = 0.0;
        for (var i = 0; i < 4000 && glide.Active; i++) rest += glide.Tick(0.001, p);
        return rest;
    }

    [TestMethod]
    public void PayoutEaseFor_TheModelsOwnRule()
    {
        // The model header's own rule is still here and still does what it says: no overlap -> 0, and an
        // integer ratio is excluded because the flat rate is already right there.
        Assert.AreEqual(0.0, Anim3.PayoutEaseFor(300.0, 200.0), 1e-9);
        Assert.AreEqual(0.0, Anim3.PayoutEaseFor(200.0, 200.0), 1e-9);
        Assert.AreEqual(Anim3.EaseAmount, Anim3.PayoutEaseFor(150.0, 200.0), 1e-9);
    }
}

/// <summary>
/// Which sender a wheel message came from. Only the two that report a wheel are eased; a touchpad is scaled
/// instead, so a wrong verdict here changes how the device feels.
/// </summary>
[TestClass]
public class WheelDeviceTests
{
    private const long TouchMarker = 0xFF515700L;

    private WheelDevice.Tracker _tracker = new();   // one gesture per test: the shared one is not safe in parallel

    [TestInitialize]
    public void FreshGesture() => _tracker = new WheelDevice.Tracker();

    [TestMethod]
    public void AWholeNotchIsANotchedMouse()
    {
        Assert.AreEqual(WheelDevice.Kind.Notched, _tracker.Feed(120, 0));
        Assert.AreEqual(WheelDevice.Kind.Notched, _tracker.Feed(-120, 0));
    }

    [TestMethod]
    public void ARegularSubNotchStepIsAFreeSpinningWheel()
    {
        Assert.AreEqual(WheelDevice.Kind.Unknown, _tracker.Feed(15, 0));
        Assert.AreEqual(WheelDevice.Kind.Unknown, _tracker.Feed(15, 0));
        Assert.AreEqual(WheelDevice.Kind.FreeSpin, _tracker.Feed(15, 0));
    }

    [TestMethod]
    public void TheVerdictCarriesIntoTheNextGesture_SoItsFirstMessageCanGlide()
    {
        // Every gesture used to start from Unknown, so a free-spinning wheel's first message was passed
        // through un-eased -- the "sticks once, then glides" feel. The verdict is kept for a second instead.
        Assert.AreEqual(WheelDevice.Kind.Unknown, _tracker.Feed(15, 0));
        Assert.AreEqual(WheelDevice.Kind.Unknown, _tracker.Feed(15, 0));
        Assert.AreEqual(WheelDevice.Kind.FreeSpin, _tracker.Feed(15, 0));

        _tracker.Clear();   // the next gesture starts here, the way the behaviour resets the shared tracker

        Assert.AreEqual(WheelDevice.Kind.FreeSpin, _tracker.Feed(15, 0));
    }

    [TestMethod]
    public void ANotchedVerdictAlsoCarriesIntoTheNextGesture()
    {
        Assert.AreEqual(WheelDevice.Kind.Notched, _tracker.Feed(120, 0));

        _tracker.Clear();

        Assert.AreEqual(WheelDevice.Kind.Notched, _tracker.Feed(30, 0));
    }

    [TestMethod]
    public void IrregularSubNotchValuesAreATouchpad()
    {
        _tracker.Feed(17, 0);
        _tracker.Feed(41, 0);
        Assert.AreEqual(WheelDevice.Kind.Touchpad, _tracker.Feed(9, 0));
    }

    [TestMethod]
    public void TheOperatingSystemMarkerWins_AndSticks()
    {
        // The marker is an explicit statement by Windows: it beats any numeric guess, and the gesture keeps it.
        Assert.AreEqual(WheelDevice.Kind.Touchpad, _tracker.Feed(15, TouchMarker | 1));
        Assert.AreEqual(WheelDevice.Kind.Touchpad, _tracker.Feed(15, 0));
        Assert.AreEqual(WheelDevice.Kind.Notched, _tracker.Feed(120, 0)); // a whole notch is a notched mouse, not a guess
    }
}