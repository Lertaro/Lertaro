namespace Lertaro.App.ViewModels.Search;

// Coalesces a tiny scoped-first snapshot with the global snapshot that normally follows moments later.
// It only affects an intermediate paint: the final result set is always rendered in full.
internal static class InlineSmallResultRenderDelay
{
    internal const int SettleDelayMs = 90;

    public static bool ShouldDelay(int localMatchCount, long elapsedMs) =>
        localMatchCount < ProgressiveRenderPlan.MinimumFirstRender && elapsedMs < SettleDelayMs;
}
