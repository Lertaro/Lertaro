using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Tests.IndexV2;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class CategoryPerUniqueTests
{
    [TestMethod]
    public void CategoryFilterAnswersEveryMatchingKindAfterUniqueLevelClassification()
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "alpha.exe", FileRecordFlags.None),
            new FileRecord(3, 1, "alpha.txt", FileRecordFlags.None),
            new FileRecord(4, 1, "alpha", FileRecordFlags.Directory),
        ]);

        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Application));
        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Document));
        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Folder));
        Assert.AreEqual(3, Search(fixture, SearchCategoryFilter.All));
    }

    private static int Search(LiveIndexFixture fixture, SearchCategoryFilter category)
    {
        var count = 0;
        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            NameSearch.SearchStreaming(
                snapshot, delta, FzfPattern.Parse("alpha"),
                limit: 20, _ => count++, CancellationToken.None,
                directoryFilterLower: null, category: category);
            return null;
        });
        return count;
    }
}
