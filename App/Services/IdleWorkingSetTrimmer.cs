using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs <see cref="IdleWorkingSetTrimGate"/>'s decision on a timer. See there for why the trim waits.
/// </summary>
internal static class IdleWorkingSetTrimmer
{
    // How long the process has to stay quiet before the working set is handed back. Matches the service's
    // own IdleTrimGate (SearchEngine.IdleTrimAfterMs), which this class is documented as mirroring -- the
    // two had drifted to 15s versus 3s for no reason anyone recorded.
    //
    // 3s still collapses a summon burst with a wide margin (the trace shows show/hide cycles 300ms apart),
    // which is the only thing this delay exists to protect: trimming evicts pages the NEXT summon has to
    // fault back in, measured at ~17MB and 70% of that summon's time. Work that must not be interrupted is
    // tracked explicitly rather than being covered by this window -- background CLI searches via
    // BackgroundSearchStarted/Finished, plugin bursts via RequestTrim -- so it does not have to outlast them.
    private const long IdleMs = 3_000;

    // How often the gate is polled once armed. Must stay below IdleMs or the poll interval, not the
    // threshold, becomes the real delay -- at the previous 5s poll a 3s threshold could not fire until
    // 5s. Each tick is a lock and a comparison, so polling this often costs nothing.
    private const int PollMs = 1_000;

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

    /// <summary>Requests a deferred trim after a plugin finishes a burst of background work.</summary>
    public static void RequestTrim()
    {
        Gate.RequestTrim(Environment.TickCount64);
        EnsureTimer();
    }

    /// <summary>Marks a CLI request routed through the App's search pipe as active.</summary>
    public static void BackgroundSearchStarted()
    {
        Gate.BackgroundSearchStarted();
        EnsureTimer();
    }

    /// <summary>Schedules a trim after the final CLI search completes and the App remains idle.</summary>
    public static void BackgroundSearchFinished() => Gate.BackgroundSearchFinished(Environment.TickCount64);

    private static void EnsureTimer()
    {
        lock (StartLock)
            _timer ??= new System.Threading.Timer(_ => Tick(), null, IdleMs, PollMs);
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
