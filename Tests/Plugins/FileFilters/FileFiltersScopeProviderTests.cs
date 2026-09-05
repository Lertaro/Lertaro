using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters.Tests;

// PluginSettingsService.GetSettingFunc (and the SettingChanged event) is process-wide static state,
// hence [DoNotParallelize] plus a reset in TestInitialize AND TestCleanup -- a previous failed run
// must not leak state into the first test here. The provider touches no filesystem at all: it maps
// the configured filters into search scopes and caches them until the "Filters" setting changes.
// What is left to test is exactly that mapping and the cache invalidation.
[TestClass]
[DoNotParallelize]
public sealed class FileFiltersScopeProviderTests
{
    private const string PluginId = "Lertaro.Plugins.FileFilters";

    private int _settingReads;

    [TestInitialize]
    public void ResetBefore() => Reset();

    [TestCleanup]
    public void ResetAfter() => Reset();

    private void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        _settingReads = 0;
    }

    private void ConfigureFilters(List<FileFiltersScopeProvider.FilterItem> filters) => PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
                                                                                             {
                                                                                                 _settingReads++;
                                                                                                 return pluginId == PluginId && key == "Filters" ? filters : defaultValue;
                                                                                             };

    [TestMethod]
    public void NoConfiguredFilters_ReturnsNoScopes()
    {
        ConfigureFilters(new());
        var provider = new FileFiltersScopeProvider();

        Assert.IsEmpty(provider.GetSearchScopes());
    }

    [TestMethod]
    public void HostAnswersNull_ReturnsNoScopes()
    {
        PluginSettingsService.GetSettingFunc = (_, _, defaultValue) => defaultValue;
        var provider = new FileFiltersScopeProvider();

        Assert.IsEmpty(provider.GetSearchScopes());
    }

    [TestMethod]
    public void DisabledFilter_ProducesNoScope()
    {
        ConfigureFilters(new() { new() { Enabled = false, Keyword = "tf", Folders = { @"C:\Movies" } } });
        var provider = new FileFiltersScopeProvider();

        Assert.IsEmpty(provider.GetSearchScopes());
    }

    [TestMethod]
    public void KeywordlessFilter_ProducesNoScope()
    {
        ConfigureFilters(new() { new() { Keyword = "", Folders = { @"C:\Movies" } } });
        var provider = new FileFiltersScopeProvider();

        Assert.IsEmpty(provider.GetSearchScopes());
    }

    [TestMethod]
    public void FilterWithOnlyBlankFolders_ProducesNoScope()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { "", "  " } } });
        var provider = new FileFiltersScopeProvider();

        Assert.IsEmpty(provider.GetSearchScopes());
    }

    [TestMethod]
    public void ConfiguredFilter_MapsToScope_WithKeywordFoldersAndPattern()
    {
        ConfigureFilters(new()
        {
            new() { Keyword = " TF ", Folders = { @"C:\Movies", @" d:\books " }, FilterPattern = " *.exe; *.lnk " }
        });
        var provider = new FileFiltersScopeProvider();

        var scope = provider.GetSearchScopes().Single();

        Assert.AreEqual("TF", scope.Keyword);
        CollectionAssert.AreEquivalent(new[] { @"C:\Movies", @"d:\books" }, scope.Folders.ToList());
        Assert.AreEqual(" *.exe; *.lnk ", scope.FilterPattern, "the pattern is passed through verbatim; the host's matcher owns its semantics");
    }

    [TestMethod]
    public void BlankPattern_BecomesMatchAll()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies" }, FilterPattern = "  " } });
        var provider = new FileFiltersScopeProvider();

        Assert.AreEqual("*", provider.GetSearchScopes().Single().FilterPattern);
    }

    [TestMethod]
    public void DuplicateFolders_Collapse()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies", @"c:\movies" } } });
        var provider = new FileFiltersScopeProvider();

        Assert.HasCount(1, provider.GetSearchScopes().Single().Folders);
    }

    [TestMethod]
    public void RepeatedReads_AreCached()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies" } } });
        var provider = new FileFiltersScopeProvider();

        _ = provider.GetSearchScopes();
        _ = provider.GetSearchScopes();

        Assert.AreEqual(1, _settingReads, "GetSearchScopes runs per keystroke, so the config must be cached between changes");
    }

    [TestMethod]
    public void FiltersSettingChanged_InvalidatesCache()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies" } } });
        var provider = new FileFiltersScopeProvider();
        _ = provider.GetSearchScopes();

        ConfigureFilters(new() { new() { Keyword = "doc", Folders = { @"D:\Docs" } } });
        PluginSettingsService.NotifySettingChanged(PluginId, "Filters");
        var scopes = provider.GetSearchScopes();

        Assert.AreEqual("doc", scopes.Single().Keyword);
    }

    [TestMethod]
    public void UnrelatedSettingChanged_DoesNotInvalidateCache()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies" } } });
        var provider = new FileFiltersScopeProvider();
        _ = provider.GetSearchScopes();

        PluginSettingsService.NotifySettingChanged("Lertaro.Plugins.SomethingElse", "Filters");
        PluginSettingsService.NotifySettingChanged(PluginId, "OtherKey");
        _ = provider.GetSearchScopes();

        Assert.AreEqual(1, _settingReads);
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromSettingChanges()
    {
        ConfigureFilters(new() { new() { Keyword = "tf", Folders = { @"C:\Movies" } } });
        using var provider = new FileFiltersScopeProvider();
        _ = provider.GetSearchScopes();

        ConfigureFilters(new() { new() { Keyword = "doc", Folders = { @"D:\Docs" } } });
        provider.Dispose();
        PluginSettingsService.NotifySettingChanged(PluginId, "Filters");

        Assert.AreEqual("tf", provider.GetSearchScopes().Single().Keyword);
    }
}
