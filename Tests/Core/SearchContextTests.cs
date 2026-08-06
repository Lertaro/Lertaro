namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchContextTests
{
    [TestInitialize]
    [TestCleanup]
    public void ResetContext() => SearchContext.DisabledAliasIds = null;

    [TestMethod]
    public void DisabledAliasIds_DefaultsToNull() => Assert.IsNull(SearchContext.DisabledAliasIds);

    [TestMethod]
    public void DisabledAliasIds_SetThenGet_RoundTrips()
    {
        var ids = new HashSet<byte> { 1, 2, 3 };

        SearchContext.DisabledAliasIds = ids;

        Assert.AreSame(ids, SearchContext.DisabledAliasIds);
    }

    [TestMethod]
    public async Task DisabledAliasIds_FlowsIntoChildAsyncContext()
    {
        var ids = new HashSet<byte> { 5 };
        SearchContext.DisabledAliasIds = ids;

        HashSet<byte>? seenInChildContext = null;
        await Task.Run(() => seenInChildContext = SearchContext.DisabledAliasIds);

        Assert.AreSame(ids, seenInChildContext);
    }

    [TestMethod]
    public async Task DisabledAliasIds_ChildContextMutation_DoesNotFlowBackToParent()
    {
        SearchContext.DisabledAliasIds = new HashSet<byte> { 1 };

        await Task.Run(() => SearchContext.DisabledAliasIds = new HashSet<byte> { 99 });

        CollectionAssert.AreEqual(new byte[] { 1 }, SearchContext.DisabledAliasIds!.ToArray());
    }
}
