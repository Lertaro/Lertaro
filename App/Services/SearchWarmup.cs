using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs the first search's one-time work while the app sits idle after startup, so the user's first keystroke
/// does not pay it.
/// </summary>
/// <remarks>
/// Measured with the render probe: the first paint of a session cost ~60 ms against 4-9 ms for every later one,
/// with only 181 rows on screen -- the data was never the cost. What that first paint actually pays is the JIT
/// of the result-mapping path plus the one-time first touch of the reads it does: the history snapshot, the
/// favorites' reachability probes, and the Start-Menu/settings catalog. All of that is read-only, and the mapper
/// records nothing (history records OPENED items, not searches), so warming it here cannot look like a search
/// the user made.
/// </remarks>
internal static class SearchWarmup
{
    /// <summary>Schedules the warm-up on a pool thread; returns immediately.</summary>
    /// <remarks>
    /// Deliberately idle-timed rather than lazy: warming on the first summon (double Ctrl) would move the cost
    /// from the first keystroke to the first summon, which is the same stall one gesture later. If the user does
    /// start typing first, this simply runs alongside the real search -- it holds no lock and shares no state
    /// beyond the read-only caches the mapper already uses, so the worst case is a little extra parallel work.
    /// </remarks>
    public static void Schedule()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                // Two shapes: one query nothing can match (catalog + history + favorites, and the JIT of the
                // whole mapping path), then a common letter so the fuzzy/highlight paths are compiled too.
                ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(null, "\u0001warmup", null, null, false);
                ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(null, "a", null, null, false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchWarmup] Warm-up skipped: {ex.Message}", LogLevel.Debug);
            }
        });
    }
}
