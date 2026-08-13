namespace Lertaro.App.ViewModels.Search;

/// <summary>
/// Decides which ticks of a still-arriving result stream are worth repainting on.
/// </summary>
/// <remarks>
/// The rule is a duty cycle and nothing else: paint whenever anything new has arrived, unless the
/// previous paint was expensive enough that painting again this soon would take more than a small
/// share of the UI thread. Because the budget is a multiple of what the last paint ACTUALLY cost, it
/// needs no notion of how big the list is or how far into a search it is -- cheap paints run on every
/// tick, expensive ones space themselves out, and the thread stays free either way.
///
/// Earlier versions instead required the list to grow by a factor before repainting, on the assumption
/// that a paint necessarily costs what the whole list costs. That was true when every paint rebuilt
/// and re-reconciled everything, and it made the displayed count freeze for minutes near the end of a
/// long search -- the list could no longer double, so nothing painted at all. It stopped being true
/// once a paint could be told which rows actually changed (see StreamingResultAccumulator's
/// FirstChangedIndex), which makes a late-search paint cost a handful of rows rather than six hundred
/// thousand, and cheap enough that this rule lets it through on essentially every tick.
/// </remarks>
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
