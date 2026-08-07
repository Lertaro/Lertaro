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
