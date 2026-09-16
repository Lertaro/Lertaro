using static Lertaro.Core.Tests.IndexV2.Search.DirectoryEnumeratorFixture;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Listing a directory straight out of the index snapshot: which entries come back, how the path is
// resolved, and how the filter pattern and result limit narrow the answer. The delta-overlay cases live
// in DirectoryEnumeratorOverlayTests.
[TestClass]
public sealed class DirectoryEnumeratorTests
{
    [TestMethod]
    public void Enumerate_NonRecursive_ListsDirectChildrenOnlyIncludingDirectories()
    {
        using var fixture = BuildSampleDrive();

        var (resolved, results) = Enumerate(fixture, @"C:\Projects");

        Assert.IsTrue(resolved);
        CollectionAssert.AreEqual(new[] { @"C:\Projects\notes.md", @"C:\Projects\readme.txt", @"C:\Projects\sub" }, Paths(results));
        Assert.IsTrue(results.Single(r => r.Name == "sub").IsDir);
    }

    [TestMethod]
    public void Enumerate_Recursive_WalksTheWholeSubtree()
    {
        using var fixture = BuildSampleDrive();

        var (_, results) = Enumerate(fixture, @"C:\Projects", recursive: true);

        CollectionAssert.AreEqual(
            new[] { @"C:\Projects\notes.md", @"C:\Projects\readme.txt", @"C:\Projects\sub", @"C:\Projects\sub\deep.txt" },
            Paths(results));
    }

    [TestMethod]
    public void Enumerate_TrailingSeparatorAndForwardSlashes_ResolveToTheSameDirectory()
    {
        using var fixture = BuildSampleDrive();

        Assert.HasCount(3, Enumerate(fixture, @"C:\Projects\").Results);
        Assert.HasCount(3, Enumerate(fixture, "C:/Projects").Results);
        Assert.HasCount(3, Enumerate(fixture, @"c:\projects").Results);
    }

    [TestMethod]
    public void Enumerate_DriveRoot_ListsTopLevelEntries()
    {
        using var fixture = BuildSampleDrive();

        var (resolved, results) = Enumerate(fixture, @"C:\");

        Assert.IsTrue(resolved);
        CollectionAssert.AreEqual(new[] { @"C:\Downloads", @"C:\Projects" }, Paths(results));
    }

    // A file pattern: "*.md" is written to pick markdown files, not to hide every folder in the tree.
    [TestMethod]
    public void Enumerate_FilterPattern_AppliesToFilesButNeverToDirectories()
    {
        using var fixture = BuildSampleDrive();

        var (_, results) = Enumerate(fixture, @"C:\Projects", filterPattern: "*.md");

        CollectionAssert.AreEqual(new[] { @"C:\Projects\notes.md", @"C:\Projects\sub" }, Paths(results));
    }

    [TestMethod]
    public void Enumerate_MultipleFilterPatterns_MatchAnyOfThem()
    {
        using var fixture = BuildSampleDrive();

        var (_, results) = Enumerate(fixture, @"C:\Downloads", filterPattern: "*.md;*.exe");

        CollectionAssert.AreEqual(new[] { @"C:\Downloads\install.exe" }, Paths(results));
    }

    // The filter says which files to RETURN, never where to look -- a subtree behind a directory the
    // pattern doesn't match still has to be walked, or "*.txt" would silently miss most of the tree.
    [TestMethod]
    public void Enumerate_FilterPattern_DoesNotGateRecursion()
    {
        using var fixture = BuildSampleDrive();

        var (_, results) = Enumerate(fixture, @"C:\Projects", recursive: true, filterPattern: "*.txt");

        CollectionAssert.AreEqual(
            new[] { @"C:\Projects\readme.txt", @"C:\Projects\sub", @"C:\Projects\sub\deep.txt" },
            Paths(results));
    }

    [TestMethod]
    public void Enumerate_Limit_StopsAfterThatManyResults()
    {
        using var fixture = BuildSampleDrive();

        var (resolved, results) = Enumerate(fixture, @"C:\Projects", recursive: true, limit: 2);

        Assert.IsTrue(resolved);
        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void Enumerate_UnknownDirectoryOrForeignDrive_ReportsUnresolvedWithoutResults()
    {
        using var fixture = BuildSampleDrive();

        foreach (var path in new[] { @"C:\Projects\nope", @"D:\Projects", "" })
        {
            var (resolved, results) = Enumerate(fixture, path);
            Assert.IsFalse(resolved, $"'{path}' should not resolve in this drive's index");
            Assert.IsEmpty(results);
        }
    }

    [TestMethod]
    public void Enumerate_PathIsAFile_ReportsUnresolved()
    {
        using var fixture = BuildSampleDrive();

        var (resolved, results) = Enumerate(fixture, @"C:\Projects\readme.txt");

        Assert.IsFalse(resolved);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Enumerate_ResultsCarryIndexMetadata()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(3, 2, "readme.txt", FileRecordFlags.None, 4096, 100, 200, 300));

        var readme = Enumerate(fixture, @"C:\Projects").Results.Single(r => r.Name == "readme.txt");

        Assert.AreEqual(4096, readme.Metadata.Size);
        Assert.AreEqual(FileTimeHelper.FromUnixSeconds(200).ToLocalTime(), readme.Metadata.Modified);
    }

    // A folder index's source root is a full path ("D:\Projects\ProjectA\"), not a drive letter, and a
    // UNC share's is "\\server\share\" -- both are reached through the in-process NetworkIndexer rather
    // than the service. Listing the root ITSELF is the case that breaks if the path isn't normalized to
    // end with a separator before it is matched against that root.
    [TestMethod]
    public void Enumerate_SourceRootedAtAFolderIndex_ListsItsOwnRootAndSubdirectories()
    {
        using var fixture = LiveIndexFixture.Build(@"D:\Projects\ProjectA", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "src", FileRecordFlags.Directory),
            new FileRecord(3, 2, "main.cs", FileRecordFlags.None),
            new FileRecord(4, 1, "readme.md", FileRecordFlags.None),
        });

        var (resolvedRoot, atRoot) = Enumerate(fixture, @"D:\Projects\ProjectA");
        var (resolvedSub, inSub) = Enumerate(fixture, @"D:\Projects\ProjectA\src");

        Assert.IsTrue(resolvedRoot);
        CollectionAssert.AreEqual(new[] { @"D:\Projects\ProjectA\readme.md", @"D:\Projects\ProjectA\src" }, Paths(atRoot));
        Assert.IsTrue(resolvedSub);
        CollectionAssert.AreEqual(new[] { @"D:\Projects\ProjectA\src\main.cs" }, Paths(inSub));
    }

    [TestMethod]
    public void Enumerate_SourceRootedAtAUncShare_ListsItsOwnRoot()
    {
        using var fixture = LiveIndexFixture.Build(@"\\server\share", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "movie.mp4", FileRecordFlags.None),
        });

