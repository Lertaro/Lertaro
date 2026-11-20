using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.IndexV2.Delta;
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
        Assert.IsTrue(index.ToStore().Records.Any(r => r.Name == "child.txt"));
    }

    // A watcher's Changed on a directory: its children report their own events (#298).
    [TestMethod]
    public void ApplyCreatedOrChanged_WithoutChildren_UpsertsOnlyTheDirectory()
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
        File.WriteAllText(Path.Combine(subDir, "child.txt"), "test");

        Assert.IsTrue(index.ApplyCreatedOrChanged(@"\\server\share", subDir, includeChildren: false));

        var names = index.ToStore().Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "subfolder");
        CollectionAssert.DoesNotContain(names, "child.txt");
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

    // A directory read can fail after Directory.EnumerateFileSystemEntries has returned: it opens the
    // directory up front but reads it lazily, so a folder that vanishes, a drive that is pulled, or (the
    // deterministic stand-in used here) a "directory" that is really a file throws from MoveNext. A file
    // named "foo " beside a folder "foo" gets there: FileInfo trims the trailing space and reports the
    // folder, so the walk treats the entry as a directory, but the enumeration keeps the space and opens
    // the file.
    [TestMethod]
    public void ApplyCreatedOrChanged_ChildThatFailsWhileBeingRead_SkipsItAndIndexesTheRest()
    {
        using var tempDir = new TempDirectory();
        var drop = Path.Combine(tempDir.Path, "drop");
        Directory.CreateDirectory(Path.Combine(drop, "foo"));
        File.WriteAllText(@"\\?\" + Path.Combine(drop, "foo "), "x");
        File.WriteAllText(Path.Combine(drop, "zzz.txt"), "x");
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });

        fixture.Index.Mutate((_, delta) => DeltaPathApplier.ApplyCreatedOrChanged(delta, 1, tempDir.Path, drop));

        Assert.IsTrue(fixture.Index.ToStore().Records.Any(r => r.Name == "zzz.txt"));
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
