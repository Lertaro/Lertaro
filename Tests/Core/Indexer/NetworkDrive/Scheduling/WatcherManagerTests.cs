using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Indexer.NetworkDrive.Scheduling;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Scheduling;

[TestClass]
public sealed class WatcherManagerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnsureWatcher_AlreadyOnline_OnlyRequestsScanWhenIndexIsMissing(bool cached)
    {
        var root = Directory.CreateTempSubdirectory("lertaro-watcher-tests-").FullName;
        using var index = cached ? new NetworkIndex(root) : null;
        var checkedIndex = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        try
        {
            using var manager = new WatcherManager(
                (drive, _) => { Interlocked.Increment(ref requests); refreshed.TrySetResult(drive); },
                _ => { checkedIndex.TrySetResult(); return index; }, (_, _, _) => { }, _ => false);

            manager.EnsureWatcher(root);
            await checkedIndex.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!cached)
                Assert.AreEqual(root, await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreEqual(cached ? 0 : 1, Volatile.Read(ref requests));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnsureWatcher_InitiallyOffline_QueuesRecoveryWithOrWithoutCache(bool cached)
    {
        var root = Directory.CreateTempSubdirectory("lertaro-watcher-tests-").FullName;
        using var index = cached ? new NetworkIndex(root) : null;
        var available = 0;
        var queued = 0;
        var probedOffline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var manager = new WatcherManager(
                (drive, _) => { Interlocked.Increment(ref queued); recovered.TrySetResult(drive); },
                _ => index, (_, _, _) => { }, _ => false,
                directoryExists: _ =>
                {
                    var ready = Volatile.Read(ref available) != 0;
                    if (!ready) probedOffline.TrySetResult();
                    return ready;
                },
                retryDelay: TimeSpan.FromMilliseconds(20));

            manager.EnsureWatcher(root);
            await probedOffline.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, Volatile.Read(ref queued));
            Volatile.Write(ref available, 1);

            Assert.AreEqual(root, await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            manager.EnsureWatcher(root);
            Assert.AreEqual(1, Volatile.Read(ref queued));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EnsureWatcher_FirstScanFinishesBeforeWatcherStarts_StillReconcilesGap()
    {
        var root = Directory.CreateTempSubdirectory("lertaro-watcher-tests-").FullName;
        using var index = new NetworkIndex(root);
        using var releaseProbe = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        NetworkIndex? current = null;
        try
        {
            using var manager = new WatcherManager(
                (drive, _) => refreshed.TrySetResult(drive),
                _ => Volatile.Read(ref current), (_, _, _) => { }, _ => false,
                directoryExists: _ =>
                {
                    entered.TrySetResult();
                    releaseProbe.Wait();
                    return true;
                });
            try
            {
                manager.EnsureWatcher(root, needsInitialReconciliation: true);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Volatile.Write(ref current, index);
                releaseProbe.Set();

                Assert.AreEqual(root, await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                releaseProbe.Set();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The debounced publish persists the index on a Timer, where an escaping exception ends the process (if
    // this regresses the test host dies). A failed persist has also dropped the unsaved changes from memory
    // (LiveIndex.Compact reopens the snapshot still on disk), so the share is owed a refresh.
    [TestMethod]
    public async Task PublishThatFailsToPersist_QueuesARefresh()
    {
        var (root, staging) = CreateRootOutsideAppData();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var index = NetworkIndex.FromSnapshotFile(root, WriteRootOnlySnapshot(root, staging));
            using var manager = new WatcherManager(
                (_, reason) => refreshed.TrySetResult(reason),
                _ => { started.TrySetResult(); return index; },
                (_, _, _) => throw new IOException("There is not enough space on the disk."), _ => false);

            manager.EnsureWatcher(root);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(root, "new.txt"), "x");

            Assert.AreEqual("incremental persist failed", await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    // A persist that fails because a refresh has already swapped this index out owes nothing: the refresh
    // that replaced it has walked the share. Only the index the drive is still served by is owed one.
    [TestMethod]
    public async Task PublishThatFailsToPersistAnIndexAlreadyReplaced_QueuesNoRefresh()
    {
        var (root, staging) = CreateRootOutsideAppData();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedAfterFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var replaced = 0;
        try
        {
            using var index = NetworkIndex.FromSnapshotFile(root, WriteRootOnlySnapshot(root, staging));
            using var replacement = new NetworkIndex(root);
            using var manager = new WatcherManager(
                (_, reason) => refreshes.Enqueue(reason),
                _ =>
                {
                    started.TrySetResult();
                    if (Volatile.Read(ref replaced) == 0)
                        return index;
                    checkedAfterFailure.TrySetResult();
                    return replacement;
                },
                (_, _, _) =>
                {
                    // A refresh finished while this publish waited, then the old index's persist fails.
                    Volatile.Write(ref replaced, 1);
                    throw new ObjectDisposedException(nameof(NetworkIndex));
                },
                _ => false);

            manager.EnsureWatcher(root);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(root, "new.txt"), "x");

            await checkedAfterFailure.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The refresh would be queued on the same thread right after that check.
            await Task.Delay(200);
            Assert.AreEqual(0, refreshes.Count, $"Refreshes queued: {string.Join(", ", refreshes)}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    // Outside %USERPROFILE%\AppData: the handler applies the user's real exclusion rules, and the stock rules
    // exclude AppData, where the temp directory lives. The staging directory holds what is moved in and the
    // snapshot.
    private static (string Root, string Staging) CreateRootOutsideAppData()
    {
        var root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "lertaro-watcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, Directory.CreateTempSubdirectory("lertaro-watcher-staging-").FullName);
    }

    private static string WriteRootOnlySnapshot(string root, string staging)
    {
        var idxPath = Path.Combine(staging, "test.idx");
        var store = new FileRecordStore
        {
            SourceKey = root,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);
        return idxPath;
    }

    [TestMethod]
    public async Task EnsureWatcher_BlockedProbe_DoesNotBlockCallerOrRemoval()
    {
        using var releaseProbe = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = 0;
        using var manager = new WatcherManager((_, _) => Interlocked.Increment(ref queued),
            _ => null, (_, _, _) => { }, _ => false,
            directoryExists: _ =>
            {
                entered.SetResult();
                releaseProbe.Wait();
                returned.SetResult();
                return true;
            });
        var ensure = Task.Run(() => manager.EnsureWatcher(@"C:\unused"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ensure.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => manager.RemoveWatcher(@"C:\unused")).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseProbe.Set();
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.AreEqual(0, Volatile.Read(ref queued));
    }
}
