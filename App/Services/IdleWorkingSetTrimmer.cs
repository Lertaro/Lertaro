using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs <see cref="IdleWorkingSetTrimGate"/>'s decision on a timer. See there for why the trim waits.
/// </summary>
internal static class IdleWorkingSetTrimmer
{
    // Long enough that a burst of summons never pays for it -- the trace shows show/hide cycles 300ms
    // apart and whole rebuilds lasting twelve seconds -- and short enough that a window genuinely left
    // alone still hands its pages back promptly.
    private const long IdleMs = 15_000;

    private static readonly IdleWorkingSetTrimGate Gate = new(IdleMs);
    private static System.Threading.Timer? _timer;
    private static readonly object StartLock = new();

    public static void WindowHidden()
    {
        Gate.WindowHidden(Environment.TickCount64);
        EnsureTimer();
    }

    /// <summary>Called at the very start of a summon, before anything touches the window.</summary>
    public static void WindowShowing() => Gate.WindowShowing();

    private static void EnsureTimer()
    {
        lock (StartLock)
            _timer ??= new System.Threading.Timer(_ => Tick(), null, IdleMs, 5_000);
    }

    private static void Tick()
    {
        if (!Gate.ShouldTrim(Environment.TickCount64))
            return;

        try
        {
            Win32Api.TrimWorkingSet();
        }
        catch
        {
            // Best effort -- this is a courtesy to Task Manager, never something to fail over.
        }
    }
}
