using Lertaro.Core;

namespace Lertaro.App.Services.ShellIcons;

/// <summary>
/// Runs one plugin thumbnail-provider call on its own worker and stops waiting for it after a budget. This
/// covers both questions the host asks a provider -- can you handle this path, and give me the thumbnail --
/// because the first one is asked from the UI thread for every visible row. A provider that never answered
/// used to hold an icon-loader permit, or the UI thread, open for the life of the process; now the wait is
/// bounded and a trip puts every provider on a cooldown, so a permanently wedged handler is probed once per
/// window instead of once per row.
/// </summary>
internal static class ThumbnailCallGuard
{
    // Tunable rather than const so the wedged-provider path is testable without a real 1.5 s stall and a
    // real 5 minute cooldown; ExplorerStaInvoker's budget tests are the timing-sensitive flakes this avoids.
    internal static int TimeoutMs = 1500;
    internal static int CooldownMs = 300_000;
    internal static long CooldownUntilUtcTicks;

    public static bool InCooldown => Interlocked.Read(ref CooldownUntilUtcTicks) > DateTime.UtcNow.Ticks;

    // Answers default on a timeout or while the cooldown runs: null for "give me the thumbnail", false for
    // "can you handle this path" -- both mean the shell's own icon path takes over.
    // ponytail: a wedged shell handler cannot be interrupted, so its worker thread stays parked until the
    // shell finally answers -- the ceiling is one parked thread per cooldown window, not zero, and a provider
    // that marshals back to the UI thread would now be doing it off one. The upgrade path if either shows up
    // in a thread count is a hard cap on concurrent provider calls.
    public static T Run<T>(Func<T> ask, string path)
    {
        if (InCooldown)
            return default!;

        var task = Task.Run(ask);
        if (task.Wait(TimeoutMs))
            return task.Result;

        Interlocked.Exchange(ref CooldownUntilUtcTicks, DateTime.UtcNow.AddMilliseconds(CooldownMs).Ticks);
        Logger.Log($"[ShellIconHelper] Thumbnail provider exceeded {TimeoutMs}ms on '{path}'; provider calls skipped for {CooldownMs / 1000}s.", LogLevel.Warn);
        return default!;
    }
}
