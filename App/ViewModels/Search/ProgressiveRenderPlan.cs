namespace Lertaro.App.ViewModels.Search;

/// <summary>
/// Schedules progressive paints when new results arrive and the elapsed time meets a budget based on the
/// previous paint's measured cost. Cheap incremental paints can run each tick; expensive paints leave time
/// for UI input. Do not require proportional result-count growth, which can stall updates near the end of
/// a large search.
/// </summary>
internal sealed class ProgressiveRenderPlan
{
    // Below this the list is too short to be worth an intermediate paint at all -- the search is about
    // to finish and render it in full.
    internal const int MinimumFirstRender = 9;

    // The first paint shows this many at most, however much has already piled up behind it. Getting
    // something on screen immediately matters more than getting all of it, and the rest follows on the
    // next tick.
    internal const int FirstRenderCap = 2_000;

    // Large backlogs are drained in bounded pieces. A single oversized append either raises thousands
    // of WPF collection notifications or crosses the collection's Reset threshold and rebuilds every
    // row already shown; both make the next visible update slower than several modest appends.
    internal const int MaximumProgressiveGrowth = 500;
    internal const int MaximumProgressiveRows = 20_000;

    // How much idle the UI thread is owed between paints, as a multiple of the last paint's own cost.
    // At 8 the thread spends at most about a ninth of its time painting, whatever that costs.
    internal const int IdleMultiplier = 8;

    private int _rendered;
    private long _lastPaintMs;

    /// <summary>Rows covered by the most recent accepted paint.</summary>
    public int Rendered => _rendered;

    /// <summary>How long the caller's most recent paint took. Sets the budget for the next one.</summary>
    public void PaintCompleted(long paintDurationMs) => _lastPaintMs = paintDurationMs;

    /// <summary>
    /// Given the number of results received so far and how long the UI has been left alone since the
    /// last paint, returns the total to paint now, or 0 to skip this tick. A non-zero return advances
    /// the plan, so each call represents one paint actually happening.
    /// </summary>
    public int NextRenderSize(int received, long msSinceLastPaint)
    {
        if (received <= _rendered)
            return 0;

        if (_rendered >= MaximumProgressiveRows)
            return 0;

        if (_rendered == 0)
        {
            if (received < MinimumFirstRender)
                return 0;
            return _rendered = Math.Min(received, FirstRenderCap);
        }

        // Divided rather than multiplied: the multiplication overflows for a large enough paint
        // duration and wraps negative, which turns the budget into no budget at all exactly when the
        // paint was most expensive.
        if (msSinceLastPaint / IdleMultiplier < _lastPaintMs)
            return 0;

        return _rendered = Math.Min(Math.Min(received, MaximumProgressiveRows), _rendered + MaximumProgressiveGrowth);
    }
}
