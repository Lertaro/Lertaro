using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.Usn.Journal;

// FILETIME-format timestamps (100ns since 1601-01-01 UTC), read straight out of the
// FILE_ID_EXTD_DIR_INFO buffer GetFileInformationByHandleEx already returns per entry. Listed mirrors
// FileRecordFlags.Listed -- set once this item's OWN children (if it's a directory) have been gathered,
// whether by a live GetFileInformationByHandleEx pass or by reusing a previous scan's cached children.
internal readonly record struct ReFsItem(string Name, UInt128 ParentFrn, bool IsDir, long Size, long CreationTimeUtc, long LastWriteTimeUtc, long LastAccessTimeUtc, bool Listed = false);

public static class ReFsScanner
{
    internal static UsnDriveIndexResult? ScanDrive(
        string drive,
        SafeFileHandle volumeHandle,
        UInt128 rootFrn,
        ulong journalId,
        long nextUsn,
        FileRecordStore? previousStore = null,
        Action<int, int>? onProgress = null,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null,
        CancellationToken token = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Log($"[ReFsScanner] Starting ReFS initial scan for drive {drive}...");

        // ReFsScanner otherwise never builds path strings (it walks purely by file ID), but the root
        // always has a real path ("Z:\") to stat directly.
        var rootLastWriteTime = FileTimeHelper.TryGetLastWriteTimeUnixSeconds($"{drive}:\\");

        var diffBaseline = TreeDiffBaseline.From(previousStore);
        var checkpointState = new ReFsCheckpointState(drive, rootFrn, nextUsn, journalId, onCheckpoint);

        // Slow path: parallel BFS via OpenFileById + GetFileInformationByHandleEx.
        // ponytail: O(N) I/O-bound scan; upgrade path = a documented ReFS full-enum API.
        Logger.Log($"[ReFsScanner] Drive {drive}: using ReFS directory-id BFS.");
        var items = ScanParallel(volumeHandle, rootFrn, rootLastWriteTime, diffBaseline, checkpointState, onProgress, token, out var errors);
        if (items == null)
            return null;

        stopwatch.Stop();
        var rate = stopwatch.Elapsed.TotalSeconds > 0 ? items.Count / stopwatch.Elapsed.TotalSeconds : items.Count;
        Logger.Log($"[ReFsScanner] Drive {drive}: directory-id BFS complete ({items.Count} items, {stopwatch.Elapsed.TotalSeconds:F2}s, {rate:F0} items/s).");
        var store = IndexCacheManager.CreateStoreFromDriveData(drive, rootFrn, items, nextUsn, journalId);
        // Unlike a checkpoint's own store (built the same way, via ReFsScannerCheckpointExtensions --
        // deliberately left false there), this is the walk's actual final result: only reached once
        // ScanParallel's Task.WaitAll returns without throwing, i.e. every enqueued directory finished.
        // Marked complete regardless of `errors` -- same reasoning as NetworkIndex/LocalDriveWalkBuilder
        // (see DriveRefreshRunner.RefreshDrive's own comment on why gating this on zero errors would make
        // it functionally never become true); a directory that failed to open just stays un-Listed for a
        // future rebuild to retry, same as those two.
        store.IsComplete = true;
        if (errors > 0)
            Logger.Log($"[ReFsScanner] {drive}: finished with {errors} directory open error(s) -- marking complete anyway; affected directories stay un-Listed for a future manual rebuild to retry.", LogLevel.Warn);
        return new UsnDriveIndexResult
        {
            Store = store,
            NextUsn = nextUsn,
            JournalId = journalId,
            IsSortedById = false
        };
    }

