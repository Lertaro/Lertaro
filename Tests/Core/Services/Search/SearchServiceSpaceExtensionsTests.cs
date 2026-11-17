using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchServiceSpaceExtensionsTests
{
    [TestMethod]
    public void MergeEntries_PathlessRows_AreNotCollapsedByTheirEmptyPath()
    {
        var local = new[]
        {
            new SpaceIndexEntry(string.Empty, "Alpha", 20, true, false),
            new SpaceIndexEntry(string.Empty, "Beta", 10, true, false),
        };

        var merged = SearchServiceSpaceExtensions.MergeEntries(
            local, [], SpaceEntryFilters.SkipPathMaterialization | SpaceEntryFilters.SkipSort);

        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, merged.Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    public void MergeEntries_PathRows_StillDeduplicateAcrossLocalAndNetworkIndexes()
    {
        var local = new[]
        {
            new SpaceIndexEntry(@"C:\Projects", "Projects", 20, true, false),
        };
        var network = new[]
        {
            new SpaceIndexEntry(@"c:\projects", "Projects", 30, true, false),
        };

        var merged = SearchServiceSpaceExtensions.MergeEntries(local, network, SpaceEntryFilters.None);

        var entry = Assert.ContainsSingle(merged);
        Assert.AreEqual(20L, entry.Size);
    }
}
