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
}
