using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Search.DynamicSidebar;

namespace Lertaro.App.Tests.ViewModels.Search.DynamicSidebar;

[TestClass]
public sealed class SidebarFilterCountAccumulatorTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string ContextDirectory { get; init; } = string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestMethod]
    public void AddBatch_AccumulatesEachPredicateIndependently()
    {
        var accumulator = new SidebarFilterCountAccumulator(new Func<ISearchResult, bool>[]
        {
            result => result.IsDir,
            result => !result.IsDir
        });

        accumulator.AddBatch(new ISearchResult[]
        {
            new FakeResult { IsDir = true },
            new FakeResult { IsDir = false },
            new FakeResult { IsDir = true }
        });

        CollectionAssert.AreEqual(new[] { 2, 1 }, accumulator.Counts.ToArray());
    }

    [TestMethod]
    public void Reset_ClearsCountsBeforeTheNextQuery()
    {
        var accumulator = new SidebarFilterCountAccumulator(new Func<ISearchResult, bool>[]
        {
            result => result.IsDir
        });
        accumulator.AddBatch(new[] { (ISearchResult)new FakeResult { IsDir = true } });

        accumulator.Reset();

        Assert.AreEqual(0, accumulator.Counts[0]);
    }
}
