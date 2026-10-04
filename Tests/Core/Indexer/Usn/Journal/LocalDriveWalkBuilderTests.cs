using Lertaro.Core.Indexer.NetworkDrive.Walk;
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

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None, options: Options());

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

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None, options: Options());

        var names = store.Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "sub");
        CollectionAssert.Contains(names, "file.txt");
        var subRecord = store.Records.Single(r => r.Name == "sub");
        Assert.IsTrue(subRecord.Flags.HasFlag(FileRecordFlags.Directory));
        Assert.IsTrue(subRecord.Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void Build_SecondPass_DoesNotReuseStaleCachedChildren()
    {
        using var dir = new TempDirectory();
        var subDir = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "real.txt"), "x");

        var firstPass = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None, options: Options());

        // A cached child that is absent from the live native listing must invalidate reuse. The second
        // pass then rebuilds the directory and drops this stale record.
        var subRecord = firstPass.Records.Single(r => r.Name == "sub");
        firstPass.Records.Add(new FileRecord((UInt128)999, subRecord.Id, "ghost.txt", FileRecordFlags.None));

        var secondPass = LocalDriveWalkBuilder.Build("Z", dir.Path, firstPass, (_, _) => { }, CancellationToken.None, options: Options());

        var names = secondPass.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("ghost.txt", names);
        CollectionAssert.Contains(names, "real.txt");
    }

    [TestMethod]
    public void Build_ForceFullScan_DoesNotReuseCachedDirectoryChildren()
    {
        using var dir = new TempDirectory();
        var subDir = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "real.txt"), "x");

        var firstPass = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None, options: Options());

        var subRecord = firstPass.Records.Single(r => r.Name == "sub");
        firstPass.Records.Add(new FileRecord((UInt128)999, subRecord.Id, "ghost.txt", FileRecordFlags.None));

        var secondPass = LocalDriveWalkBuilder.Build("Z", dir.Path, firstPass, (_, _) => { }, CancellationToken.None, options: Options(), forceFullScan: true);

        Assert.IsFalse(secondPass.Records.Any(r => r.Name == "ghost.txt"));
        CollectionAssert.Contains(secondPass.Records.Select(r => r.Name).ToList(), "real.txt");
    }

    [TestMethod]
    public void Build_ProducedCache_StillSurfacesInLocalDriveCacheLocatorListing()
    {
        using var dir = new TempDirectory();
        using var cacheDir = new TempDirectory();
        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None, options: Options());

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
            LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, cts.Token, options: Options());
        }
        catch (OperationCanceledException)
        {
            // Task.WaitAll(tasks, token) surfaces this as a TaskCanceledException -- a subclass, not the
            // exact type, so a plain "is OperationCanceledException" catch is the correct check here.
            threw = true;
        }

        Assert.IsTrue(threw);
    }

    // --- Exclusion rules (WalkOptions + recheckExclusions) ------------------------------------------
    // The local walk filters with the rules its caller hands it, the same field list the network path
    // uses. These run against a real temp tree: an excluded directory must not be descended into (so
    // nothing in its subtree is captured) while its siblings still are, and a newly excluded path must not
    // survive out of the previous store's cached children.

    [TestMethod]
    public void Build_ExcludedDirectory_CapturesNothingInThatSubtreeAndKeepsItsSibling()
    {
        using var dir = new TempDirectory();
        var excluded = Path.Combine(dir.Path, "excluded");
        Directory.CreateDirectory(Path.Combine(excluded, "deeper"));
        File.WriteAllText(Path.Combine(excluded, "skipped.txt"), "x");
        File.WriteAllText(Path.Combine(excluded, "deeper", "skipped-too.txt"), "x");
        var sibling = Path.Combine(dir.Path, "sibling");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "kept.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            options: Options(excludedPaths: [excluded]));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("excluded", names);
        Assert.DoesNotContain("deeper", names);
        Assert.DoesNotContain("skipped.txt", names);
        Assert.DoesNotContain("skipped-too.txt", names);
        CollectionAssert.Contains(names, "sibling");
        CollectionAssert.Contains(names, "kept.txt");
    }

    // The diff-reuse case: the previous store still has this directory Listed with its children, so the
    // walk must not carry those cached records into the new store now that the path is excluded.
    [TestMethod]
    public void Build_SecondPassWithANewlyExcludedDirectory_DropsItsCachedSubtree()
    {
        using var dir = new TempDirectory();
        var excluded = Path.Combine(dir.Path, "excluded");
        Directory.CreateDirectory(excluded);
        File.WriteAllText(Path.Combine(excluded, "skipped.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "kept.txt"), "x");

        var firstPass = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            options: Options());
        CollectionAssert.Contains(firstPass.Records.Select(r => r.Name).ToList(), "skipped.txt");

        var secondPass = LocalDriveWalkBuilder.Build("Z", dir.Path, firstPass, (_, _) => { }, CancellationToken.None,
            options: Options(excludedPaths: [excluded]));

        var names = secondPass.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("excluded", names);
        Assert.DoesNotContain("skipped.txt", names);
        CollectionAssert.Contains(names, "kept.txt");
        Assert.IsTrue(secondPass.IsComplete);
    }

    // The other direction, which is what recheckExclusions is actually for: a reused (mtime-unchanged)
    // directory's cached children were filtered under the rules active then, so a path un-excluded since
    // has to be reconciled from the live listing to reappear.
    [TestMethod]
    public void Build_SecondPassWithExclusionRemoved_AddsTheSubtreeBack()
    {
        using var dir = new TempDirectory();
        var reopened = Path.Combine(dir.Path, "reopened");
        Directory.CreateDirectory(reopened);
        File.WriteAllText(Path.Combine(reopened, "newly-visible.txt"), "x");

        var excludedPass = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            options: Options(excludedPaths: [reopened]));
        Assert.DoesNotContain("newly-visible.txt", excludedPass.Records.Select(r => r.Name).ToList());

        var reopenedPass = LocalDriveWalkBuilder.Build("Z", dir.Path, excludedPass, (_, _) => { }, CancellationToken.None,
            options: Options());

        CollectionAssert.Contains(reopenedPass.Records.Select(r => r.Name).ToList(), "newly-visible.txt");
    }

    // A glob and a regex, built through the same FromUserSettings the network path uses, must filter this
    // walk the same way -- including on directories, which the walk must not descend into.
    [TestMethod]
    public void Build_GlobAndRegexRulesFromUserSettings_AreHonouredLikeTheNetworkPath()
    {
        using var dir = new TempDirectory();
        var modules = Path.Combine(dir.Path, "node_modules");
        Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "pkg.js"), "x");
        var secretDir = Path.Combine(dir.Path, "secret-dir");
        Directory.CreateDirectory(secretDir);
        File.WriteAllText(Path.Combine(secretDir, "hidden.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "secret-plan.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "kept.txt"), "x");

        var settings = new UserSettings
        {
            ExcludedPaths = [],
            IgnoredPathGlobs = ["node_modules"],
            IgnoredPathRegexes = ["^secret-"],
        };

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            options: WalkOptions.FromUserSettings(settings));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("node_modules", names);
        Assert.DoesNotContain("pkg.js", names);
        Assert.DoesNotContain("secret-dir", names);
        Assert.DoesNotContain("hidden.txt", names);
        Assert.DoesNotContain("secret-plan.txt", names);
        CollectionAssert.Contains(names, "kept.txt");
    }

    // Excluding the walk's own root (a volume root listed in ExcludedPaths) must leave the drive with an
    // empty-but-complete index -- the root record only, no error, no crash -- rather than skipping the
    // walk entirely: TreeBuilder enqueues the root itself without asking ShouldDescend, and every child it
    // then lists is rejected by the same rule that excludes an ancestor.
    [TestMethod]
    public void Build_WholeRootExcluded_ProducesARootOnlyStoreInsteadOfFailing()
    {
        using var dir = new TempDirectory();
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "file.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            options: Options(excludedPaths: [dir.Path]));

        var root = store.Records.Single();
        Assert.AreEqual(root.Id, root.ParentId);
        Assert.IsTrue(store.IsComplete);
    }

    // Test-side rules: the production caller passes the rules the App sent over SetMachineSettings
    // (pinned end to end in LocalDriveWalkBuilderExclusionRulesTests), so here the rules are explicit and
    // a developer's own exclusion settings cannot change what these cases walk.
    private static WalkOptions Options(
        IReadOnlyList<string>? excludedPaths = null,
        IReadOnlyList<string>? globs = null,
        IReadOnlyList<string>? regexes = null) => new(
            excludedPaths ?? [],
            globs ?? [],
            regexes ?? [],
            MaxDepth: 0,
            WorkerCount: 0,
            UseIgnoreFiles: false);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
