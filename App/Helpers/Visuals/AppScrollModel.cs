namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// The app-side scroll model, ported from Apex's common/core.h, common/release.h and shared/model.h -- the
/// rules the app actually runs, which are NOT the same as the model header's own defaults.
/// </summary>
/// <remarks>
/// Three pieces, each measured and not to be "tidied" apart:
///   * the TRAVEL comes from Travel() with the speed budget, and nothing is scaled after it (a gain on the
///     output was tried and removed: it moves the ceiling with the floor and puts "how far a notch goes" in
///     two places);
///   * the LENGTH comes from the release model: the Glide setting for a lone message, Glide + 200.0 ms
///     for one inside a roll, so the ending is drawn out while the wheel is being turned;
///   * the SHAPE comes from AppEaseFor -- the model's flat rate is right for a roll that keeps going, the
///     fully eased shape for one that stops.
/// </remarks>
internal static class AppScrollModel
{
    // The panel's four parameters, at their factory values (Apex common/config.h).
    internal const double GlideMs = 200.0;         // -> Params.windowMs: the shortest window, a slow notch
    internal const double SlowStepDeltas = 5.0;    // -> Travel(startDeltas): the travel of the first/slowest notch
    internal const double RampUpDeltas = 1000.0;   // -> Travel(budgetDeltas): how much turning reaches full size
    internal const double TopSpeedMul = 1.0;       // -> Travel(speedMul): 1 = stop at the wheel's own speed (user's call)

    /// <summary>The speed budget's time constant, ms.</summary>
    internal const double DecayMs = 300.0;

    /// <summary>How much longer a message's window is while the wheel is being turned. FIXED: not a setting.</summary>
    internal const double ReleaseMs = 200.0;

    private const double WindowLoMs = 100.0;
    private const double WindowHiMs = 400.0;

    /// <summary>The Glide setting, clamped to the window envelope the model was tuned over.</summary>
    internal static double BaseWindowMs(double glideMs) => Math.Clamp(glideMs, WindowLoMs, WindowHiMs);

    /// <summary>Whether this message's window would overlap its predecessor's -- i.e. whether it is a roll.</summary>
    internal static bool IsRoll(double gapMs, double baseWindowMs) =>
        gapMs > 0.0 && baseWindowMs > 0.0 && gapMs < baseWindowMs;

    /// <summary>The window a message gets WHILE ROLLING: the Glide setting plus the fixed release.</summary>
    internal static double RollWindowMs(double baseWindowMs) =>
        baseWindowMs > 0.0 ? baseWindowMs + ReleaseMs : baseWindowMs;

    /// <summary>
    /// What the caller should put in <c>Params.PayoutEase</c> for a message with this gap and window.
    /// </summary>
    /// <remarks>
    /// A lone notch must come to rest, and easing it is what makes the ending smooth, so a non-overlapping
    /// message is fully eased (1.0). A tiling roll (gap == the window) is the one case the model's flat rate
    /// handles correctly -- and easing it is measurably WORSE (ripple 0.0% -> 45.0%), because its windows butt
    /// end to end and there is no gap for the ease to fill -- so that case asks for 0. Everything else that
    /// overlaps gets the fully eased shape too. (The model's own 0.5 is not enough: a window's rate at its end
    /// is exactly 1 - ease, so 0.5 still stops at 40% of peak.)
    /// </remarks>
    internal static double AppEaseFor(double gapMs, double windowMs)
    {
        if (!(windowMs > 0.0)) return 0.0;   // no window at all: nothing to shape
        if (gapMs <= 0.0) return 1.0;        // the first message of a gesture: a lone notch until a roll proves otherwise
        if (gapMs > windowMs) return 1.0;    // no overlap: a lone notch, and it must come to rest
        if (Math.Abs(windowMs / gapMs - 1.0) < 0.08) return 0.0; // tiling: flat rate is right, easing is worse
        return 1.0;                          // everything else overlaps: the fully eased shape
    }

    /// <summary>
    /// How far a message's notch travels, from the speed budget. <paramref name="messageDeltas"/> is that
    /// message's own size, so each device caps at what it actually reports.
    /// </summary>
    /// <remarks>
    /// u = budget / budgetDeltas (0 at a standstill, 1 once the budget is full) and
    /// travel = start + (cap - start) * min(u, speedMul). speedMul raises the CEILING only: past cap the same
    /// straight line simply keeps climbing, so nothing below the baseline changes and there is no step at the
    /// join.
    /// </remarks>
    internal static double Travel(double messageDeltas, double budget, double budgetDeltas, double startDeltas,
        double speedMul)
    {
        var cap = messageDeltas < 0.0 ? 0.0 : messageDeltas;
        var start = startDeltas < 0.0 ? 0.0 : startDeltas;
        if (start > cap) start = cap;                       // never move more than the notch itself
        if (!(budgetDeltas > 0.0)) return cap;              // no ramp: every notch goes its full size

        var mul = speedMul > 1.0 ? speedMul : 1.0;          // 1 = stop at the wheel's own speed
        var u = budget / budgetDeltas;
        if (u < 0.0) u = 0.0;
        if (u > mul) u = mul;
        return start + (cap - start) * u;
    }

    /// <summary>
    /// "How much has the wheel been turning lately", in deltas: each message adds its own deltas and the
    /// running total DECAYS over time, so the ramp follows the current speed instead of latching.
    /// </summary>
    /// <remarks>
    /// The decay is an Euler step of the continuous law <c>db/dt = rate - b/tau</c>, NOT an exponential of the
    /// gap: the two agree only when the gaps are small, and a notched mouse's gaps (125 ms) are not small next
    /// to tau (300 ms) -- measured, the exponential form settles at a different level for a notched mouse than
    /// for a free-spinner at the same hand speed (14% travel difference on one flick). The Euler step's steady
    /// state is exactly rate*tau whatever the gap, so the two devices match. Counting DELTAS (not messages) is
    /// what makes it device-independent: 120 in one message and eight 15s both add 120 over the same hand move.
    /// </remarks>
    internal sealed class SpeedBudget
    {
        private double _budget;

        internal void Reset() => _budget = 0.0;

        internal double Value => _budget;

        /// <summary>Adds this message's magnitude (always positive) and lets the budget decay since the last message.</summary>
        internal double Add(double messageDeltas, double gapMs)
        {
            const double tau = DecayMs;
            if (gapMs <= 0.0) _budget += messageDeltas;                  // no elapsed time (first message)
            else if (gapMs >= tau) _budget = messageDeltas;              // long gap: the past has decayed away
            else _budget = _budget * (1.0 - gapMs / tau) + messageDeltas; // Euler step of db/dt = rate - b/tau
            return _budget;
        }
    }
}