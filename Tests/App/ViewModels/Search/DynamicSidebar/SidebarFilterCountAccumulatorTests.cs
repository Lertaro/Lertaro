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
    public void Calculate_AppliesOtherGroupsAndIgnoresTargetGroupSelection()
    {
        var accumulator = new SidebarFilterCountAccumulator(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            new Func<ISearchResult, bool>[]
            {
                result => result.IsDir,
                result => !result.IsDir
            },
            new Func<ISearchResult, bool>[]
            {
                result => result.Name == "a",
                result => result.Name == "b"
            }
        });

        accumulator.AddBatch(new ISearchResult[]
        {
            new FakeResult { IsDir = true, Name = "a" },
            new FakeResult { IsDir = true, Name = "b" },
            new FakeResult { IsDir = false, Name = "a" },
            new FakeResult { IsDir = false, Name = "b" },
            new FakeResult { IsDir = false, Name = "c" }
        });

        accumulator.Calculate(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            Array.Empty<Func<ISearchResult, bool>>(),
            new Func<ISearchResult, bool>[] { result => result.Name == "a" }
        });

        CollectionAssert.AreEqual(new[] { 1, 1, 2, 2 }, accumulator.Counts.ToArray());

        accumulator.Calculate(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            new Func<ISearchResult, bool>[] { result => result.IsDir },
            new Func<ISearchResult, bool>[] { result => result.Name == "a" }
        });

        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, accumulator.Counts.ToArray());
    }

    [TestMethod]
    public void Reset_ClearsCountsBeforeTheNextQuery()
    {
        var accumulator = new SidebarFilterCountAccumulator(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            new Func<ISearchResult, bool>[] { result => result.IsDir }
        });
        accumulator.AddBatch(new[] { (ISearchResult)new FakeResult { IsDir = true } });

        accumulator.Reset();
        accumulator.Calculate(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            Array.Empty<Func<ISearchResult, bool>>()
        });

        Assert.AreEqual(0, accumulator.Counts[0]);
    }

    [TestMethod]
    public void ReplaceResults_DropsRowsRemovedByFinalSearchFiltering()
    {
        var accumulator = new SidebarFilterCountAccumulator(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            new Func<ISearchResult, bool>[] { result => !result.IsDir }
        });

        accumulator.AddBatch(new ISearchResult[]
        {
            new FakeResult { IsDir = false },
            new FakeResult { IsDir = false }
        });
        accumulator.ReplaceResults(new ISearchResult[]
        {
            new FakeResult { IsDir = false }
        });

        accumulator.Calculate(new IReadOnlyList<Func<ISearchResult, bool>>[]
        {
            Array.Empty<Func<ISearchResult, bool>>()
        });

        Assert.AreEqual(1, accumulator.Counts[0]);
    }
}
