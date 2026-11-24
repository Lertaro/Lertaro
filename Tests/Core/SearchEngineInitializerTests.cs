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
}
