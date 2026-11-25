using System.ComponentModel;

namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Enumerates TreeBuilder directories and contains failures to the affected directory. A faulted worker
// would otherwise end the whole drive scan through Task.WaitAll.
internal static class TreeBuilderEnumerationExtensions
{
    // Retry only transient native I/O failures when OPENING a directory. Missing paths and access
    // denials used to pay all three waits too, adding 4.25 seconds per permanent failure on large trees.
    // ponytail: transient failures still get a per-directory 4.25-second backoff budget (excluding
    // native call time); a walk-wide retry budget is the upgrade if sustained outages dominate scans.
    private const int EnumerateAttempts = 4;
    private static readonly int[] EnumerateBackoffMilliseconds = [250, 1000, 3000];

    public static bool TryEnumerateChildren(
        this TreeBuilder builder,
        string directoryPath,
        out IEnumerable<NativeFileEntry> children,
        out Exception? failure,
        Func<string, IEnumerable<NativeFileEntry>>? enumerate = null,
        Func<int, bool>? waitForCancellation = null)
    {
        enumerate ??= NativeFileEnumerator.Enumerate;
        failure = null;
        for (var attempt = 0; attempt < EnumerateAttempts; attempt++)
        {
            builder._token.ThrowIfCancellationRequested();
            try
            {
                children = enumerate(directoryPath);
                return true;
            }
            catch (Exception ex) when (IsEnumerationFailure(ex))
            {
                failure = ex;
                // The last attempt breaks instead of sleeping, and so does an already-cancelled walk:
                // there is nothing left to wait for either way.
                if (attempt == EnumerateAttempts - 1 || !IsTransientEnumerationFailure(ex))
                    break;
                var delay = EnumerateBackoffMilliseconds[attempt];
                if (waitForCancellation?.Invoke(delay) ?? builder._token.WaitHandle.WaitOne(delay))
                    builder._token.ThrowIfCancellationRequested();
            }
        }

        children = Array.Empty<NativeFileEntry>();
        return false;
    }

    internal static bool IsTransientEnumerationFailure(Exception ex) =>
        Win32Code(ex) is 32 or 33 // sharing/lock violation
            or 53 or 54 or 55 or 59 or 64 or 71 // network unavailable/busy/disconnected
            or 121 or 1231 or 1232 or 1236 or 1237 or 1460; // timeout/unreachable/aborted/retry

    // Whether CarryForwardPreviousSubtree keeps a failed folder's previous content: only when the failure
    // says the server, the connection or the device could not answer right now. Those pass, and the folder
    // is listed again by a later pass. A failure that describes the folder itself -- not there (2, 3), not a
    // folder (267), not readable by this user (5, 65), damaged (23, 1392) -- or a code nobody listed can last
    // indefinitely, and carrying on it would keep a phantom subtree for as long as it does, so it drops.
    // Beyond the retry list, by their documented meaning: 21 "The device is not ready", 51 "Windows cannot
    // find the network path. Verify that the network path is correct and the destination computer is not
    // busy or turned off", 70 "The remote server has been paused or is in the process of being started", 240
    // "The session was canceled", 1006 "The volume for a file has been externally altered so that the opened
    // file is no longer valid" (a handle an SMB reconnect invalidated), 1130 "Not enough server storage is
    // available to process this command", 1167 "The device is not connected", 1222 "The network is not
    // present or not started", 1225 "The remote computer refused the network connection", 1229 "An operation
    // was attempted on a nonexistent network connection", 1234 "No service is operating at the destination
    // network endpoint on the remote system", 1255 "The server machine is shutting down", 1256 "The remote
    // system is not available", 1450 "Insufficient system resources exist to complete the requested
    // service". The retry list's 32 and 33 (sharing and lock violations) stay too: a locked folder still
    // exists, so its last-known contents are a better answer than none. Retrying stays a separate, narrower
    // question (a cost one) with its own list above.
    internal static bool IsTemporarilyUnavailable(Exception ex) =>
        IsTransientEnumerationFailure(ex)
        || Win32Code(ex) is 21 or 51 or 70 or 240 or 1006 or 1130 or 1167 or 1222 or 1225 or 1229 or 1234
            or 1255 or 1256 or 1450;

    private static int Win32Code(Exception ex) => ex switch
    {
        Win32Exception native => native.NativeErrorCode,
        IOException io when (io.HResult & 0xFFFF0000) == 0x80070000 => io.HResult & 0xFFFF,
        _ => 0
    };

