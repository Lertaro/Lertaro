using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class DriveWatcherHostTests
{
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