    // Slow path: parallel BFS using Channel<UInt128> as the work queue.
    // Workers await new items (no spin); termination via channel.Writer.TryComplete() when inFlight hits 0.
    private static Dictionary<UInt128, ReFsItem>? ScanParallel(
        SafeFileHandle volumeHandle, UInt128 rootFrn, uint rootLastWriteTime, TreeDiffBaseline? diffBaseline,
        ReFsCheckpointState checkpointState, Action<int, int>? onProgress, CancellationToken token, out int errors)
    {
        var items = new ConcurrentDictionary<UInt128, ReFsItem>(8, 32768);
        var channel = Channel.CreateUnbounded<UInt128>(new UnboundedChannelOptions { SingleReader = false });
        var files = 0;
        var dirs = 0;
        var errorCount = 0;
        var inFlight = 1;

        // The root has no ReFsItem of its own (only its children do, added by whichever ProcessDir call
        // discovers them) -- so its own reuse check happens here, once, up front, using the path-stat mtime
        // above instead of a value read off some parent's directory listing the way every other directory's
        // check works (see ProcessDir).
        if (diffBaseline != null && diffBaseline.TryGetUnchangedChildren(rootFrn, rootLastWriteTime, out var rootCachedChildren))
        {
            CopyReusedChildren(rootCachedChildren, rootFrn, items, ref files, ref dirs, subId =>
            {
                Interlocked.Increment(ref inFlight);
                channel.Writer.TryWrite(subId);
            }, checkpointState);
            if (Interlocked.Decrement(ref inFlight) == 0)
                channel.Writer.TryComplete();
        }
        else
        {
            channel.Writer.TryWrite(rootFrn);
        }

        try
        {
            var workerCount = Math.Min(8, Environment.ProcessorCount);
            var tasks = Enumerable.Range(0, workerCount).Select(_ => Task.Run(async () =>
            {
                await foreach (var dirId in channel.Reader.ReadAllAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        ProcessDir(volumeHandle, dirId, items, diffBaseline, checkpointState, onProgress, ref files, ref dirs, ref errorCount, subId =>
                        {
                            Interlocked.Increment(ref inFlight);
                            channel.Writer.TryWrite(subId);
                        });
                    }
                    finally
                    {
                        // Only one thread sees 0; it completes the channel, ending all ReadAllAsync
                        // loops. Must run in a finally: a non-cancellation exception out of
                        // ProcessDir (checkpoint write I/O, a throwing progress callback) would
                        // otherwise leave inFlight stuck above zero, the channel permanently open,
                        // and every worker blocked in ReadAllAsync forever -- hanging the whole
                        // scan past Task.WaitAll instead of failing it.
                        if (Interlocked.Decrement(ref inFlight) == 0)
                            channel.Writer.TryComplete();
                    }
                }
            }, token)).ToArray();

            Task.WaitAll(tasks, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(token);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ReFsScanner] Parallel BFS error: {ex.Message}", LogLevel.Error);
            errors = Volatile.Read(ref errorCount);
            return null;
        }

