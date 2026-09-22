namespace Lertaro.Core;

/// <summary>
/// A cancellation slot per directory filter, plus one for unscoped searches (the empty filter), so a
/// new search supersedes only an older search for the SAME filter.
/// </summary>
/// <remarks>
/// A single slot for every directory-filtered search meant the fan-out of ONE keystroke cancelled its
/// own siblings: FileFilters publishes a keyword as a set of folders, the App issues one SearchDir
/// request per folder concurrently (ScopedSearchRunner), and whichever request reached the engine last
/// cancelled the others -- so all but that one folder silently came back with zero results, roughly
/// half of them per keystroke. Superseding per filter keeps the original intent (a newer search of the
/// same folder wins) without the fan-out cancelling itself.
/// </remarks>
internal sealed class SearchCancellationRegistry
{
    private readonly Dictionary<string, CancellationTokenSource> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Cancels whatever search is still running for this filter and returns the source the new one owns.
    /// A cancelled source is deliberately not disposed: the search it cancelled may still be unwinding
    /// and registering on its token, which a disposed source would turn into an ObjectDisposedException
    /// (it holds no unmanaged resources, so skipping Dispose is safe -- same stance as the engine's own
    /// shutdown and UsnServicePipeServer.Stop).
    /// </summary>
    public CancellationTokenSource Begin(string? directoryFilter)
    {
        var key = directoryFilter ?? string.Empty;
        var current = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_lock)
        {
            _slots.TryGetValue(key, out previous);
            _slots[key] = current;
        }

        CancelSuperseded(previous);
        return current;
    }

    /// <summary>
    /// Cancels a superseded source outside <c>_lock</c> and never lets it throw. Cancel() runs every
    /// registered continuation synchronously on this thread, and these tokens reach the whole search
    /// pipeline -- including <see cref="End"/>, which the search's own finally calls from another thread,
    /// so a callback that blocks would deadlock the search path if the lock were held. A throwing
    /// callback would otherwise abort <see cref="Begin"/> before the new source was registered, leaving
    /// the new search uncancelable by the next keystroke.
    /// </summary>
    private static void CancelSuperseded(CancellationTokenSource? previous)
    {
        if (previous is null)
            return;

        try
        {
            previous.Cancel();
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchCancellation] A superseded search's cancellation callback threw: {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Gives the slot back once that search is done. Without this, a search that already finished could
    /// still be "superseded" by a late request for the same folder, which would then cancel the newer
    /// search actually running on that folder -- and the slot map would grow with every folder ever
    /// searched instead of staying bounded by the searches currently in flight.
    /// </summary>
    public void End(string? directoryFilter, CancellationTokenSource current)
    {
        lock (_lock)
        {
            var key = directoryFilter ?? string.Empty;
            if (_slots.TryGetValue(key, out var live) && ReferenceEquals(live, current))
                _slots.Remove(key);
        }
    }

    public void CancelAll()
    {
        CancellationTokenSource[] superseded;
        lock (_lock)
        {
            superseded = [.. _slots.Values];
            _slots.Clear();
        }

        foreach (var source in superseded)
            CancelSuperseded(source);
    }
}
