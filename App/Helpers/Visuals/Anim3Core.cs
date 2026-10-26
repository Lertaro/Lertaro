namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// MODEL 3.0 -- "take how much, give how much, spread over a stated time." The C# port of the reaper
/// plugin's src/anim3_core.h, which is the authority for this model.
/// </summary>
/// <remarks>
/// The wheel reports an amount and the model hands that amount over spread across a stated time. That is
/// ALL it does: no eat, no spit, no speed-dependent shaping -- what goes in is what comes out, only
/// spread out. A received amount becomes ONE window of exactly WindowMs, handed over in equal parts
/// across that window; overlapping windows simply add up, so a continuing roll never stops between
/// notches. The model is therefore exactly conservative, and the only thing it shapes is TIMING.
///
/// Nothing here depends on WPF or on which device sent the amount -- the classification is the caller's
/// (see WheelDevice). This is pure timing.
/// </remarks>
internal static class Anim3
{
    /// <summary>Model parameters; see the plugin's anim3_core.h for what each one means.</summary>
    internal sealed class Params
    {
        /// <summary>How long one amount takes to be handed over, in milliseconds (the caller clamps it).</summary>
        internal double WindowMs = 100.0;

        /// <summary>
        /// How much of the smoothstep payout shape THIS amount's window uses (0 = constant rate). The
        /// caller decides it per message, from the gap that produced it: see <see cref="PayoutEaseFor"/>.
        /// </summary>
        internal double PayoutEase;

        internal double WindowSec => (WindowMs > 0.0 ? WindowMs : 0.0) * 0.001;
    }

    /// <summary>How much smoothstep to blend into an OVERLAPPING window's payout (measured optimum).</summary>
    internal const double EaseAmount = 0.5;

    /// <summary>
    /// What the caller should put in <see cref="Params.PayoutEase"/> for a message whose gap was
    /// <paramref name="gapMs"/>, with a window of <paramref name="windowMs"/>.
    /// </summary>
    /// <remarks>
    /// Only an OVERLAPPING roll can ripple, and only sometimes: the ripple comes from the number of
    /// windows in flight oscillating, which needs the window to be a NON-INTEGER multiple of the gap.
    /// When it is an integer multiple the constant-rate payout is already flat and easing can only add a
    /// ripple, so the ease is asked for only when the windows overlap AND the ratio is not (almost) an
    /// integer. Everywhere else the caller asks for 0 and the payout is unchanged.
    /// </remarks>
    internal static double PayoutEaseFor(double gapMs, double windowMs)
    {
        if (!(gapMs > 0.0) || !(windowMs > 0.0) || gapMs >= windowMs) return 0.0;

        var ratio = windowMs / gapMs;
        var nearestInteger = ratio - Math.Floor(ratio) < 0.5 ? Math.Floor(ratio) : Math.Ceiling(ratio);
        return Math.Abs(ratio - nearestInteger) < 0.08 ? 0.0 : EaseAmount;
    }

    /// <summary>The motion: one window per amount the wheel reported, handed over across <see cref="Params.WindowMs"/>.</summary>
    internal sealed class Glide
    {
        // A free-spinning wheel can report ~1000 amounts/s; at the longest window (400 ms) that is ~400
        // windows in flight. 2048 leaves a wide margin, and anything beyond is handed over at once
        // rather than dropped, so an amount is never lost to the cap.
        private const int MaxEnv = 2048;

        private readonly double[] _amounts = new double[MaxEnv];
        private readonly double[] _ages = new double[MaxEnv];
        private readonly double[] _eases = new double[MaxEnv];
        private int _count;
        private double _due;

        internal void Reset()
        {
            _count = 0;
            _due = 0.0;
        }

        internal bool Active => _count > 0 || _due != 0.0;

        /// <summary>Windows still being handed over (for diagnosis).</summary>
        internal int InFlight => _count;

        /// <summary>One amount the WHEEL reported, in the caller's unit (signed), stored exactly as it came.</summary>
        internal void Feed(double amount, Params p)
        {
            if (amount == 0.0) return;
            if (p.WindowSec <= 0.0)
            {
                _due += amount; // no window: hand it over on the next tick
                return;
            }

            if (_count < MaxEnv)
            {
                _amounts[_count] = amount;
                _ages[_count] = 0.0;
                _eases[_count] = p.PayoutEase; // this window's shape, chosen by the caller for this amount
                _count++;
            }
            else
            {
                _due += amount; // the cap is full: do not lose it, just stop spreading it
            }
        }

        /// <summary>Advances by dt seconds; returns the amount to hand over this frame.</summary>
        internal double Tick(double dt, Params p)
        {
            if (dt <= 0.0) return 0.0;

            var output = _due;
            _due = 0.0;

            var w = p.WindowSec;
            if (w <= 0.0)
            {
                _count = 0;
                return output; // every window is "now"
            }

            // Each window hands its amount over across exactly its own WindowMs. Whatever shape is used,
            // this frame's share is taken OFF the window and the window's own progress decides the rest,
            // so over the window the shares telescope to exactly the amount it held when it opened: the
            // total handed over equals the total fed, to the last fraction.
            var kept = 0;
            for (var i = 0; i < _count; i++)
            {
                var u0 = _ages[i] / w;           // progress entering this frame
                var u1 = (_ages[i] + dt) / w;    // progress leaving it
                var s0 = PayoutFrac(u0, _eases[i]);
                var s1 = PayoutFrac(u1, _eases[i]);
                var remaining = 1.0 - s0;        // of the ORIGINAL amount, what is still to come
                var pay = remaining > 1e-12 ? _amounts[i] * (s1 - s0) / remaining : _amounts[i];
                output += pay;
                _amounts[i] -= pay;              // really taken off the window
                _ages[i] += dt;
                if (_ages[i] < w)                // still running: keep it for the next frame
                {
                    _amounts[kept] = _amounts[i];
                    _ages[kept] = _ages[i];
                    _eases[kept] = _eases[i];
                    kept++;
                }
            }

            _count = kept;
            return output;
        }

        // THE PAYOUT SHAPE. A window pays its amount over its WindowMs, not necessarily at a constant
        // rate: an eased shape has a payout rate of zero at both ends, so a window being added or
        // finishing no longer puts a step into the output rate. It only helps when windows OVERLAP,
        // which is why the caller chooses it per message (Params.PayoutEase).
        private static double PayoutFrac(double u, double ease)
        {
            if (u <= 0.0) return 0.0;
            if (u >= 1.0) return 1.0;
            if (ease <= 0.0) return u;                       // constant rate
            var smoothstep = u * u * (3.0 - 2.0 * u);        // zero slope at both ends
            return (1.0 - ease) * u + ease * smoothstep;
        }
    }
}