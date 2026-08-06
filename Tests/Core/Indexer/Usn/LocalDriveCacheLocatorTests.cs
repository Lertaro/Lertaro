using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class LocalDriveCacheLocatorTests
{
    [TestMethod]
    public void ListCachedDrives_CacheDirDoesNotExist_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var missing = Path.Combine(dir.Path, "does-not-exist");

        var result = LocalDriveCacheLocator.ListCachedDrives(missing);

        Assert.IsEmpty(result);
    }

    // Regression coverage: a not-present (unplugged) drive's cache path can't be re-derived from a live
    // volume identity query (see DriveMaintenanceHelper.UpdateStatus's own comment), so the Settings UI
    // relies on this actual on-disk path -- not just the drive letter -- to offer deleting a leftover
    // cache file for a drive that isn't currently mounted.
    [TestMethod]
    public void ListCachedDrives_LocalMftSnapshot_ReturnsDriveAndItsActualPath()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "somekey.idx");
        SnapshotWriter.Write(BuildStore("D", FileRecordSourceKind.LocalMft), path);

        var result = LocalDriveCacheLocator.ListCachedDrives(dir.Path);

        Assert.HasCount(1, result);
        Assert.AreEqual("D", result[0].Drive);
        Assert.AreEqual(path, result[0].Path);
    }

    [TestMethod]
    public void ListCachedDrives_NonLocalMftSnapshot_IsExcluded()
    {
        using var dir = new TempDirectory();
        SnapshotWriter.Write(BuildStore("Z", FileRecordSourceKind.NetworkMappedDrive), Path.Combine(dir.Path, "network.idx"));

        var result = LocalDriveCacheLocator.ListCachedDrives(dir.Path);

        Assert.IsEmpty(result);
    }

    private static FileRecordStore BuildStore(string sourceKey, FileRecordSourceKind sourceKind)
    {
        var store = new FileRecordStore
        {
            SourceKey = sourceKey,
            SourceKind = sourceKind,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        return store;
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
