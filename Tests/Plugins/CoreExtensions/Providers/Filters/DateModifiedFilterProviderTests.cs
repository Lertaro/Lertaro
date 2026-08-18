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

    private static Func<ISearchResult, bool> GetPredicate(string id)
    {
        var provider = new DateModifiedFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).MatchPredicate;
    }

    private static FakeResult WithModified(DateTime modified) => new() { Metadata = new FileMetadata(0, default, modified, default) };

    [TestMethod]
    public void Date1_RecentFile_IsIncluded()
    {
        var predicate = GetPredicate("Date_1");
        Assert.IsTrue(predicate(WithModified(DateTime.Now)));
    }

    [TestMethod]
    public void Date1_FileOlderThanOneDay_IsExcluded()
    {
        var predicate = GetPredicate("Date_1");
        Assert.IsFalse(predicate(WithModified(DateTime.Now.AddDays(-2))));
    }

    [TestMethod]
    public void Date7_FileFourDaysOld_IsIncludedButExcludedFromDate1()
    {
        var result = WithModified(DateTime.Now.AddDays(-4));

        Assert.IsTrue(GetPredicate("Date_7")(result));
        Assert.IsFalse(GetPredicate("Date_1")(result));
    }

    [TestMethod]
    public void Date365_UnknownMetadata_IsExcluded()
    {
        var predicate = GetPredicate("Date_365");
        Assert.IsFalse(predicate(WithModified(DateTime.MinValue)));
    }

    [TestMethod]
    public void GetFilterGroups_ReturnsAllFourDateRanges()
    {
        var provider = new DateModifiedFilterProvider();

        var ids = provider.GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "Date_1", "Date_7", "Date_30", "Date_365" }, ids);
    }
}