    // Shared failure tail for a directory whose enumeration failed -- whether it failed to open or failed
    // midway through being iterated (FindNextFile mid-listing, e.g. the share dropping). The directory is
    // left un-Listed: its contents were never fully captured, so a later diff-aware pass must re-list it
    // rather than trust the partial rows already recorded for it (TreeDiffBaseline only ever trusts
    // FileRecordFlags.Listed directories).
    public static void CountEnumerationFailure(this TreeBuilder builder, string directoryPath, Exception failure)
    {
        builder.CountError(ref builder._enumerateErrors);
        // Once per failed directory, never per entry: one dropped share fails thousands of directories in
        // a row, and a Warn line each is already a lot of log.
        Logger.Log($"[NetworkIndexer] Failed to enumerate {directoryPath}: {Describe(failure)}", LogLevel.Warn);
    }

    // Records, filters and commits one directory's already-enumerated children, then marks the directory
    // Listed. Takes the sequence instead of enumerating it itself so the mid-iteration failure path stays
    // exercisable without a real failing share on hand. Returns the enumeration failure (passed in, or hit
    // while streaming) so the caller can still check whether the root went with it; null when complete.
    public static Exception? ConsumeChildren(
        this TreeBuilder builder,
        WorkItem current,
        NetworkIgnoreRuleSet ignoreRules,
        IEnumerable<NativeFileEntry> children,
        Exception? enumerationFailure = null)
    {
        var batch = new List<FileRecord>(TreeBuilder.RecordBatchSize);
        try
        {
            foreach (var child in children)
            {
                builder._token.ThrowIfCancellationRequested();

                var childPath = current.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar + child.Name;
                var createResult = builder.TryCreateRecord(child, current.LogicalPath, current.LocalId, out var record, out var isDirectory, out var logicalFullPath);
                if (createResult != WalkRecordResult.Success)
                {
                    builder.CountCreateFailure(createResult);
                    continue;
                }

                if (!builder._filter.ShouldIndex(logicalFullPath, record.Name, isDirectory, record.Attributes, ignoreRules))
                {
                    Interlocked.Increment(ref builder._skippedItems);
                    continue;
                }

                batch.Add(record);
                if (batch.Count >= TreeBuilder.RecordBatchSize)
                    builder.FlushRecords(batch);

                var indexedItems = Interlocked.Increment(ref builder._indexedItems);
                if (isDirectory) Interlocked.Increment(ref builder._indexedDirs); else Interlocked.Increment(ref builder._indexedFiles);

                if (isDirectory && builder._filter.ShouldDescend(logicalFullPath, record.Attributes, current.Depth + 1, ignoreRules))
                {
                    // A directory just added to batch isn't in _indexById until its batch is flushed --
                    // another worker can dequeue and finish this child (including its own MarkListed)
                    // before that happens, silently leaving it un-Listed forever. Flush now so the child's
                    // own record is registered before anyone else can possibly touch it.
                    builder.FlushRecords(batch);
                    builder.EnqueueDirectory(childPath, logicalFullPath, record.Id, current.Depth + 1, ignoreRules, current.Ancestors,
                        (record.Attributes & FileAttributes.ReparsePoint) != 0);
                }

                builder.MaybeReportProgress();
                builder.MaybeCheckpoint(indexedItems);
            }
        }
        catch (Exception ex) when (IsEnumerationFailure(ex))
        {
            enumerationFailure = ex;
        }

        builder.FlushRecords(batch);
        if (enumerationFailure != null)
        {
            builder.CountEnumerationFailure(current.Path, enumerationFailure);
            return enumerationFailure;
        }

        builder.MarkListed(current.LocalId);
        return null;
    }

    // The exceptions a directory enumeration really raises: the native enumerator's own failures
    // (FindFirstFileEx/FindNextFile, both surfaced as Win32Exception by NativeFileEnumerator) plus the
    // file system ones a listing can hit on the way. Deliberately narrow: anything else is a bug in this
    // walker, and hiding it behind "un-captured directory" would leave the directory permanently un-Listed
    // instead of surfacing the bug. Cancellation is not in this set either -- it must keep propagating.
    internal static bool IsEnumerationFailure(Exception ex) =>
        ex is Win32Exception or IOException or UnauthorizedAccessException;

    // Exception type plus the codes a log reader needs to tell "share dropped" from "access denied": the
    // HRESULT always, plus Win32Exception's own native error code when it carries one.
    private static string Describe(Exception ex)
    {
        var win32 = ex is Win32Exception native ? native.NativeErrorCode : ex.HResult;
        return $"{ex.GetType().Name} (HRESULT=0x{ex.HResult:X8}, Win32={win32}): {ex.Message}";
    }
}
