namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// How one indexing run ended, for the single summary the user gets when the queue drains.
/// </summary>
internal enum ContentIndexRunOutcome
{
    /// <summary>Every queued file was processed and none is left waiting.</summary>
    Completed,

    /// <summary>The run ended because the plugin was stopped or disabled.</summary>
    Interrupted,

    /// <summary>The size cap paused indexing with files still waiting; the user has to act on it.</summary>
    GaveUp
}

/// <summary>
/// Decides whether an indexing run has ended, and how, so exactly one summary reaches the user per
/// run instead of one per batch.
/// </summary>
/// <remarks>
/// The scheduler serves any number of monitored folders and drains them batch by batch, so "a run" is
/// the whole drained queue, not a batch: the run opens when a file is taken from an idle queue and
/// closes when the queue is empty again. Sending the summary from the run's own edge is what keeps a
/// scan of thousands of files, or several folders watched at once, from filling the screen.
/// </remarks>
internal sealed class ContentIndexRunReporter
{
    private bool _runOpen;

    /// <summary>
    /// Call where a batch ended with files still queued, or with the queue drained. When that drain
    /// closes an open run, the run's one summary goes out; every other call is silent.
    /// </summary>
    /// <param name="hasPendingFiles">Whether anything is still waiting to be indexed.</param>
    /// <param name="pausedAtCap">Whether the index is paused at its size cap.</param>
    /// <param name="wasCancelled">Whether the run was stopped rather than left to drain.</param>
    /// <param name="indexedFiles">Files searchable now, for the summary's count.</param>
    public void Observe(bool hasPendingFiles, bool pausedAtCap, bool wasCancelled, int indexedFiles)
    {
        if (hasPendingFiles)
        {
            _runOpen = true;
            return;
        }

        if (!_runOpen)
            return;

        _runOpen = false;
        ContentIndexNotifier.NotifyRunFinished(Classify(pausedAtCap, wasCancelled), indexedFiles);
    }

    /// <summary>
    /// Names why an ended run stopped. A cancelled run is only a cap give-up when the cap is the
    /// reason it was stuck; otherwise the user switched the plugin off under it.
    /// </summary>
    private static ContentIndexRunOutcome Classify(bool pausedAtCap, bool wasCancelled) =>
        !wasCancelled
            ? ContentIndexRunOutcome.Completed
            : pausedAtCap
                ? ContentIndexRunOutcome.GaveUp
                : ContentIndexRunOutcome.Interrupted;
}