        var (resolved, results) = Enumerate(fixture, @"\\server\share");

        Assert.IsTrue(resolved);
        CollectionAssert.AreEqual(new[] { @"\\server\share\movie.mp4" }, Paths(results));
    }

    [TestMethod]
    public void Enumerate_PathOutsideThisSourcesRoot_ReportsUnresolved()
    {
        using var fixture = LiveIndexFixture.Build(@"D:\Projects\ProjectA", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "src", FileRecordFlags.Directory),
        });

        // Shares a textual prefix with the root but is a sibling, not a child -- the separator the
        // normalization adds is what keeps these apart.
        Assert.IsFalse(Enumerate(fixture, @"D:\Projects\ProjectAB").Resolved);
        Assert.IsFalse(Enumerate(fixture, @"D:\Projects").Resolved);
    }

    [TestMethod]
    public void Enumerate_HiddenAndSystemEntries_AreNotReturned()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
            new FileRecord(3, 2, "visible.txt", FileRecordFlags.None),
            new FileRecord(4, 2, "desktop.ini", FileRecordFlags.Hidden | FileRecordFlags.System),
            new FileRecord(5, 2, "notes.swp", FileRecordFlags.Hidden),
        });

        var (_, results) = Enumerate(fixture, @"C:\Projects");

        CollectionAssert.AreEqual(new[] { @"C:\Projects\visible.txt" }, Paths(results));
    }

    // Entry-level only: AppData is hidden, so a recursive walk that refused to descend into hidden
    // directories would lose most of a user profile -- which is exactly where a plugin looks.
    [TestMethod]
    public void Enumerate_HiddenDirectory_IsNotReturnedButIsStillWalkedThrough()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Profile", FileRecordFlags.Directory),
            new FileRecord(3, 2, "AppData", FileRecordFlags.Directory | FileRecordFlags.Hidden),
            new FileRecord(4, 3, "app.exe", FileRecordFlags.None),
        });

        var (_, results) = Enumerate(fixture, @"C:\Profile", recursive: true);

        CollectionAssert.AreEqual(new[] { @"C:\Profile\AppData\app.exe" }, Paths(results));
    }
}
