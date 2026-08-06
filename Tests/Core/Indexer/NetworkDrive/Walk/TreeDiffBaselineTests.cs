using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeDiffBaselineTests
{
    [TestMethod]
    public void From_NullStore_ReturnsNull() => Assert.IsNull(TreeDiffBaseline.From(null));

    [TestMethod]
    public void From_EmptyStore_ReturnsNull()
    {
        var store = new FileRecordStore();

        Assert.IsNull(TreeDiffBaseline.From(store));
    }

    [TestMethod]
    public void TryGetUnchangedChildren_MtimeMatchesLive_ReturnsChildrenExcludingRoot()
    {
        using var dir = new TempDirectory();
        var mtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(dir.Path));

        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: mtime));
        store.Records.Add(new FileRecord(2, 1, "child1.txt", FileRecordFlags.None));
        store.Records.Add(new FileRecord(3, 1, "child2.txt", FileRecordFlags.None));

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(dir.Path, 1, out var children);

        Assert.IsTrue(found);
        var names = children.Select(c => c.Name).OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(new[] { "child1.txt", "child2.txt" }, names);
    }

    [TestMethod]
    public void TryGetUnchangedChildren_MtimeDoesNotMatchLive_ReturnsFalse()
    {
        using var dir = new TempDirectory();

        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: 123));

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(dir.Path, 1, out var children);

        Assert.IsFalse(found);
        Assert.IsEmpty(children.ToList());
    }

    [TestMethod]
    public void TryGetUnchangedChildren_DirectoryNotListed_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var mtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(dir.Path));

        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory, lastWriteTimeUnixSeconds: mtime)); // no Listed flag

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(dir.Path, 1, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetUnchangedChildren_UnknownDirectoryId_ReturnsFalse()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed));

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(@"C:\anywhere", 999, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetUnchangedChildren_NonDirectoryRecord_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        var mtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(dir.Path));

        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "file.txt", FileRecordFlags.Listed, lastWriteTimeUnixSeconds: mtime)); // not a directory

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(dir.Path, 1, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void From_DuplicateRecordId_OnlyLastOccurrenceContributesToChildList()
    {
        using var dir = new TempDirectory();
        var mtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(dir.Path));

        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: mtime));
        store.Records.Add(new FileRecord(2, 1, "first", FileRecordFlags.None));
        store.Records.Add(new FileRecord(2, 1, "second", FileRecordFlags.None)); // duplicate id, same parent

        var baseline = TreeDiffBaseline.From(store)!;
        baseline.TryGetUnchangedChildren(dir.Path, 1, out var children);
        var list = children.ToList();

        Assert.HasCount(1, list);
        Assert.AreEqual("second", list[0].Name);
    }

    // Path-free overload -- used by ReFsScanner, which walks purely by file ID and already knows a
    // directory's live mtime from its parent's own listing, with no path string ever built.
    [TestMethod]
    public void TryGetUnchangedChildren_PathFreeOverload_MtimeMatches_ReturnsChildren()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: 42));
        store.Records.Add(new FileRecord(2, 1, "child.txt", FileRecordFlags.None));

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(directoryId: 1, liveMtimeUnixSeconds: 42, out var children);

        Assert.IsTrue(found);
        CollectionAssert.AreEqual(new[] { "child.txt" }, children.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void TryGetUnchangedChildren_PathFreeOverload_MtimeMismatch_ReturnsFalse()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: 42));

        var baseline = TreeDiffBaseline.From(store)!;
        var found = baseline.TryGetUnchangedChildren(directoryId: 1, liveMtimeUnixSeconds: 99, out var children);

        Assert.IsFalse(found);
        Assert.IsEmpty(children.ToList());
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
