using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

// The wheel glide has three intents that pull against each other: a precise single step, a fast spin that
// actually accelerates, and a prompt stop once the wheel is released. A lone notch's 10px/200ms pins its
// punch small, and a glide's speed stabilizes near punch/friction, so that small punch alone cannot build
// momentum. The glide therefore scales each notch's punch with the speed already built (a feedback, not a
// threshold -- a threshold made the second notch jump ~12x and felt like "slow, then suddenly flying").
// These pin the lone-notch tuning, that the spin ramps smoothly and reaches a high speed, and the stop.
[TestClass]
public sealed class SmoothWheelScrollBehaviorTests
{
    private static double SV => SmoothWheelScrollBehavior.StopVelocity;
    private static double FrictionValue => SmoothWheelScrollBehavior.Friction;

    private static double OneNotchPixels =>
        (SmoothWheelScrollBehavior.VelocityPerNotch - SV) / FrictionValue;

    private static double OneNotchSeconds =>
        Math.Log(SmoothWheelScrollBehavior.VelocityPerNotch / SV) / FrictionValue;

    // Replays the Glide's own integration for a series of notches `dt` apart, returning the speed after
    // each. Friction is the lone-notch value throughout, which is what holds while a spin is being fed.
    private static List<double> SimulateSpin(double dt, int notches)
    {
        var speeds = new List<double>();
        var velocity = 0.0;
        for (var n = 0; n < notches; n++)
        {
            velocity *= Math.Exp(-FrictionValue * dt);
            velocity = SmoothWheelScrollBehavior.ClampVelocity(
                velocity + SmoothWheelScrollBehavior.SelectPunch(velocity));
            speeds.Add(velocity);
        }
        return speeds;
    }

    // -- The lone notch: precise, and the two knobs the user actually set. --

    [TestMethod]
    public void ALoneNotchTravelsThePreciseConfiguredDistance() => Assert.AreEqual(SmoothWheelScrollBehavior.NotchPixels, OneNotchPixels, 1e-6,
            "a single deliberate notch must scroll exactly NotchPixels");

    [TestMethod]
    public void ALoneNotchSettlesInTheConfiguredReleaseTime() => Assert.AreEqual(SmoothWheelScrollBehavior.ReleaseMilliseconds / 1000.0, OneNotchSeconds, 1e-6,
            "a single deliberate notch must settle in exactly the configured release time");

    [TestMethod]
    public void TheSolverReproducesTheRequestedDistanceAndRelease()
    {
        foreach (var (pixels, milliseconds) in new[] { (10.0, 200.0), (60.0, 100.0), (30.0, 150.0), (120.0, 300.0) })
        {
            var (punch, friction) = SmoothWheelScrollBehavior.DerivePunchAndFriction(pixels, milliseconds);

            var travel = (punch - SV) / friction;
            var tail = Math.Log(punch / SV) / friction;

            Assert.AreEqual(pixels, travel, 1e-6, $"travel must round-trip for ({pixels}, {milliseconds})");
            Assert.AreEqual(milliseconds / 1000.0, tail, 1e-6, $"release must round-trip for ({pixels}, {milliseconds})");
        }
    }

    [TestMethod]
    public void ANotchFromRestGetsExactlyTheLonePunch() =>
        // The feedback must collapse to the precise punch when nothing is moving, or a deliberate single
        // step would scroll more than NotchPixels.
        Assert.AreEqual(SmoothWheelScrollBehavior.VelocityPerNotch, SmoothWheelScrollBehavior.SelectPunch(0.0), 1e-9);

    // -- The spin: a smooth ramp, and enough of one to feel fast. The regression that shipped was a
    //    speed plateau of ~57 px/s (the original build reached ~1050); a later attempt jumped ~12x between
    //    the first two notches and felt like "slow, then suddenly flying". Both are excluded here. --

    [TestMethod]
    public void ASpinAcceleratesSmoothlyWithoutAFirstStepJump()
    {
        var speeds = SimulateSpin(0.08, 8);

        // The gap between consecutive speeds must grow gently, never spike. A threshold model jumped from
        // ~132 to ~1596 (12x) between the first two notches; a 3x cap on any single step's increase keeps
        // that out while still allowing real acceleration.
        for (var i = 1; i < speeds.Count; i++)
        {
            Assert.IsLessThan(3.0 * speeds[i - 1], speeds[i] + 1.0,
                $"notch {i + 1} jumped too sharply ({speeds[i - 1]:F0} -> {speeds[i]:F0}), which reads as a step not a ramp");
        }
    }

    [TestMethod]
    public void ASustainedSpinReachesAHighSpeed()
    {
        var speeds = SimulateSpin(0.1, 10);

        Assert.IsGreaterThan(1000.0, speeds[^1],
            "a sustained spin must outrun the original build's ~1050 px/s, not plateau in the tens");
    }

