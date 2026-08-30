using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

// SettingsSearchService.GetEntriesFunc, SettingsWindowService.ShowEntryFunc, and
// PluginSettingsService.GetSettingFunc are shared static
// delegates, and this provider also caches the resolved trigger word in a private static field --
// [DoNotParallelize] plus resetting everything (including busting the cache via NotifySettingChanged)
// keeps tests in this class from racing on any of that.
[TestClass]
[DoNotParallelize]
public sealed class SearchSettingsInstantProviderTests
{
    private const string PluginId = "Lertaro.Plugins.CoreExtensions";

    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        FuzzyMatchService.IsMatchFunc = null;
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
        PluginSettingsService.NotifySettingChanged(PluginId, "SearchSettingsTrigger"); // busts the cached trigger word
        SettingsSearchService.GetEntriesFunc = () => Array.Empty<SettingsSearchEntryInfo>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        FuzzyMatchService.IsMatchFunc = null;
        SettingsWindowService.ShowEntryFunc = null;
        SettingsWindowService.ShowWindowFunc = null;
    }

    private static void ConfigureEntries(params SettingsSearchEntryInfo[] entries) =>
        SettingsSearchService.GetEntriesFunc = () => entries;

    [TestMethod]
    public void GetInstantResults_QueryWithoutTriggerPrefix_ReturnsNothing()
    {
        ConfigureEntries(new SettingsSearchEntryInfo("Dark mode", "Appearance", 0));

        Assert.IsEmpty(new SearchSettingsInstantProvider().GetInstantResults("dark"));
    }

    [TestMethod]
    public void GetInstantResults_TriggerWithNoTerm_ListsEveryEntry()
    {
        ConfigureEntries(
            new SettingsSearchEntryInfo("Dark mode", "Appearance", 0),
            new SettingsSearchEntryInfo("Hotkeys", "General", 1));

        var results = new SearchSettingsInstantProvider().GetInstantResults("set ").ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void GetInstantResults_TriggerIsCaseInsensitive()
    {
        ConfigureEntries(new SettingsSearchEntryInfo("Dark mode", "Appearance", 0));

        Assert.HasCount(1, new SearchSettingsInstantProvider().GetInstantResults("SET ").ToList());
    }

    [TestMethod]
    public void GetInstantResults_FilteredQuery_ReturnsAllMatchingEntries()
    {
        FuzzyMatchService.IsMatchFunc = (_, _) => true;
        ConfigureEntries(Enumerable.Range(0, 12)
            .Select(index => new SettingsSearchEntryInfo($"Dark mode {index}", "Appearance", index))
            .ToArray());

        var results = new SearchSettingsInstantProvider().GetInstantResults("set dark").ToList();

        Assert.HasCount(12, results);
    }

    [TestMethod]
    public void GetInstantResults_SelectionNotifiesHostWithEntry()
    {
        ConfigureEntries(new SettingsSearchEntryInfo("Dark mode", "Appearance", 42));
        SettingsSearchEntryInfo? selected = null;
        SettingsWindowService.ShowEntryFunc = entry =>
        {
            selected = entry;
            return true;
        };

        var result = new SearchSettingsInstantProvider().GetInstantResults("set ").Single();

        result.OnExecute?.Invoke();
        Assert.IsNotNull(selected);
        Assert.AreEqual(42, selected.Index);
        Assert.AreEqual("None", result.ActionType);
        Assert.IsEmpty(result.ActionArgument);
        Assert.AreEqual("Dark mode", result.Title);
        Assert.AreEqual("Appearance", result.Description);
    }

    [TestMethod]
    public void GetHighlightMask_QueryWithoutTriggerPrefix_ReturnsNull() =>
        Assert.IsNull(new SearchSettingsInstantProvider().GetHighlightMask("Dark mode", "dark"));

    [TestMethod]
    public void GetHighlightMask_TriggerWithNoTerm_ReturnsAllFalseMask()
    {
        var mask = new SearchSettingsInstantProvider().GetHighlightMask("Dark mode", "set ");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }
}
