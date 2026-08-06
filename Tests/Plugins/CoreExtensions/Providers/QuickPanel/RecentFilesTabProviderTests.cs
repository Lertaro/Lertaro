using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

// The tab reads three settings and hands them to the host's recency query. What is pinned here is that
// it asks for what the settings say, and that it asks for nothing at all when there is nothing to watch.
[TestClass]
[DoNotParallelize]
public sealed class RecentFilesTabProviderTests
{
    private (IReadOnlyList<string> Directories, int Limit, int MaxAge)? _asked;

    [TestInitialize]
    public void CaptureTheQuery()
    {
        RecentFilesService.GetRecentFilesFunc = (directories, limit, maxAge, _) =>
        {
            _asked = (directories, limit, maxAge);
            return Task.FromResult<IReadOnlyList<PluginSdk.Abstractions.ISearchResult>>(
                Array.Empty<PluginSdk.Abstractions.ISearchResult>());
        };
        PluginSettingsService.GetSettingFunc = null;
    }

    [TestCleanup]
    public void Unhook()
    {
        RecentFilesService.GetRecentFilesFunc = null;
        PluginSettingsService.GetSettingFunc = null;
    }

    // Nothing configured: the schema's own defaults are what the tab shows before anyone visits the
    // plugin's config page, which is the state most installs stay in.
    [TestMethod]
    public async Task WithNothingConfigured_AsksForTheDefaults()
    {
        await new RecentFilesTabProvider().GetEntriesAsync();

        Assert.IsNotNull(_asked);
        CollectionAssert.AreEqual(RecentFilesTabProvider.DefaultDirectories(), _asked!.Value.Directories.ToList());
        Assert.AreEqual(10, _asked!.Value.Limit);
        Assert.AreEqual(60, _asked!.Value.MaxAge);
    }

    [TestMethod]
    public async Task AsksForWhatTheSettingsSay()
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key switch
        {
            RecentFilesTabProvider.DirectoriesKey => new List<string> { @"C:\one", @"C:\two" },
            RecentFilesTabProvider.CountKey => 40,
            RecentFilesTabProvider.MaxAgeKey => 0,
            _ => fallback,
        };

        await new RecentFilesTabProvider().GetEntriesAsync();

        CollectionAssert.AreEqual(new[] { @"C:\one", @"C:\two" }, _asked!.Value.Directories.ToList());
        Assert.AreEqual(40, _asked!.Value.Limit);
        Assert.AreEqual(0, _asked!.Value.MaxAge, "0 is the settings' own way of saying no age limit");
    }

    // Nothing watched is a legitimate state, and the host drops a tab that returns nothing -- so the
    // query is never made rather than made over an empty set.
    [TestMethod]
    public async Task WithNoDirectories_AsksNothingAndReturnsNothing()
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) =>
            key == RecentFilesTabProvider.DirectoriesKey ? new List<string> { "  ", string.Empty } : fallback;

        var entries = await new RecentFilesTabProvider().GetEntriesAsync();

        Assert.IsEmpty(entries);
        Assert.IsNull(_asked, "an empty watch list is not a query worth making");
    }
}