    [TestMethod]
    public void ASpinGetsFasterTheFasterItIsTurned()
    {
        // A tighter notch interval must reach a higher speed -- "the faster I spin, the faster it goes".
        var fast = SimulateSpin(0.06, 6)[^1];
        var slower = SimulateSpin(0.15, 6)[^1];

        Assert.IsGreaterThan(slower, fast, "turning the wheel faster must raise the speed");
    }

    [TestMethod]
    public void MovingFasterFeedsInMoreSpeed() =>
        // The feedback that replaced the old threshold: a notch arriving while already moving must feed in
        // strictly more than one arriving at rest -- that is what lets a spin accelerate at all, and it is
        // checked through the method rather than the constant so it cannot fold away.
        Assert.IsGreaterThan(
            SmoothWheelScrollBehavior.SelectPunch(0.0),
            SmoothWheelScrollBehavior.SelectPunch(1000.0),
            "a notch at speed must feed in more than a notch at rest");

    [TestMethod]
    public void ReverseDirectionDoesNotInheritTheOldAcceleration() =>
        Assert.AreEqual(
            SmoothWheelScrollBehavior.SelectPunch(0),
            SmoothWheelScrollBehavior.SelectPunchForDirection(1000, -1),
            1e-9,
            "a reversing notch should cancel existing motion instead of being amplified by it");

    [TestMethod]
    public void CompoundWheelDeltaCountsEveryNotchForSpinFriction()
    {
        Assert.AreEqual(2, SmoothWheelScrollBehavior.AccumulateNotchCount(0, 1000, 2));
        Assert.AreEqual(5, SmoothWheelScrollBehavior.AccumulateNotchCount(3, 100, 2));
        Assert.AreEqual(2, SmoothWheelScrollBehavior.AccumulateNotchCount(3, 151, 2));
    }

    // -- Stopping and the state selection. --

    [TestMethod]
    public void SelectFriction_PicksPerState()
    {
        var lone = FrictionValue;
        var stop = SmoothWheelScrollBehavior.StopFriction;

        // A spin still being fed keeps the lone-notch friction -- that is the one the simulation above
        // assumes, so it is what the acceleration is tuned against.
        Assert.AreEqual(lone, SmoothWheelScrollBehavior.SelectFriction(3, 80.0), 1e-9);

        // Gone quiet: switch up so the stacked speed is shed rather than carried.
        Assert.AreEqual(stop, SmoothWheelScrollBehavior.SelectFriction(3, SmoothWheelScrollBehavior.SpinWindowMilliseconds + 1), 1e-9);

        // A lone notch never takes a spin path, however long the pause -- its precise 10px/200ms is exact
        // only if the friction stays the lone-notch value for the whole glide.
        Assert.AreEqual(lone, SmoothWheelScrollBehavior.SelectFriction(1, 0.0), 1e-9);
        Assert.AreEqual(lone, SmoothWheelScrollBehavior.SelectFriction(1, SmoothWheelScrollBehavior.SpinWindowMilliseconds + 1), 1e-9);
    }

    [TestMethod]
    public void TheSpinWindowOutlastsARealWheelGap() =>
        // Too tight and the friction flips to the stopping value mid-spin, damping out the acceleration;
        // a real wheeling hand feeds notches roughly 60-120 ms apart.
        Assert.IsGreaterThan(120.0, SmoothWheelScrollBehavior.SpinWindowMilliseconds,
            "the spin window must not expire between two notches of a genuine spin");

    [TestMethod]
    public void AReleasedCappedSpinStopsPromptly()
    {
        var tailSeconds = Math.Log(SmoothWheelScrollBehavior.MaxVelocity / SV) / SmoothWheelScrollBehavior.StopFriction;

        Assert.IsLessThan(0.5, tailSeconds, "a released spin must stop promptly, not drift on");
    }

    // -- The ceiling. --

    [TestMethod]
    public void AccumulatedVelocityIsCappedWithoutFlippingItsSign()
    {
        Assert.AreEqual(SmoothWheelScrollBehavior.MaxVelocity, SmoothWheelScrollBehavior.ClampVelocity(1e9), 1e-9);
        Assert.AreEqual(-SmoothWheelScrollBehavior.MaxVelocity, SmoothWheelScrollBehavior.ClampVelocity(-1e9), 1e-9);

        Assert.AreEqual(500.0, SmoothWheelScrollBehavior.ClampVelocity(500.0), 1e-9);
        Assert.AreEqual(-500.0, SmoothWheelScrollBehavior.ClampVelocity(-500.0), 1e-9);
    }

    [TestMethod]
    public void TheCeilingIsTheDoubledPreviousPunch() => Assert.AreEqual(6800.0, SmoothWheelScrollBehavior.MaxVelocity, 1e-9);
}
