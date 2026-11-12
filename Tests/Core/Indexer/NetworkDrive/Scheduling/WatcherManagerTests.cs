using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Indexer.NetworkDrive.Scheduling;

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
