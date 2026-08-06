using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.Indexer.Usn;

// TryOpenV2/LoadDrivesFromCache/TryLoadDriveFromCache/DropDriveFromRuntime all need a real V2 cache file on
// disk (SnapshotWriter.Write + Snapshot.Open), so they aren't covered here -- see LiveIndexFixture-based
// tests elsewhere for that. IsDriveIndexComplete is pure dictionary bookkeeping and gets direct coverage.
[TestClass]
public sealed class UsnIndexerCacheExtensionsTests
{
    [TestMethod]
    public void IsDriveIndexComplete_NoMetadataLoadedForDrive_ReturnsFalse() =>
        Assert.IsFalse(new UsnIndexer().IsDriveIndexComplete("C"));

    [TestMethod]
    public void IsDriveIndexComplete_MetadataMarkedComplete_ReturnsTrue()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = true };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
    }

    [TestMethod]
    public void IsDriveIndexComplete_MetadataMarkedIncomplete_ReturnsFalse()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false, FileSystemType = "ReFS" };

        Assert.IsFalse(indexer.IsDriveIndexComplete("C"));
    }

    [TestMethod]
    public void IsDriveIndexComplete_OnlyChecksTheNamedDrive()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = true };
        indexer._driveMetadata["D"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false, FileSystemType = "ReFS" };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
        Assert.IsFalse(indexer.IsDriveIndexComplete("D"));
    }

    // Regression coverage: true NTFS ($MFT, via MftIndexScanner) never produces partial/checkpoint output
    // -- it's always either a fully-finished result or nothing -- so its cache must be treated as always
    // complete regardless of what IsComplete says. This is also what protects an EXISTING NTFS cache
    // written before this field existed (IsComplete defaults to false on anything that never set it) from
    // triggering a needless full $MFT re-scan the first time this check ships.
    [TestMethod]
    public void IsDriveIndexComplete_NtfsMetadataMarkedIncomplete_StillReturnsTrue()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false, FileSystemType = "NTFS" };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
    }

    [TestMethod]
    public void IsDriveIndexComplete_NtfsFileSystemTypeIsCaseInsensitive()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false, FileSystemType = "ntfs" };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
    }
}
