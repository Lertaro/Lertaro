using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Filters;

[TestClass]
public sealed class DateModifiedFilterProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
        public FileMetadata Metadata { get; init; }
    }

    private static Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>> GetPredicate(string id)
    {
        var provider = new DateModifiedFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).FilterPredicate!;
    }

    private static FakeResult WithModified(DateTime modified) => new() { Metadata = new FileMetadata(0, default, modified, default) };

    [TestMethod]
    public async Task Date1_RecentFile_IsIncluded()
    {
        var predicate = GetPredicate("Date_1");
        var results = new ISearchResult[] { WithModified(DateTime.Now) };

        var filtered = await predicate(results);

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task Date1_FileOlderThanOneDay_IsExcluded()
    {
        var predicate = GetPredicate("Date_1");
        var results = new ISearchResult[] { WithModified(DateTime.Now.AddDays(-2)) };

        var filtered = await predicate(results);

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task Date7_FileFourDaysOld_IsIncludedButExcludedFromDate1()
    {
        var result = WithModified(DateTime.Now.AddDays(-4));

        var withinWeek = await GetPredicate("Date_7")(new[] { result });
        var withinDay = await GetPredicate("Date_1")(new[] { result });

        Assert.HasCount(1, withinWeek);
        Assert.IsEmpty(withinDay);
    }

    [TestMethod]
    public async Task Date365_UnknownMetadata_IsExcluded()
    {
        var predicate = GetPredicate("Date_365");
        var results = new ISearchResult[] { WithModified(DateTime.MinValue) };

        var filtered = await predicate(results);

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public void GetFilterGroups_ReturnsAllFourDateRanges()
    {
        var provider = new DateModifiedFilterProvider();

        var ids = provider.GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "Date_1", "Date_7", "Date_30", "Date_365" }, ids);
    }
}
