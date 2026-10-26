using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class DriveWatcherHostTests
{
    [TestMethod]
    public async Task Start_OfflineRoot_ReturnsOnlineAndNotifiesOnlyAfterWatching()
    {
        using var dir = new TempDirectory();
        var online = 0;
        FileSystemWatcher? watcher = null;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new DriveWatcherHost("Test", dir.Path,
            _ => Volatile.Read(ref online) != 0,
            (current, _, _, _, _) => { watcher = current; return true; },
            _ => { }, TimeSpan.FromMilliseconds(20),
            onStarted: () => started.TrySetResult(watcher!.EnableRaisingEvents));

        host.Start();
        Assert.IsFalse(started.Task.IsCompleted);
        Volatile.Write(ref online, 1);

        Assert.IsTrue(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task Restart_QueuesReconciliationAfterNewWatcherIsEnabled()
    {
        using var dir = new TempDirectory();
        Action? restart = null;
        FileSystemWatcher? watcher = null;
        var starts = 0;
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new DriveWatcherHost("Test", dir.Path, _ => true,
            (current, _, restartWatcher, _, _) => { watcher = current; restart = restartWatcher; return true; },
            _ => { }, TimeSpan.FromMilliseconds(20), onStarted: () =>
            {
                if (Interlocked.Increment(ref starts) == 2) recovered.TrySetResult(watcher!.EnableRaisingEvents);
            });

        host.Start();
        Assert.IsNotNull(restart);
        restart();

        Assert.IsTrue(await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, starts);
    }

    [TestMethod]
    public void Start_RootDoesNotExist_NeverConfiguresOrLogs()
    {
        var configureCalled = false;
        var logs = new List<string>();
        using var host = new DriveWatcherHost("Test", @"C:\lertaro-does-not-exist",
            _ => false,
            (_, _, _, _, _) => { configureCalled = true; return true; },
            logs.Add);

        host.Start();

        Assert.IsFalse(configureCalled);
        Assert.IsEmpty(logs);
    }

    [TestMethod]
    public void Start_ConfigureReturnsFalse_DisposesWatcherAndDoesNotLog()
    {
        using var dir = new TempDirectory();
        var logs = new List<string>();
        using var host = new DriveWatcherHost("Test", dir.Path,
            _ => true,
            (watcher, drive, _, _, _) => false,
            logs.Add);

        host.Start();

        Assert.IsEmpty(logs);
    }

    [TestMethod]
    public void Start_ConfigureReturnsTrue_LogsStartedMonitoringWithNameAndDrive()
    {
        using var dir = new TempDirectory();
        var logs = new List<string>();
        using var host = new DriveWatcherHost("MyHost", dir.Path,
            _ => true,
            (_, _, _, _, _) => true,
            logs.Add);

        host.Start();

        Assert.HasCount(1, logs);
        StringAssert.Contains(logs[0], "[MyHost]");
        StringAssert.Contains(logs[0], dir.Path);
    }

    [TestMethod]
    public void Start_ConfigureReturnsTrue_PassesRealWatcherRootedAtDrive()
    {
        using var dir = new TempDirectory();
        FileSystemWatcher? seen = null;
        string? seenDrive = null;
        using var host = new DriveWatcherHost("Test", dir.Path,
            _ => true,
            (watcher, drive, _, _, _) => { seen = watcher; seenDrive = drive; return true; },
            _ => { });

        host.Start();

        Assert.AreEqual(dir.Path, seenDrive);
        Assert.AreEqual(dir.Path + Path.DirectorySeparatorChar, seen!.Path);
    }

    [TestMethod]
    public void Start_CalledTwice_OnlyConfiguresOnce()
    {
        using var dir = new TempDirectory();
        var configureCount = 0;
        using var host = new DriveWatcherHost("Test", dir.Path,
            _ => true,
            (_, _, _, _, _) => { configureCount++; return true; },
            _ => { });

        host.Start();
        host.Start();

        Assert.AreEqual(1, configureCount);
    }

    [TestMethod]
    public async Task Release_GivesUpTheWatcherButKeepsPollingForTheRoot()
    {
        using var dir = new TempDirectory();
        FileSystemWatcher? first = null;
        var rearmed = new TaskCompletionSource<FileSystemWatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new DriveWatcherHost("Test", dir.Path, _ => true,
            (current, _, _, _, _) =>
            {
                if (first == null) first = current;
                else rearmed.TrySetResult(current);
                return true;
            },
            _ => { }, TimeSpan.FromMilliseconds(20));

        host.Start();
        host.Release();

        // A release that stopped the host from ever re-arming would leave the root unwatched for good, so
        // what an eject-then-reinsert needs is exactly this: a second watcher on the same root.
        var second = await rearmed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public async Task Release_AfterDispose_LeavesNothingReArmed()
    {
        using var dir = new TempDirectory();
        var configured = 0;
        var host = new DriveWatcherHost("Test", dir.Path, _ => true,
            (_, _, _, _, _) => { Interlocked.Increment(ref configured); return true; },
            _ => { }, TimeSpan.FromMilliseconds(20));

        host.Start();
        host.Dispose();
        host.Release();

        await Task.Delay(200);
        Assert.AreEqual(1, Volatile.Read(ref configured));
    }

    [TestMethod]
    public void Dispose_ThenStart_NeverTouchesExistsOrConfigure()
    {
        using var dir = new TempDirectory();
        var existsCalled = false;
        var configureCalled = false;
        var host = new DriveWatcherHost("Test", dir.Path,
            _ => { existsCalled = true; return true; },
            (_, _, _, _, _) => { configureCalled = true; return true; },
            _ => { });

        host.Dispose();
        host.Start();

        Assert.IsFalse(existsCalled);
        Assert.IsFalse(configureCalled);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
