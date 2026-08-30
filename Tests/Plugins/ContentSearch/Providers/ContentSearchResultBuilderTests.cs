using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Providers;

namespace Lertaro.Plugins.ContentSearch.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class ContentSearchResultBuilderTests
{
    [TestInitialize]
    public void SetUp()
    {
        SettingsSearchService.GetEntriesFunc = () => Array.Empty<SettingsSearchEntryInfo>();
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
        TranslationService.LookupFunc = key => key == "ContentSearch_PluginName" ? "Content Search" : $"[{key}]";
    }

    [TestCleanup]
    public void TearDown()
    {
        SettingsSearchService.GetEntriesFunc = () => Array.Empty<SettingsSearchEntryInfo>();
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
        TranslationService.LookupFunc = key => $"[{key}]";
    }

    [TestMethod]
    public void CreatePlaceholderItem_ExecutesEntryNavigationThroughSdk()
    {
        var entry = new SettingsSearchEntryInfo("Enable indexing", "Plugins › Content Search › Configuration › General", 7);
        SettingsSearchService.GetEntriesFunc = () => [entry];
        SettingsSearchEntryInfo? selected = null;
        SettingsWindowService.ShowEntryFunc = item =>
        {
            selected = item;
            return true;
        };

        var result = ContentSearchResultBuilder.CreatePlaceholderItem(10, false);

        result.OnExecute?.Invoke();

        Assert.IsNotNull(selected);
        Assert.AreEqual(7, selected.Index);
        Assert.AreEqual("None", result.ActionType);
        Assert.IsEmpty(result.ActionArgument);
    }

    [TestMethod]
    public void CreatePlaceholderItem_WithoutEntryShowsPluginSettingsSection()
    {
        string? selectedSection = null;
        SettingsWindowService.ShowWindowFunc = section =>
        {
            selectedSection = section;
            return true;
        };

        var result = ContentSearchResultBuilder.CreatePlaceholderItem(10, false);

        result.OnExecute?.Invoke();

        Assert.AreEqual("Plugins", selectedSection);
    }
}
