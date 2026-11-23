namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// The files whose extraction is running right now, mirrored to a sidecar file beside the index.
/// Extraction runs inside the App process (see ExtractionLimits), so a parser that takes the process
/// down -- a stack overflow or native fault on a corrupt PDF -- leaves no failed row behind, and the
/// next start re-queues the same file and crashes again. Whatever a previous process left listed is
/// recorded as failed instead (IndexBatchProcessor.RecordInterruptedExtractions).
/// </summary>
/// <remarks>
/// ponytail: every file the crash interrupted is marked, so up to (lanes - 1) innocent files extracting
/// alongside the culprit wait until they next change. Upgrade path: re-run leftovers one at a time and
/// mark only the file that is in flight when the process dies again.
/// </remarks>
internal sealed class ExtractionJournal
{
    private readonly string _path;
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);

    public ExtractionJournal(string path)
    {
        _path = path;
        // A normal exit (not a crash: the runtime skips ProcessExit for those) simply abandons whatever
        // is still extracting -- nothing about those files is suspect.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (_active)
            {
                _active.Clear();
                Save();
            }
        };
    }

    public void Begin(string file)
    {
        lock (_active)
        {
            // A timed-out parser can still run when a later batch starts the same file again.
            _active.TryGetValue(file, out var count);
            _active[file] = count + 1;
            if (count == 0)
                Save();
        }
    }

    public void End(string file)
    {
        lock (_active)
        {
            if (!_active.TryGetValue(file, out var count))
                return;
            if (count > 1)
                _active[file] = count - 1;
            else if (_active.Remove(file))
                Save();
        }
    }

    /// <summary>
    /// Paths a previous process left listed: it ended mid-extraction without exiting normally. Must run
    /// before this process extracts anything, since the first Begin overwrites the sidecar.
    /// </summary>
    public string[] TakeInterrupted()
    {
        lock (_active)
        {
            try
            {
                if (!File.Exists(_path))
                    return [];

                var interrupted = File.ReadAllLines(_path).Where(p => p.Length > 0).ToArray();
                Save();
                return interrupted;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    // Best effort: only crash recovery reads this file, and failing to write it must never stop indexing.
    // Written beside and swapped in: WriteAllLines truncates first, and with several lanes rewriting the list
    // many times a second, a crash in that window left an empty sidecar and the culprit was re-extracted.
    private void Save()
    {
        try
        {
            if (_active.Count == 0)
            {
                File.Delete(_path);
            }
            else
            {
                var temp = _path + ".tmp";
                File.WriteAllLines(temp, _active.Keys);
                File.Move(temp, _path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
