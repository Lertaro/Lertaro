using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class NetworkIndexTests
{
    // Regression coverage: Dispose() used to only call _live?.Dispose() without clearing the field, so
    // every _live-touching method's own "if (_live == null) return;" guard (Count, SaveToCache,
    // SearchStreaming, GetRecentFiles, ApplyCreatedOrChanged/Deleted/Renamed) silently assumed a disposed
    // instance would look null and no-op -- instead it stayed non-null and pointed at an already-disposed
    // LiveIndex, so calling any of them again threw ObjectDisposedException instead of no-op'ing. This
    // became reachable in practice once WatcherManager started debouncing its publish call: a watcher-
    // detected change can now be scheduled up to a second before it's actually persisted, widening the
    // window for PublishCheckpoint's ReleaseCachedIndex to dispose this same instance first.
    [TestMethod]
    public void Dispose_ThenSaveToCacheAgain_DoesNotThrow()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();

        index.SaveToCache(path);
    }

    [TestMethod]
    public void Dispose_ThenReadCount_ReturnsZeroInsteadOfThrowing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();

        Assert.AreEqual(0, index.Count);
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();
        index.Dispose();
    }

    // Listing a directory out of THIS index rather than the service's: network, WSL and folder indexes
    // are held in this process, so a caller enumerating a share or a folder index can only reach one
    // through here. The walk itself is IndexV2Searcher's (covered by DirectoryEnumeratorTests); what
    // these pin is that this drive answers for its own paths and declines everything else.
    [TestMethod]
    public void EnumerateDirectory_DirectoryHeldByThisIndex_ListsItFromTheIndex()
    {
        using var index = OpenShareIndex(out var dir);
        using (dir)
        {
            var results = new List<SearchResult>();

            var resolved = index.EnumerateDirectory(@"\\server\share\Movies", recursive: false, patterns: null,
                limit: 0, results.Add, CancellationToken.None);

            Assert.IsTrue(resolved);
            CollectionAssert.AreEquivalent(
                new[] { @"\\server\share\Movies\a.mp4", @"\\server\share\Movies\notes.txt" },
                results.ConvertAll(r => r.Path));
        }
    }

    // The share's own root, which only resolves because the enumerator normalizes the requested path to
    // end with a separator -- a UNC or folder-index source root always does, unlike "C:\".
    [TestMethod]
    public void EnumerateDirectory_ShareRootItself_ListsItsTopLevel()
    {
        using var index = OpenShareIndex(out var dir);
        using (dir)
        {
            var results = new List<SearchResult>();

            var resolved = index.EnumerateDirectory(@"\\server\share", recursive: false, patterns: null,
                limit: 0, results.Add, CancellationToken.None);

            Assert.IsTrue(resolved);
            Assert.AreEqual(@"\\server\share\Movies", results.Single().Path);
        }
    }

    [TestMethod]
    public void EnumerateDirectory_FilePattern_IsApplied()
    {
        using var index = OpenShareIndex(out var dir);
        using (dir)
        {
            var results = new List<SearchResult>();

            index.EnumerateDirectory(@"\\server\share\Movies", recursive: false, patterns: new[] { "*.mp4" },
                limit: 0, results.Add, CancellationToken.None);

            Assert.AreEqual(@"\\server\share\Movies\a.mp4", results.Single().Path);
        }
    }

    // False, not "empty": the caller still has other indexes to try and a live walk after them, and an
    // empty stream alone cannot be told apart from a directory that genuinely holds nothing.
    [TestMethod]
    public void EnumerateDirectory_PathUnderAnotherSource_ReportsNotHeldHere()
    {
        using var index = OpenShareIndex(out var dir);
        using (dir)
        {
            var resolved = index.EnumerateDirectory(@"\\other\share\Movies", recursive: true, patterns: null,
                limit: 0, _ => Assert.Fail("an index that does not hold the path must emit nothing"), CancellationToken.None);

            Assert.IsFalse(resolved);
        }
    }

    [TestMethod]
    public void EnumerateDirectory_IndexNotLoadedYet_ReportsNotHeldHereInsteadOfThrowing()
    {
        using var index = new NetworkIndex("Z");

        var resolved = index.EnumerateDirectory(@"Z:\", recursive: false, patterns: null,
            limit: 0, _ => Assert.Fail("an unloaded index has nothing to emit"), CancellationToken.None);

        Assert.IsFalse(resolved);
    }

    private static NetworkIndex OpenShareIndex(out IDisposable tempDirectory)
    {
        var dir = new TempDirectory();
        tempDirectory = dir;
        var path = Path.Combine(dir.Path, "test.idx");
        var store = BuildStore(@"\\server\share",
            new FileRecord(2, 1, "Movies", FileRecordFlags.Directory),
            new FileRecord(3, 2, "a.mp4", FileRecordFlags.None),
            new FileRecord(4, 2, "notes.txt", FileRecordFlags.None));
        SnapshotWriter.Write(store, path);
        return NetworkIndex.FromSnapshotFile(@"\\server\share", path);
    }

    // #562 review: the refresh must keep the previous complete index when the share drops mid-walk (coverage
    // for what #562 does, not a new fix). The root's listing is read in full first -- buffered, because the
    // previous index knows a child, so no handle is left open -- and while its entries are being consumed, the
    // progress callback moves the root away. The next folder then fails with the root gone. Build has to end
    // in cancellation, which DriveRefreshRunner turns into "cached" without saving, before beforeFinalWrite
    // (wired to ReleaseCachedIndex, which drops the index being served). That callback runs before
    // NetworkIndex.FromStore writes the cache file, and throws here, so a failing run writes nothing either.
    [TestMethod]
    public void Build_ShareLostMidWalk_EndsBeforeTouchingTheSavedIndex()
    {
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "share");
        // Folders, not files: nothing for an on-access scanner to hold open when the root is moved away.
        for (var i = 0; i < TreeBuilder.ProgressBatchSize + 50; i++)
            Directory.CreateDirectory(Path.Combine(root, $"d{i:D4}"));
        var previous = BuildStore(root, new FileRecord(2, 1, "old.txt", FileRecordFlags.None, 1, 0, 0, 0));
        previous.IsComplete = true;
        var shareLost = false;

        Assert.Throws<OperationCanceledException>(() => NetworkIndex.Build(root, root, root,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false), CancellationToken.None,
            (_, _) =>
            {
                if (shareLost)
                    return;
                Directory.Move(root, root + "-gone");
                shareLost = true;
            },
            (_, _) => Assert.Fail("No checkpoint is due this early."),
            previous,
            beforeFinalWrite: () => throw new AssertFailedException("The walk finished and was about to replace the saved index.")));

        Assert.IsTrue(shareLost, "precondition: the share dropped mid-walk");
    }

    private static FileRecordStore BuildStore(string drive, params FileRecord[] records)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        store.Records.AddRange(records);
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
