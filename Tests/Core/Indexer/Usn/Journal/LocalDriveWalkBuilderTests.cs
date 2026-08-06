using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Indexer.Usn.Journal;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.Indexer.Usn.Journal;

// LocalDriveWalkBuilder replaces FolderDriveScanner's hand-rolled full-rescan-only walk with a thin
// orchestrator around the same TreeBuilder/TreeDiffBaseline machinery network/WSL/folder-index drives use.
// These tests cover the orchestrator itself (root-record shape, SourceKind/IdKind preservation, and that a
// previous store's cached children genuinely get reused) -- not TreeBuilder's own walk/diff mechanics,
// already covered by Tests/Core/Indexer/NetworkDrive/Walk/TreeBuilder*Tests.cs.
[TestClass]
public sealed class LocalDriveWalkBuilderTests
{
    [TestMethod]
    public void Build_FreshWalk_RootRecordPreservesLocalSourceKindAndIdKindWithARealTimestamp()
    {
        using var dir = new TempDirectory();

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None);

        Assert.AreEqual(FileRecordSourceKind.LocalMft, store.SourceKind);
        Assert.AreEqual(FileRecordIdKind.SourceLocalId64, store.IdKind);
        Assert.AreEqual("Z", store.SourceKey);
        Assert.IsTrue(store.IsComplete);
        var root = store.Records.Single(r => r.Id == r.ParentId);
        Assert.AreNotEqual(0u, root.LastWriteTimeUnixSeconds);
    }

    [TestMethod]
    public void Build_RealDirectoryTree_WalksFilesAndMarksDirectoriesListed()
    {
        using var dir = new TempDirectory();
        var subDir = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None);

        var names = store.Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "sub");
        CollectionAssert.Contains(names, "file.txt");
        var subRecord = store.Records.Single(r => r.Name == "sub");
        Assert.IsTrue(subRecord.Flags.HasFlag(FileRecordFlags.Directory));
        Assert.IsTrue(subRecord.Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void Build_SecondPassWithUnchangedTree_ReusesCachedDirectoryInsteadOfRelistingFromDisk()
    {
        using var dir = new TempDirectory();
        var subDir = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "real.txt"), "x");

        var firstPass = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None);

        // Splice in a record that has no backing file on disk -- if the second pass re-lists "sub" from
        // disk (rather than reusing firstPass's cached children), this record simply won't appear.
        var subRecord = firstPass.Records.Single(r => r.Name == "sub");
        firstPass.Records.Add(new FileRecord((UInt128)999, subRecord.Id, "ghost.txt", FileRecordFlags.None));

        var secondPass = LocalDriveWalkBuilder.Build("Z", dir.Path, firstPass, (_, _) => { }, CancellationToken.None);

        CollectionAssert.Contains(secondPass.Records.Select(r => r.Name).ToList(), "ghost.txt");
    }

    [TestMethod]
    public void Build_ProducedCache_StillSurfacesInLocalDriveCacheLocatorListing()
    {
        using var dir = new TempDirectory();
        using var cacheDir = new TempDirectory();
        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None);

        SnapshotWriter.Write(store, Path.Combine(cacheDir.Path, "z.idx"));

        var entries = LocalDriveCacheLocator.ListCachedDrives(cacheDir.Path);
        Assert.IsTrue(entries.Any(e => e.Drive == "Z"));
    }

    // Regression coverage for real per-drive rebuild cancellation (Phase 3): a token cancelled before the
    // walk starts must actually stop TreeBuilder.Run() rather than silently completing anyway -- this is
    // what makes the Settings UI's Stop button meaningful for a non-journal drive.
    [TestMethod]
    public void Build_CancelledToken_ThrowsOperationCanceledInsteadOfCompleting()
    {
        using var dir = new TempDirectory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var threw = false;
        try
        {
            LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Task.WaitAll(tasks, token) surfaces this as a TaskCanceledException -- a subclass, not the
            // exact type, so a plain "is OperationCanceledException" catch is the correct check here.
            threw = true;
        }

        Assert.IsTrue(threw);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
