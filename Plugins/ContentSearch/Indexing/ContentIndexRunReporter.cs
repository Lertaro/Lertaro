using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Available summary wording; automatic reporting uses Completed only.
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
/// Reports only the first completed content index, or the first completion after an explicit rebuild.
/// </summary>
/// <remarks>
/// Queue drains during file copies are incremental updates, not new completion announcements.
/// The database retains the completion flag across plugin and application restarts.
/// </remarks>
internal sealed class ContentIndexRunReporter(ContentSearchDatabase database)
{
    private bool _runOpen;
    public bool IsRunOpen => _runOpen;

    /// <summary>
    /// A drained queue is complete only after discovery finishes. Stopping or reaching the cap does
    /// not consume the completion notification; the cap has its own actionable warning.
    /// </summary>
    /// <param name="hasPendingFiles">Whether anything is still waiting to be indexed.</param>
    /// <param name="pausedAtCap">Whether the index is paused at its size cap.</param>
    /// <param name="wasCancelled">Whether the run was stopped rather than left to drain.</param>
    /// <param name="indexedFiles">Files searchable now, for the summary's count.</param>
    public void Observe(bool hasPendingFiles, bool pausedAtCap, bool wasCancelled, int indexedFiles, bool scanCompleted = true)
    {
        if (hasPendingFiles && !wasCancelled)
        {
            _runOpen = true;
            return;
        }

        if (database.InitialIndexCompleted)
        {
            _runOpen = false;
            return;
        }

        if ((!_runOpen && indexedFiles == 0) || !scanCompleted || pausedAtCap || wasCancelled)
            return;

        database.InitialIndexCompleted = true;
        _runOpen = false;
        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.Completed, indexedFiles);
    }
}
