using Lertaro.Core;
using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QuickLaunchSourceCatalogTests
{
    [TestMethod]
    public void GetEffectiveSourceIds_UsesSavedSelectionAfterInitialization()
    {
        var settings = new QuickLaunchSettings
        {
            SourceSelectionInitialized = true,
            EnabledSourceIds = new List<string> { "source-a", "source-b" }
        };

        var ids = QuickLaunchSourceCatalog.GetEffectiveSourceIds(settings);

        CollectionAssert.AreEqual(new[] { "source-a", "source-b" }, ids.ToArray());
    }

    [TestMethod]
    public void GetDefaultSourceIds_IncludesEveryAvailableProvider()
    {
        var ids = QuickLaunchSourceCatalog.GetDefaultSourceIds(new IQuickPanelTabProvider[]
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
