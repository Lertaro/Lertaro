using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeBuilderDiffExtensionsTests
{
    private static TreeBuilder CreateBuilder(string root, TreeDiffBaseline? diffBaseline = null, bool recheckExclusions = false) => new(
        new FileRecordStore(), root, root,
        new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
        CancellationToken.None, (_, _) => { }, onCheckpoint: null, diffBaseline, recheckExclusions);

    [TestMethod]
    public void RegisterDirectoryIndices_OnlyIndexesDirectoryRecords()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);
        var batch = new List<FileRecord>
        {
            new(1, 0, "dir1", FileRecordFlags.Directory),
            new(2, 0, "file1.txt", FileRecordFlags.None),
        };

        builder.RegisterDirectoryIndices(5, batch);

        Assert.IsTrue(builder._indexById.ContainsKey(1));
        Assert.AreEqual(5, builder._indexById[1]);
        Assert.IsFalse(builder._indexById.ContainsKey(2));
    }

    [TestMethod]
    public void MarkListed_KnownDirectory_SetsListedFlag()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);
        builder._store.Records.Add(new FileRecord(1, 1, "d", FileRecordFlags.Directory));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        builder.MarkListed(1);

        Assert.IsTrue(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void MarkListed_AlreadyListed_IsANoOp()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);
        builder._store.Records.Add(new FileRecord(1, 1, "d", FileRecordFlags.Directory | FileRecordFlags.Listed));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        builder.MarkListed(1);

        Assert.HasCount(1, builder._store.Records);
        Assert.IsTrue(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void MarkListed_UnknownId_DoesNothing()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        builder.MarkListed(999);

        Assert.IsEmpty(builder._store.Records);
    }

    [TestMethod]
    public void TryReuseUnchangedDirectory_MtimeUnchanged_CopiesCachedChildrenAndMarksListed()
    {
        using var root = new TempDirectory();
        var reuseDir = Path.Combine(root.Path, "reuseDir");
        Directory.CreateDirectory(reuseDir);
        File.WriteAllText(Path.Combine(reuseDir, "keep1.txt"), "a");
        File.WriteAllText(Path.Combine(reuseDir, "keep2.txt"), "b");
        var liveMtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(reuseDir));

        var previousStore = new FileRecordStore();
        previousStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        previousStore.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: liveMtime));
        previousStore.Records.Add(new FileRecord(11, 10, "keep1.txt", FileRecordFlags.None));
        previousStore.Records.Add(new FileRecord(12, 10, "keep2.txt", FileRecordFlags.None));
        var baseline = TreeDiffBaseline.From(previousStore);

        var builder = CreateBuilder(root.Path, baseline, recheckExclusions: false);
        builder._store.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory));
        builder.RegisterDirectoryIndices(0, builder._store.Records);
        var current = new WorkItem(reuseDir, "reuseDir", 10, 1, NetworkIgnoreRuleSet.Empty);

        var reused = builder.TryReuseUnchangedDirectory(current);

        Assert.IsTrue(reused);
        Assert.AreEqual(1, builder._reusedDirectories);
        var names = builder._store.Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "keep1.txt");
        CollectionAssert.Contains(names, "keep2.txt");
        Assert.IsTrue(builder._store.Records[builder._indexById[10]].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void TryReuseUnchangedDirectory_MtimeChanged_ReturnsFalseAndAddsNothing()
    {
        using var root = new TempDirectory();
        var reuseDir = Path.Combine(root.Path, "reuseDir");
        Directory.CreateDirectory(reuseDir);

        var previousStore = new FileRecordStore();
        previousStore.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: 1));
        var baseline = TreeDiffBaseline.From(previousStore);

        var builder = CreateBuilder(root.Path, baseline);
        builder._store.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory));
        builder.RegisterDirectoryIndices(0, builder._store.Records);
        var current = new WorkItem(reuseDir, "reuseDir", 10, 1, NetworkIgnoreRuleSet.Empty);

        var reused = builder.TryReuseUnchangedDirectory(current);

        Assert.IsFalse(reused);
        Assert.HasCount(1, builder._store.Records); // only the seeded reuseDir record itself
    }

    [TestMethod]
    public void TryReuseUnchangedDirectory_RecheckExclusions_PicksUpNewLiveEntryNotInCache()
    {
        using var root = new TempDirectory();
        var reuseDir = Path.Combine(root.Path, "reuseDir");
        Directory.CreateDirectory(reuseDir);
        File.WriteAllText(Path.Combine(reuseDir, "cached.txt"), "a");
        File.WriteAllText(Path.Combine(reuseDir, "newfile.txt"), "b"); // exists live, absent from the cached snapshot
        var liveMtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(reuseDir));

        var previousStore = new FileRecordStore();
        previousStore.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: liveMtime));
        previousStore.Records.Add(new FileRecord(11, 10, "cached.txt", FileRecordFlags.None));
        var baseline = TreeDiffBaseline.From(previousStore);

        var builder = CreateBuilder(root.Path, baseline, recheckExclusions: true);
        builder._store.Records.Add(new FileRecord(10, 1, "reuseDir", FileRecordFlags.Directory));
        builder.RegisterDirectoryIndices(0, builder._store.Records);
        var current = new WorkItem(reuseDir, "reuseDir", 10, 1, NetworkIgnoreRuleSet.Empty);

        var reused = builder.TryReuseUnchangedDirectory(current);

        Assert.IsTrue(reused);
        var names = builder._store.Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "cached.txt");
        CollectionAssert.Contains(names, "newfile.txt");
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
