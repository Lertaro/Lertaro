using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.IndexV2.Persistence;

[TestClass]
public sealed class SnapshotWriterTests
{
    // Regression coverage for the GitHub issue this fixes: a network drive's periodic scan checkpoint and
    // its FileSystemWatcher's incremental updates are two entirely separate LiveIndex instances with no
    // lock in common, so both could previously call SnapshotWriter.Write for the SAME final path at once
    // -- racing on the same fixed ".tmp" filename and the final File.Replace, surfacing as "the process
    // cannot access the file" on large shares where a single write takes long enough to widen the window.
    [TestMethod]
    public void Write_ConcurrentCallsForSameFinalPath_AllSucceedAndLeaveAValidSnapshot()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "shared.idx");

        var stores = Enumerable.Range(0, 8).Select(i => BuildStore(fileCount: i + 1)).ToList();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.ForEach(stores, store =>
        {
            try
            {
                SnapshotWriter.Write(store, path);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.IsEmpty(exceptions, string.Join("; ", exceptions.Select(e => e.Message)));
        Assert.IsFalse(File.Exists(path + ".tmp"), "No temp file should be left behind once every writer finishes.");

        using var snapshot = Snapshot.Open(path);
        // Whichever writer went last, its own record count (1 root + N files) must be fully intact --
        // a torn/interleaved write would show a row count that doesn't match ANY single writer's input.
        var expectedCounts = stores.Select(s => s.Records.Count).ToHashSet();
        CollectionAssert.Contains(expectedCounts.ToList(), snapshot.Count);
    }

    [TestMethod]
    public void Write_ParentSequenceNumberMismatch_ResolvesParentVia48BitRecordIndexFallback()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "sequence_mismatch.idx");

        var store = new FileRecordStore
        {
            SourceKey = "D",
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = 1,
        };

        // Root
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        // Parent directory with FRN index 10 and sequence 2: 0x000200000000000A
        UInt128 parentDirectoryFrn = ((ulong)2 << 48) | 10;
        store.Records.Add(new FileRecord(parentDirectoryFrn, 1, "WorkDir", FileRecordFlags.Directory));

        // Child file with ParentId having FRN index 10 but OLD sequence 1: 0x000100000000000A
        UInt128 childMismatchedParentFrn = ((ulong)1 << 48) | 10;
        UInt128 childFrn = ((ulong)1 << 48) | 20;
        store.Records.Add(new FileRecord(childFrn, childMismatchedParentFrn, "ChildFile.txt", FileRecordFlags.None));

        SnapshotWriter.Write(store, path);

        using var snapshot = Snapshot.Open(path);
        Assert.AreEqual(0, snapshot.Meta.OrphanCount, "Child file should be resolved via 48-bit Record Index fallback, not orphaned.");
        Assert.AreEqual(3, snapshot.Count);
    }

    // Regression: a truncated file (torn write / crashed checkpoint) used to map fine and only blow
    // up later with an access violation when a query walked off the end of a section -- it must be
    // rejected at open with InvalidDataException instead.
    [TestMethod]
    public void Open_TruncatedFile_ThrowsInvalidDataException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "truncated.idx");

        SnapshotWriter.Write(BuildStore(fileCount: 4), path);
        var fullLength = new FileInfo(path).Length;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(fullLength - 16); // chop bytes off the tail sections
        }

        Assert.ThrowsExactly<InvalidDataException>(() => Snapshot.Open(path));
    }

    private static FileRecordStore BuildStore(int fileCount)
    {
        var store = new FileRecordStore
        {
            SourceKey = "Z",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        for (var i = 0; i < fileCount; i++)
            store.Records.Add(new FileRecord((UInt128)(2 + i), 1, $"file{i}.txt", FileRecordFlags.None));
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
