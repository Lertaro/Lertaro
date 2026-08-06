using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

// End-to-end TreeBuilder.Run() coverage -- TreeBuilderRecordExtensionsTests/TreeBuilderDiffExtensionsTests
// deliberately stay unit-scoped (construct a builder but never Run() it); this file is for behavior that
// only shows up once a real multi-threaded walk actually happens.
[TestClass]
public sealed class TreeBuilderTests
{
    // Regression coverage: _onProgress only ever received a single cumulative count before, with no way
    // to tell files and directories apart. _indexedFiles/_indexedDirs must each land on the record kind
    // that actually produced them, not just split the total arbitrarily.
    [TestMethod]
    public void Run_MixedFilesAndDirectories_TracksFilesAndDirsSeparately()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub1"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub2"));
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "y");
        File.WriteAllText(Path.Combine(dir.Path, "c.txt"), "z");

        var builder = new TreeBuilder(
            new FileRecordStore(), dir.Path, dir.Path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
            CancellationToken.None, (_, _) => { });
        builder._store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        builder.Run();

        Assert.AreEqual(3, builder._indexedFiles);
        Assert.AreEqual(2, builder._indexedDirs);
    }

    [TestMethod]
    public void EnqueueDirectory_AncestorLoopDetected_IncrementsReparseSkippedAndReturns()
    {
        using var dir = new TempDirectory();
        var builder = new TreeBuilder(
            new FileRecordStore(), dir.Path, dir.Path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
            CancellationToken.None, (_, _) => { });

        var parentAncestors = new AncestorNode(dir.Path, null);
        // Attempt to enqueue the exact same path that is already in parentAncestors
        builder.EnqueueDirectory(dir.Path, dir.Path, parentId: 2, depth: 1, NetworkIgnoreRuleSet.Empty, parentAncestors);

        Assert.AreEqual(1, builder._reparseSkipped);
        Assert.AreEqual(1, builder._skippedItems);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
