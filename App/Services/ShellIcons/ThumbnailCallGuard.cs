using System.Windows.Media;

using Lertaro.Core;

namespace Lertaro.App.Services.ShellIcons;

/// <summary>
/// Runs one plugin thumbnail call on its own worker and stops waiting for it after a budget. A plugin
/// that never answers used to hold an icon-loader permit for the life of the process, which froze every
/// result row's icon at its placeholder; here the wait is bounded and the trip puts every provider on a
/// cooldown, so a permanently wedged handler is probed once per window instead of once per row.
/// </summary>
internal static class ThumbnailCallGuard
{
    // Tunable rather than const so the wedged-provider path is testable without a real 1.5 s stall and a
    // real 5 minute cooldown; ExplorerStaInvoker's budget tests are the timing-sensitive flakes this avoids.
    internal static int TimeoutMs = 1500;
    internal static int CooldownMs = 300_000;
    internal static long CooldownUntilUtcTicks;

    public static bool InCooldown => Interlocked.Read(ref CooldownUntilUtcTicks) > DateTime.UtcNow.Ticks;

    // ponytail: a wedged shell handler cannot be interrupted, so its worker thread stays parked until the
    // shell finally answers -- the ceiling is one parked thread per cooldown window, not zero. The upgrade
    // path if that ever shows up in a thread count is a hard cap on concurrent provider calls.
    public static ImageSource? Run(Func<ImageSource?> getThumbnail, string path)
    {
        if (InCooldown)
            return null;

        var task = Task.Run(getThumbnail);
        if (task.Wait(TimeoutMs))
            return task.Result;

        Interlocked.Exchange(ref CooldownUntilUtcTicks, DateTime.UtcNow.AddMilliseconds(CooldownMs).Ticks);
        Logger.Log($"[ShellIconHelper] Thumbnail provider exceeded {TimeoutMs}ms on '{path}'; provider results skipped for {CooldownMs / 1000}s.", LogLevel.Warn);
        return null;
    }
}
