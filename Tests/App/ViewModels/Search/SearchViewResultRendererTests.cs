using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchViewResultRendererTests
{
    [TestMethod]
    public void Render_LimitFollowsRankingAndFilteringWithoutDiscardingCandidates()
    {
        var accumulator = new StreamingResultAccumulator(@"Z:\Archive\ /report/", resolveQueriedDirectory: _ => null);
        accumulator.AbsorbBatch(Enumerable.Range(0, 8000).Select(i => new SearchResult
        {
            Name = $"report{i:D4}.txt", Path = $@"Z:\Archive\report{i:D4}.txt", RankSortKey = ulong.MaxValue - 100
        }).ToArray());
        var rows = accumulator.AbsorbBatch([new() { Name = "report-best.txt", Path = @"Z:\Archive\report-best.txt", RankSortKey = 1 }]);
        var visible = new ObservableRangeCollection<AppSearchResult>();
        var exceeded = false;
        var count = 0;
        var renderer = new SearchViewResultRenderer(visible, () => false, _ => 0, n => count = n, () => { },
            () => 8000, value => exceeded = value);
        renderer.Render(rows);
        Assert.HasCount(8001, rows);
        Assert.AreEqual(8000, count);
        Assert.IsTrue(exceeded);
        Assert.IsTrue(visible.Any(r => r.Name == "report-best.txt"));

        // A token/sidebar may retain only the late row, or a column sort may put a former tail first.
        renderer.Render(rows.Where(r => r.Name == "report-best.txt").ToList());
        Assert.AreEqual("report-best.txt", Assert.ContainsSingle(visible).Name);
        Assert.IsFalse(exceeded);
        var reversed = rows.AsEnumerable().Reverse().ToList();
        renderer.Render(reversed);
        Assert.AreSame(reversed[0], visible[0]);
        Assert.AreEqual(8000, count);
    }
}
