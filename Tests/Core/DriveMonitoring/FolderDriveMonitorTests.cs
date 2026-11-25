using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

// The watcher itself is never started here: Schedule is what its event handlers call, and the debounce
// Timer it arms is the real one.
[TestClass]
public sealed class FolderDriveMonitorTests
{
    // An exception escaping a Timer callback terminates the process, so a failing change must turn into a
    // re-walk of the drive instead. If this regresses, the test host dies rather than the test failing.
    [TestMethod]
    public void Schedule_ChangeHandlerThrows_RequestsAReindex()
    {
        using var reindexRequested = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (_, _, _) => throw new IOException("The device is not ready."),
            CancellationToken.None,
            reindexRequested.Set);

        monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "new-folder"));

        Assert.IsTrue(reindexRequested.Wait(TimeSpan.FromSeconds(10)));
    }
}
