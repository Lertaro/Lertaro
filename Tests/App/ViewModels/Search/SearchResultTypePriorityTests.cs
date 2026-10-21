using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// The quick window resolves a candidate's display type rank for every row it maps, so the dictionary form
// has to agree with the list form it replaced -- including the two details that decide real rankings: the
// first occurrence wins, and an id that is not listed at all sorts after every listed one.
[TestClass]
public sealed class SearchResultTypePriorityTests
{
    [TestMethod]
    public void BuildRanks_MatchesTheListOverload()
    {
        var orders = new[]
        {
            new List<string>(),
            ["apps", "files", "apps"],
            [SearchResultTypePriority.FilesTypeId, "apps"],
            ["settings"],
        };

        foreach (var order in orders)
        {
            var ranks = SearchResultTypePriority.BuildRanks(order);
            foreach (var typeId in order.Append("unlisted").Append(SearchResultTypePriority.FilesTypeId))
            {
                Assert.AreEqual(
                    SearchResultTypePriority.Rank(typeId, order),
                    SearchResultTypePriority.Rank(typeId, ranks),
                    $"{typeId} in [{string.Join(", ", order)}]");
            }
        }
    }

    [TestMethod]
    public void BuildRanks_FirstOccurrenceWins()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["apps", "files", "apps"]);

        Assert.AreEqual(0, SearchResultTypePriority.Rank("apps", ranks));
        Assert.AreEqual(1, SearchResultTypePriority.Rank("files", ranks));
    }

    [TestMethod]
    public void Rank_UnknownTypeIsMaxValue()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["apps"]);

        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.Rank("nope", ranks));
    }

    [TestMethod]
    public void Rank_CaseSensitiveLikeTheOldIndexOf()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["Apps"]);

        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.Rank("apps", ranks));
    }

    // An empty order is the inline window's shape: no type layer at all, every row tied.
    [TestMethod]
    public void Rank_EmptyOrderPutsEverythingLast() => Assert.AreEqual(
            int.MaxValue,
            SearchResultTypePriority.Rank(SearchResultTypePriority.FilesTypeId, SearchResultTypePriority.BuildRanks([])));
}