        errors = Volatile.Read(ref errorCount);
        return new Dictionary<UInt128, ReFsItem>(items);
    }

    // Open one directory by file ID and enumerate its direct children -- unless a diff baseline confirms
    // this directory's own mtime (already known from whichever parent's listing discovered it, in `items`)
    // hasn't changed since the previous scan, in which case its cached children are copied instead of
    // making any I/O call for it at all. Calls onSubdir for each subdirectory found either way (caller
    // handles inFlight accounting) -- a reused child directory still gets enqueued for its OWN reuse check,
    // since a directory's own mtime never reflects grandchild changes (see TreeDiffBaseline's own comment).
    private static void ProcessDir(
        SafeFileHandle volumeHandle,
        UInt128 dirId,
        ConcurrentDictionary<UInt128, ReFsItem> items,
        TreeDiffBaseline? diffBaseline,
        ReFsCheckpointState checkpointState,
        Action<int, int>? onProgress,
        ref int files,
        ref int dirs,
        ref int errors,
        Action<UInt128> onSubdir)
    {
        if (diffBaseline != null && items.TryGetValue(dirId, out var self)
            && diffBaseline.TryGetUnchangedChildren(dirId, FileTimeHelper.FileTimeToUnixSeconds(self.LastWriteTimeUtc), out var cachedChildren))
        {
            CopyReusedChildren(cachedChildren, dirId, items, ref files, ref dirs, onSubdir, checkpointState);
            items.TryUpdate(dirId, self with { Listed = true }, self);
            onProgress?.Invoke(Volatile.Read(ref files), Volatile.Read(ref dirs));
            return;
        }

        var desc = new Win32Api.FILE_ID_DESCRIPTOR
        {
            dwSize = 24,
            Type = 2, // ExtendedFileIdType
            ExtendedFileId = new Win32Api.FILE_ID_128 { Low = (ulong)dirId, High = (ulong)(dirId >> 64) }
        };
        using var dirHandle = Win32Api.OpenFileById(volumeHandle, ref desc,
            1, Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | 4,
            IntPtr.Zero, Win32Api.FILE_FLAG_BACKUP_SEMANTICS);
        if (dirHandle.IsInvalid)
        {
            // Same "count it, leave the directory un-Listed, move on" shape as
            // TreeBuilder.WalkDirectory's own CountError(ref _enumerateErrors) -- this directory (deleted
            // out from under the scan, permission-denied, etc.) never reaches the Listed marking below, so
            // a future rebuild retries it; this counter only feeds the completion-time diagnostic log.
            Interlocked.Increment(ref errors);
            return;
        }

        const int bufSize = 1024 * 1024;
        var buf = Marshal.AllocHGlobal(bufSize);
        var enumerationFailed = false;
        try
        {
            // Loop until GetFileInformationByHandleEx returns false. A false return is only a normal
            // end-of-directory when the last Win32 error is ERROR_NO_MORE_FILES -- any other code (access
            // denied, directory deleted mid-scan, etc.) is a real failure partway through enumeration, so
            // the entries already added to `items` below may be an incomplete listing of this directory.
            // The original code only called it once, missing entries in large directories.
            while (Win32Api.GetFileInformationByHandleEx(dirHandle, Win32Api.FileIdExtdDirectoryInfo, buf, bufSize))
            {
                var cur = buf;
                while (true)
                {
                    var nextOff = (uint)Marshal.ReadInt32(cur, 0);
                    // FILE_ID_EXTD_DIR_INFO: already-fetched fields, no extra I/O to read them.
                    var creationTimeUtc = Marshal.ReadInt64(cur, 8);
                    var lastAccessTimeUtc = Marshal.ReadInt64(cur, 16);
                    var lastWriteTimeUtc = Marshal.ReadInt64(cur, 24);
                    var size = Marshal.ReadInt64(cur, 40);
                    var attrs = (uint)Marshal.ReadInt32(cur, 56);
                    var nameLen = (uint)Marshal.ReadInt32(cur, 60);
                    var idLow = (ulong)Marshal.ReadInt64(cur, 72);
                    var idHigh = (ulong)Marshal.ReadInt64(cur, 80);
                    var fileId = new UInt128(idHigh, idLow);
                    var name = Marshal.PtrToStringUni(cur + 88, (int)nameLen / 2);
                    if (name != "." && name != "..")
                    {
                        var isDir = (attrs & 0x10) != 0;
                        var item = new ReFsItem(name!, dirId, isDir, isDir ? 0 : size, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc);
                        if (items.TryAdd(fileId, item))
                        {
                            if (isDir)
                            {
                                Interlocked.Increment(ref dirs);
                                onSubdir(fileId);
                            }
                            else
                            {
                                Interlocked.Increment(ref files);
                            }

                            if ((items.Count & 4095) == 0)
                                onProgress?.Invoke(Volatile.Read(ref files), Volatile.Read(ref dirs));

                            checkpointState.MaybeCheckpoint(items);
                        }
                    }
                    if (nextOff == 0) break;
                    cur += (int)nextOff;
                }
            }

            // Captured immediately after the loop exits, before FreeHGlobal or anything else can touch
            // the thread's last-error slot -- ERROR_NO_MORE_FILES is the only "this was a normal end of
            // directory" code; anything else means the entries already added above are a partial listing.
            if (Marshal.GetLastWin32Error() != Win32Api.ERROR_NO_MORE_FILES)
            {
                enumerationFailed = true;
                Interlocked.Increment(ref errors);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }

        // dirId's own entry (if it has one -- the root doesn't) was added by whichever parent discovered
        // it, with Listed defaulted to false; now that its own children are fully gathered, mark it the
        // same way a reused directory is marked above, so a LATER scan's diff baseline can trust IT too.
        // Skipped on a real enumeration failure (see above) -- this directory's own listing may be
        // incomplete, so it must stay un-Listed for a future rebuild to retry, same as the
        // dirHandle.IsInvalid case above.
        if (!enumerationFailed && items.TryGetValue(dirId, out var listedSelf))
            items.TryUpdate(dirId, listedSelf with { Listed = true }, listedSelf);
    }

    // Copies a reused directory's cached children into `items`, recursing into cached subdirectories for
    // their OWN reuse check via onSubdir (never trusting a subtree more than one level deep at a time).
    // Silently skips a cached child whose id already exists in `items` -- a corrupted/duplicated baseline
    // (e.g. a duplicate row left by some earlier bug) -- rather than aborting the whole reuse: this
    // directory's other, non-colliding cached children are still perfectly valid to reuse, and skipping
    // (not re-adding) a duplicate is always safe, the same conservative choice TreeBuilder's own
    // EnqueueDirectory makes for the equivalent case.
    internal static void CopyReusedChildren(
        IEnumerable<FileRecord> cachedChildren,
        UInt128 directoryId,
        ConcurrentDictionary<UInt128, ReFsItem> items,
        ref int files,
        ref int dirs,
        Action<UInt128> onSubdir,
        ReFsCheckpointState? checkpointState = null)
    {
        foreach (var child in cachedChildren)
        {
            var item = new ReFsItem(
                child.Name,
                directoryId,
                child.IsDirectory,
                child.Size,
                ToFileTimeUtcOrZero(child.CreationTimeUnixSeconds),
                ToFileTimeUtcOrZero(child.LastWriteTimeUnixSeconds),
                ToFileTimeUtcOrZero(child.LastAccessTimeUnixSeconds));
            if (!items.TryAdd(child.Id, item))
                continue;

            if (child.IsDirectory)
            {
                Interlocked.Increment(ref dirs);
                onSubdir(child.Id);
            }
            else
            {
                Interlocked.Increment(ref files);
            }

            checkpointState.MaybeCheckpoint(items);
        }
    }

    // FileTimeHelper.FromUnixSeconds(0) returns DateTime.MinValue (its own "not recorded" convention),
    // and DateTime.MinValue.ToFileTimeUtc() throws (FILETIME's epoch is 1601, DateTime's is year 1) --
    // mirrors FileTimeHelper.FileTimeToUnixSeconds' own "<= 0 means not recorded" guard, just inverted.
    internal static long ToFileTimeUtcOrZero(uint unixSeconds) =>
        unixSeconds == 0 ? 0 : FileTimeHelper.FromUnixSeconds(unixSeconds).ToFileTimeUtc();
}
