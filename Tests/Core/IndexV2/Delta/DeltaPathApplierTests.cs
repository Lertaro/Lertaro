using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.IndexV2.Delta;

[TestClass]
public sealed class DeltaPathApplierTests
{
    [TestMethod]
    public void ApplyCreatedOrChanged_ValidDirectory_UpsertsPathAndChildren()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);

        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);

        var subDir = Path.Combine(tempDir.Path, "subfolder");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "child.txt");
        File.WriteAllText(subFile, "test");

        var changed = index.ApplyCreatedOrChanged(@"\\server\share", subDir);
        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void ApplyDeleted_ExistingPath_RemovesFromIndex()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);

        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);

        var subDir = Path.Combine(tempDir.Path, "subfolder");
        Directory.CreateDirectory(subDir);

        index.ApplyCreatedOrChanged(@"\\server\share", subDir);
        var removed = index.ApplyDeleted(subDir);

        Assert.IsTrue(removed);
    }

    [TestMethod]
    public void ApplyCreatedOrChanged_ExistingFile_RefreshesAttributes()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");
        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);
        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);
        var filePath = Path.Combine(tempDir.Path, "hidden.txt");
        File.WriteAllText(filePath, "test");
        index.ApplyCreatedOrChanged(@"\\server\share", filePath);
        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.Hidden);

        index.ApplyCreatedOrChanged(@"\\server\share", filePath);

        Assert.IsTrue(index.ToStore().Records.Single(r => r.Name == "hidden.txt").Flags.HasFlag(FileRecordFlags.Hidden));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-delta-test-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
