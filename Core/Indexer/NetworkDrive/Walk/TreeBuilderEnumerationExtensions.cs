using System.ComponentModel;

namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Directory enumeration for TreeBuilder, as extension methods (matching this folder's other
// TreeBuilder*Extensions files) instead of a partial class, to keep TreeBuilder.cs under the repo's
// per-file line limit. Every failure here is contained to ONE directory: no work item may take a worker
// down with it, because TreeBuilder.Run's Task.WaitAll ends that drive's whole scan on a faulted worker.
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

    private static bool IsTransientEnumerationFailure(Exception ex)
    {
        var code = ex switch
        {
            Win32Exception native => native.NativeErrorCode,
            IOException io when (io.HResult & 0xFFFF0000) == 0x80070000 => io.HResult & 0xFFFF,
            _ => 0
        };
        return code is 32 or 33 // sharing/lock violation
            or 53 or 54 or 55 or 59 or 64 or 71 // network unavailable/busy/disconnected
            or 121 or 1231 or 1232 or 1236 or 1237 or 1460; // timeout/unreachable/aborted/retry
    }

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
    // exercisable without a real failing share on hand.
    public static void ConsumeChildren(
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

                if (Interlocked.Increment(ref builder._countSinceProgress) >= TreeBuilder.ProgressBatchSize)
                {
                    Interlocked.Exchange(ref builder._countSinceProgress, 0);
                    builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
                }

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
            return;
        }

        builder.MarkListed(current.LocalId);
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
