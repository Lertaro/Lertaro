using Lertaro.Core;
using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QuickLaunchSourceCatalogTests
{
    [TestMethod]
    public void GetEnabledSourceIds_ExcludesSavedDisabledSources()
    {
        var providers = new IQuickPanelTabProvider[]
        {
            new FirstSourceProvider(),
            new SecondSourceProvider()
        };
        var settings = new QuickLaunchSettings
        {
            DisabledSourceIds = new List<string> { QuickLaunchSourceCatalog.GetId(providers[1]) }
        };

        var ids = QuickLaunchSourceCatalog.GetEnabledSourceIds(settings, providers);

        CollectionAssert.AreEqual(new[] { QuickLaunchSourceCatalog.GetId(providers[0]) }, ids.ToArray());
    }

    [TestMethod]
    public void GetEnabledSourceIds_WithNoDisabledSourcesIncludesEveryProvider()
    {
        var ids = QuickLaunchSourceCatalog.GetEnabledSourceIds(new QuickLaunchSettings(), new IQuickPanelTabProvider[]
        {
            new FirstSourceProvider(),
            new SecondSourceProvider()
        });

        Assert.HasCount(2, ids);
        Assert.IsTrue(ids.Any(id => id.EndsWith("::QuickPanelTabProvider::FirstSourceProvider", StringComparison.Ordinal)));
        Assert.IsTrue(ids.Any(id => id.EndsWith("::QuickPanelTabProvider::SecondSourceProvider", StringComparison.Ordinal)));
    }

    private sealed class FirstSourceProvider : IQuickPanelTabProvider
    {
        public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());
    }

    private sealed class SecondSourceProvider : IQuickPanelTabProvider
    {
        public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());
    }
}
