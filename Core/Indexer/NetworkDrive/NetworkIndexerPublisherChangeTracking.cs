namespace Lertaro.Core.Indexer.NetworkDrive;

// Telling subscribers where a network, WSL or folder index just changed and handling watcher-driven
// incremental updates. Split from NetworkIndexerPublisher.cs to keep that file under the project's line limit.
internal sealed partial class NetworkIndexerPublisher
{
    // Mirrors UsnIndexerExtensions.MarkMissedIfRebuilding for local drives: decides -- synchronously, at
    // the moment a watcher-detected change is actually applied to the in-memory index, not later when
    // PublishIncrementalUpdate's own debounced timer happens to fire -- whether this drive is currently
    // being rescanned, and flags the change as missed if so.
    public bool MarkMissedIfRescanning(string drive)
    {
        lock (_gate)
        {
            var isTracked = _statuses.TryGetValue(drive, out var status);
            return isTracked && status!.State == "indexing";
        }
    }

    public void PublishIncrementalUpdate(string drive, NetworkIndex index, IReadOnlyCollection<string>? changedDirectories = null)
    {
        bool skip;
        lock (_gate)
        {
            var isTracked = _statuses.TryGetValue(drive, out var stateCheck);
            skip = !isTracked || stateCheck!.State == "indexing";
        }
        if (skip)
            return;

        IndexerHelper.Save(index);
        lock (_gate)
        {
            if (!_statuses.TryGetValue(drive, out var current) || current.State == "indexing")
                return;
            _statuses[drive] = (NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, current));
            RaiseDirectoriesChanged(drive, changedDirectories);
        }
        PublishStatusesChanged();
    }

    /// <summary>
    /// Says this drive's index took content in, and where. <paramref name="changedDirectories"/> null
    /// means a whole tree was replaced and anything under it may have moved.
    /// </summary>
    private void RaiseDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        try
        {
            _raiseDirectoriesChanged(drive, changedDirectories);
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] A directory-change subscriber threw: {ex.Message}", LogLevel.Error);
        }
    }
}
