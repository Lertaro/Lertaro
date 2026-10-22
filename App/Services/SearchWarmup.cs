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
    /// Deliberately not lazy: warming on the first summon (double Ctrl) would move the cost from the first
    /// keystroke to the first summon, which is the same stall one gesture later. If the user does start typing
    /// first, this simply runs alongside the real search -- it holds no lock and shares no state beyond the
    /// read-only caches the mapper already uses, so the worst case is a little extra parallel work.
    /// </remarks>
    public static void Schedule()
    {
        _ = Task.Run(async () =>
        {
            var started = Environment.TickCount64;
            try
            {
                await Task.Yield();

                // Two shapes: one query nothing can match (catalog + history + favorites, and the JIT of the
                // whole mapping path), then a common letter so the fuzzy/highlight paths are compiled too.
                ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(null, "\u0001warmup", null, null, false);
                ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(null, "a", null, null, false);

                // The two calls above pass no index results, so they never reach the ranking kernel or the
                // result list: the first real search still paid that first time (measured: the first paint cost
                // 109 ms with no index results in hand, against 0-16 ms for every later one). Feed a small
                // synthetic batch through the same path so RankPolicy, the type order and the highlight JIT are
                // warmed too. The rows are fabricated and only ever cross this method boundary -- nothing is
                // recorded, so this cannot look like a search the user made.
                var warmRows = new List<SearchResult>
                {
                    new() { Path = @"C:\Warmup\a.txt", Name = "a", IsDir = false },
                    new() { Path = @"C:\Warmup\ab", Name = "ab", IsDir = false },
                    new() { Path = @"C:\Warmup\warmup", Name = "warmup", IsDir = true },
                };
                ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(warmRows, "a", null, null, false);

                // Info, not Debug: whether this ran is the first thing to check when the first search still
                // stalls, and a silent warm-up made that unknowable.
                Logger.Log($"[SearchWarmup] Warm-up finished in {Environment.TickCount64 - started} ms.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchWarmup] Warm-up failed after {Environment.TickCount64 - started} ms: {ex.Message}", LogLevel.Info);
            }
        });
    }
}
