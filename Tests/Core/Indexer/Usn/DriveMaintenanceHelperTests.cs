using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class DriveMaintenanceHelperTests
{
    [TestMethod]
    public void NormalizeDrive_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, DriveMaintenanceHelper.NormalizeDrive(""));
        Assert.AreEqual(string.Empty, DriveMaintenanceHelper.NormalizeDrive("   "));
    }

    [TestMethod]
    [DataRow("d", "D")]
    [DataRow("D:", "D")]
    [DataRow(@"D:\", "D")]
    [DataRow("  d  ", "D")]
    public void NormalizeDrive_VariousFormats_NormalizeToUppercaseLetter(string input, string expected) => Assert.AreEqual(expected, DriveMaintenanceHelper.NormalizeDrive(input));

    // "Z" is deliberately a drive letter that (almost certainly) isn't actually mounted on the test
    // machine -- GetCachePath (isPresent branch) requires a live volume identity query and would throw
    // for a genuinely-unmounted drive, but every case here passes isPresent: false, so that branch is
    // never reached (see UpdateStatus's own cachePath ternary).

    [TestMethod]
    public void UpdateStatus_NewNotPresentDriveWithCachedPath_UsesCachedPathInsteadOfEmpty()
    {
        var current = new Dictionary<string, UsnIndexer.DriveIndexStatus>(StringComparer.OrdinalIgnoreCase);
        var drivesToBuild = new List<string>();
        var cachedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Z"] = @"C:\cache\Z.idx" };

        var status = DriveMaintenanceHelper.UpdateStatus("Z", isPresent: false, isEnabled: false, "ignored", current, drivesToBuild, cachedPaths);

        Assert.AreEqual(@"C:\cache\Z.idx", status.CachePath);
        Assert.AreEqual("unavailable", status.State);
    }

    [TestMethod]
    public void UpdateStatus_NewNotPresentDriveWithNoCachedPath_LeavesCachePathEmpty()
    {
        var current = new Dictionary<string, UsnIndexer.DriveIndexStatus>(StringComparer.OrdinalIgnoreCase);
        var drivesToBuild = new List<string>();
        var cachedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var status = DriveMaintenanceHelper.UpdateStatus("Z", isPresent: false, isEnabled: false, "ignored", current, drivesToBuild, cachedPaths);

        Assert.AreEqual(string.Empty, status.CachePath);
    }

    [TestMethod]
    public void UpdateStatus_ExistingNotPresentDriveGainsACachedPathOnRefresh_BackfillsCachePath()
    {
        // Simulates a drive that was already tracked (e.g. discovered while not present, before its
        // cache file was found by an earlier ListCachedDrives pass) getting its CachePath backfilled the
        // next time a refresh's cachedPaths lookup does have an entry for it -- CachePath is no longer
        // stuck at whatever it was set to on first discovery.
        var existing = new UsnIndexer.DriveIndexStatus { Drive = "Z", CachePath = string.Empty, State = "unavailable" };
        var current = new Dictionary<string, UsnIndexer.DriveIndexStatus>(StringComparer.OrdinalIgnoreCase) { ["Z"] = existing };
        var drivesToBuild = new List<string>();
        var cachedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Z"] = @"C:\cache\Z.idx" };

        var status = DriveMaintenanceHelper.UpdateStatus("Z", isPresent: false, isEnabled: false, "ignored", current, drivesToBuild, cachedPaths);

        Assert.AreSame(existing, status);
        Assert.AreEqual(@"C:\cache\Z.idx", status.CachePath);
    }

    [TestMethod]
    [DataRow("ready", true, true, "ready")]
    [DataRow("ready", true, false, "disabled")]
    [DataRow("indexing", false, true, "unavailable")]
    public void ResolveExistingState_UsesPresenceAndExplicitSelection(
        string currentState,
        bool isPresent,
        bool isEnabled,
        string expected) =>
        Assert.AreEqual(expected, DriveMaintenanceHelper.ResolveExistingState(currentState, isPresent, isEnabled));

    [TestMethod]
    [DataRow(false, true, "disabled", true)]
    [DataRow(false, true, "ready", true)]
    [DataRow(false, true, "pending", false)]
    [DataRow(false, true, "indexing", false)]
    [DataRow(true, true, "ready", false)]
    [DataRow(false, false, "disabled", false)]
    public void ShouldRestoreAfterEnable_OnlyQueuesACompletedDisabledDrive(
        bool wasEnabled,
        bool isEnabled,
        string state,
        bool expected) =>
        Assert.AreEqual(expected, DriveMaintenanceHelper.ShouldRestoreAfterEnable(wasEnabled, isEnabled, state));

    // The retry half of the same decision: a drive that is present and enabled but still "pending" has a
    // build request that was refused (QueueDriveRebuild drops it while a full rebuild is running) and whose
    // disabled->enabled edge is already spent, so without a retry it never gets indexed again.
    [TestMethod]
    [DataRow("pending", true)]
    [DataRow("indexing", false)]
    [DataRow("ready", false)]
    [DataRow("failed", false)]
    public void IsPendingRestoreWithoutARebuild_OnlyRequeuesAPendingDrive(string state, bool expected) =>
        Assert.AreEqual(expected, DriveMaintenanceHelper.IsPendingRestoreWithoutARebuild(state));

    [TestMethod]
    public void UpdateStatus_MountedEnabledDriveStillPending_IsQueuedAgain()
    {
        // A real mounted letter: with isPresent the helper reads the drive's own cache path, which needs a
        // live volume identity.
        var drive = VolumeHelper.DetectIndexableLocalDrives().First();
        var existing = new UsnIndexer.DriveIndexStatus { Drive = drive, State = "pending", Enabled = true };
        var current = new Dictionary<string, UsnIndexer.DriveIndexStatus>(StringComparer.OrdinalIgnoreCase) { [drive] = existing };
        var drivesToBuild = new List<string>();

        DriveMaintenanceHelper.UpdateStatus(drive, isPresent: true, isEnabled: true, "ignored", current, drivesToBuild,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Queued, but through the restore path: forcing here re-ran a whole $MFT scan on every poll that
        // still saw "pending".
        CollectionAssert.AreEqual(new[] { drive }, drivesToBuild);
        Assert.AreEqual("pending", existing.State);
    }

    [TestMethod]
    public void UpdateStatus_DriveWhoseRebuildIsActuallyRunning_IsNotQueuedAgain()
    {
        var drive = VolumeHelper.DetectIndexableLocalDrives().First();
        var existing = new UsnIndexer.DriveIndexStatus { Drive = drive, State = "indexing", Enabled = true };
        var current = new Dictionary<string, UsnIndexer.DriveIndexStatus>(StringComparer.OrdinalIgnoreCase) { [drive] = existing };
        var drivesToBuild = new List<string>();

        DriveMaintenanceHelper.UpdateStatus(drive, isPresent: true, isEnabled: true, "ignored", current, drivesToBuild,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual("indexing", existing.State);
        Assert.IsEmpty(drivesToBuild);
    }

}
