using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class DirectoryFilterResolverTests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "sub", FileRecordFlags.Directory),
        new FileRecord(5, 4, "deep.txt", FileRecordFlags.None),
    });

    [TestMethod]
    public void NormalizeFilter_Null_ReturnsNull() => Assert.IsNull(DirectoryFilterResolver.NormalizeFilter(null));

    [TestMethod]
    public void NormalizeFilter_Whitespace_ReturnsNull() => Assert.IsNull(DirectoryFilterResolver.NormalizeFilter("  "));

    [TestMethod]
    public void NormalizeFilter_AddsTrailingSeparatorAndLowercases() =>
        Assert.AreEqual(@"c:\projects\", DirectoryFilterResolver.NormalizeFilter(@"C:\Projects"));

    [TestMethod]
    public void ExcludesSource_MatchingDriveLetter_ReturnsFalse()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            Assert.IsFalse(DirectoryFilterResolver.ExcludesSource(snapshot, @"c:\projects\"));
            return 0;
        });
    }

    [TestMethod]
    public void ExcludesSource_DifferentDriveLetter_ReturnsTrue()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            Assert.IsTrue(DirectoryFilterResolver.ExcludesSource(snapshot, @"d:\projects\"));
            return 0;
        });
    }

    [TestMethod]
    public void ExcludesSource_TooShortToBeADriveRoot_ReturnsFalse()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            Assert.IsFalse(DirectoryFilterResolver.ExcludesSource(snapshot, "d"));
            return 0;
        });
    }

    [TestMethod]
    public void TryResolve_FullyResolvablePath_ReturnsRowWithEmptyRemainder()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"c:\projects\sub\", false, out var row, out var remainder);

            Assert.IsTrue(resolved);
            Assert.AreEqual(string.Empty, remainder);
            Assert.AreEqual("sub", snapshot.GetName(row));
            return 0;
        });
    }

    [TestMethod]
    public void TryResolve_UnknownTrailingSegment_ReturnsDeepestAncestorAsRemainder()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"c:\projects\nope\", false, out var row, out var remainder);

            Assert.IsTrue(resolved);
            Assert.AreEqual("nope", remainder);
            Assert.AreEqual("Projects", snapshot.GetName(row));
            return 0;
        });
    }

    [TestMethod]
    public void TryResolve_OverriddenDirectory_ResolvesUsingItsLiveName()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(4, 2, "renamed", FileRecordFlags.Directory, 0, 0, 0, 0));

        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"c:\projects\renamed\", false, out var row, out var remainder);

            Assert.IsTrue(resolved);
            Assert.AreEqual(string.Empty, remainder);
            Assert.AreEqual(snapshot.FirstRowForId(4), row);
            return 0;
        });
    }

    [TestMethod]
    public void TryResolve_AddedDirectoryAndNestedDirectory_ResolveByFullPath()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(101, 100, "nested", FileRecordFlags.Directory, 0, 0, 0, 0);
        });

        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"c:\projects\newdir\nested\", false, out var row, out var remainder);

            Assert.IsTrue(resolved);
            Assert.AreEqual(string.Empty, remainder);
            Assert.IsGreaterThanOrEqualTo(snapshot.Count, row);
            Assert.AreEqual("nested", DirectoryFilterResolver.GetName(snapshot, delta, row));
            return 0;
        });
    }

    [TestMethod]
    public void Enumerate_DirectoryAddedByTheOverlay_CanBeOpenedByItsFullPath()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(200, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(201, 200, "inner.txt", FileRecordFlags.None, 10, 0, 0, 0);
        });
        var results = new List<SearchResult>();

        var resolved = IndexV2Searcher.EnumerateDirectory(fixture.Index, @"C:\Projects\newdir", false, null, 0,
            results.Add, CancellationToken.None);

        Assert.IsTrue(resolved);
        CollectionAssert.AreEqual(new[] { @"C:\Projects\newdir\inner.txt" }, results.Select(r => r.Path).ToArray());
    }

    [TestMethod]
    public void TryResolve_DeletedDirectory_DoesNotResolveItsOldPath()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Remove(4));

        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"c:\projects\sub\", false, out var row, out var remainder);

            Assert.IsTrue(resolved);
            Assert.AreEqual("sub", remainder);
            Assert.AreEqual("Projects", snapshot.GetName(row));
            return 0;
        });
    }

    [TestMethod]
    public void TryResolve_PathOutsideSourceRoot_ReturnsFalse()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var resolved = DirectoryFilterResolver.TryResolve(snapshot, delta, @"d:\projects\", false, out _, out _);

            Assert.IsFalse(resolved);
            return 0;
        });
    }

    [TestMethod]
    public void IsUnderCached_DescendantOfAncestor_ReturnsTrue()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            // "deep.txt" (id 5) is under "Projects" (id 2) via "sub" (id 4).
            var result = DirectoryFilterResolver.IsUnderCached(snapshot, snapshot.FirstRowForId(5), snapshot.FirstRowForId(2), new Dictionary<int, bool>());

            Assert.IsTrue(result);
            return 0;
        });
    }

    [TestMethod]
    public void IsUnderCached_UnrelatedRow_ReturnsFalse()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            // "readme.txt" (id 3) is a direct child of "Projects" (id 2), not of "sub" (id 4).
            var result = DirectoryFilterResolver.IsUnderCached(snapshot, snapshot.FirstRowForId(3), snapshot.FirstRowForId(4), new Dictionary<int, bool>());

            Assert.IsFalse(result);
            return 0;
        });
    }

    [TestMethod]
    public void IsUnderCached_FileRowsAreNotMemoizedButTheirDirectoryIs()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, _) =>
        {
            var fileRow = snapshot.FirstRowForId(5);
            var directoryRow = snapshot.FirstRowForId(4);
            var cache = new Dictionary<int, bool>();

            Assert.IsTrue(DirectoryFilterResolver.IsUnderCached(snapshot, fileRow, snapshot.FirstRowForId(2), cache));
            Assert.IsFalse(cache.ContainsKey(fileRow));
            Assert.IsTrue(cache.TryGetValue(directoryRow, out var isUnder) && isUnder);
            return 0;
        });
    }
}
