using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchEngineInitializerTests
{
    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    public void CanUseCachedUsnCatchUp_RequiresCompleteJournalBackedCache(
        bool isComplete,
        bool supportsUsnJournal,
        bool expected) =>
        Assert.AreEqual(expected, SearchEngineInitializer.CanUseCachedUsnCatchUp(isComplete, supportsUsnJournal));

    // An in-flight state left behind by a failed initialization is a progress bar that never moves, so it
    // must settle on the failure value; a state that already describes a usable index must survive a fault
    // raised after it was reached (monitor startup, cache save, ...).
    [TestMethod]
    [DataRow("pending")]
    [DataRow("indexing")]
    [DataRow("loading-cache")]
    [DataRow("")]
    public void ResolveStateAfterFailure_SettlesInFlightStatesOnError(string stateAtFailure) =>
        Assert.AreEqual("error", SearchEngineInitializer.ResolveStateAfterFailure(stateAtFailure));

    [TestMethod]
    [DataRow("ready")]
    [DataRow("cached")]
    [DataRow("idle")]
    public void ResolveStateAfterFailure_KeepsAlreadyUsableState(string stateAtFailure) =>
        Assert.AreEqual(stateAtFailure, SearchEngineInitializer.ResolveStateAfterFailure(stateAtFailure));

    // SearchEngineDriveMaintenance refuses every rebuild request while the full rebuild runs (the engine's
    // _isRebuilding, cleared only by onComplete(false)), and leaves deferring to the initializer. A drive that
    // ends initialization with no monitor asks for its own recovery from inside Run, so that request has to
    // arrive after onComplete, not inside the window where it is dropped. The lifetime token is cancelled up
    // front, so the drive's build stops before it reads the volume and ends with no monitor.
    [TestMethod]
    public void Run_DriveEndsWithoutAMonitor_RequestsItsRecoveryAfterInitializationCompletes()
    {
        var drive = VolumeHelper.DetectIndexableLocalDrives().FirstOrDefault(VolumeHelper.SupportsUsnJournal);
        if (drive == null)
            Assert.Inconclusive("No mounted journal drive to run initialization against.");
        var cacheDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        try
        {
            using var indexer = new UsnIndexer();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var rebuilding = true;
            var accepted = new List<string>();
            // Stands in for SearchEngineDriveMaintenance.QueueDriveRebuild: refused while rebuilding.
            var initializer = new SearchEngineInitializer(indexer, cacheDir, d => { if (!rebuilding) accepted.Add(d); });
            var settings = new MachineSettings { LocalDrives = { VolumeHelper.GetVolumeId(drive)! } };

            initializer.Run(forceRebuild: true, cts, isRebuilding => rebuilding = isRebuilding, settings);

            Assert.IsFalse(rebuilding);
            Assert.Contains(drive, accepted);
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    private static string FreeDriveLetter() =>
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).First(l => !VolumeHelper.DetectIndexableLocalDrives().Contains(l));

    // Run with no drive selected and the lifetime already cancelled: initialization does nothing but complete
    // and flush its deferred requests.
    private static void RunWithNoDrives(SearchEngineInitializer initializer, Action<bool> onComplete)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        initializer.Run(forceRebuild: true, cts, onComplete, new MachineSettings());
    }

    // A removal monitor that StartMonitor starts during initialization recovers through
    // RequestRebuildAfterRemoval. A query-remove-failed before onComplete(false) used to reach the engine's
    // forced rebuild directly, while the engine still refused every request, and was lost. It now waits for
    // initialization and is flushed as a forced rebuild; once initialization is over, recoveries pass
    // straight through. The notifications are sent before Run starts, which is the same state: nothing is
    // flushed until Run's finally.
    //
    // StartMonitor itself cannot be driven here: a removal monitor registers only against a real volume
    // handle, and EnsureDriveStatuses' waiter fires only when its letter is really mounted. Their wiring is
    // guaranteed by construction instead. The initializer keeps the engine's callbacks in no field, so a
    // monitor or waiter can only be handed RequestRebuild or RequestRebuildAfterRemoval. Reverting either call
    // site to the raw callback does not compile.
    [TestMethod]
    public void Run_RemovalFailedDuringInitialization_ForcedRebuildRunsAfterIt()
    {
        var drive = FreeDriveLetter();
        var cacheDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        try
        {
            using var indexer = new UsnIndexer();
            using var lifetime = new CancellationTokenSource();
            var rebuilding = true;
            var requests = new List<string>();
            var initializer = new SearchEngineInitializer(indexer, cacheDir,
                onReindexRequired: d => requests.Add($"plain {d} rebuilding={rebuilding}"),
                onRemovalRequested: _ => { },
                onReindexAfterRemoval: d => requests.Add($"forced {d} rebuilding={rebuilding}"));
            var registration = new DriveMonitorRegistration(new NoopDisposable(), null);
            indexer.RegisterDriveMonitor(drive, registration);
            // What StartMonitor hands EnsureMonitor, which hands it on to AttachRemovalMonitor.
            using var removal = DriveMonitorFactory.CreateRemovalMonitor(indexer, drive, registration, lifetime.Token,
                initializer.RequestRebuild, _ => { }, initializer.RequestRebuildAfterRemoval);
            Assert.IsTrue(registration.ReplaceRemovalMonitor(removal));

            removal.HandleNotification(IntPtr.Zero, IntPtr.Zero, DeviceNotificationNative.DeviceQueryRemove, IntPtr.Zero, 0);
            removal.HandleNotification(IntPtr.Zero, IntPtr.Zero, DeviceNotificationNative.DeviceQueryRemoveFailed, IntPtr.Zero, 0);
            Assert.IsEmpty(requests, "the recovery reached the engine while it still refused every request");
            RunWithNoDrives(initializer, isRebuilding => rebuilding = isRebuilding);
            initializer.RequestRebuildAfterRemoval(drive);

            CollectionAssert.AreEqual(new[] { $"forced {drive} rebuilding=False", $"forced {drive} rebuilding=False" }, requests);
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    // A parked drive's re-insertion waiter asks for a plain rebuild, and a removal recovery asks for a forced
    // one. When both arrive during initialization, in either order, they flush as one forced rebuild, which
    // also answers the plain request.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Run_ReinsertionAndRemovalRecoveryDuringInitialization_FlushAsOneForcedRebuild(bool reinsertedFirst)
    {
        var drive = FreeDriveLetter();
        var cacheDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        try
        {
            using var indexer = new UsnIndexer();
            var requests = new List<string>();
            var initializer = new SearchEngineInitializer(indexer, cacheDir,
                onReindexRequired: d => requests.Add($"plain {d}"),
                onReindexAfterRemoval: d => requests.Add($"forced {d}"));

            if (reinsertedFirst)
                initializer.RequestRebuild(drive);
            initializer.RequestRebuildAfterRemoval(drive);
            if (!reinsertedFirst)
                initializer.RequestRebuild(drive);
            RunWithNoDrives(initializer, _ => { });

            CollectionAssert.AreEqual(new[] { $"forced {drive}" }, requests);
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
