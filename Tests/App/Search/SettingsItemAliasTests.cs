using System.Text.Json;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.SystemSettings;

namespace Lertaro.App.Tests.Search;

[TestClass]
public sealed class SettingsItemAliasTests
{
    private const string SystemTitle = "编辑系统环境变量";
    private const string UserTitle = "编辑账户的环境变量";
    private const string SystemPath = "::{ED7BA470-8E54-465E-825C-99712043E01C}\\{E2394C16-F45A-496F-83CC-49E163281662}";
    private const string UserPath = "::{ED7BA470-8E54-465E-825C-99712043E01C}\\{37092408-D49C-451D-B56D-78B243DC475C}";

    [TestMethod]
    [DataRow("env")]
    [DataRow("ENV")]
    [DataRow("environment")]
    public void MatchCatalogEntry_DefaultEnvironmentKeyword_ReturnsBothLocalizedTasks(string query)
    {
        var items = new[]
        {
            SystemSettingsItemProvider.CreateItem(SystemTitle, SystemPath, ""),
            SystemSettingsItemProvider.CreateItem(UserTitle, UserPath, ""),
            SystemSettingsItemProvider.CreateItem("事件查看器", "other-task", "")
        };
        var matches = items.Where(item => SearchableItemMapper.MatchCatalogEntry(
            FuzzyQuery.Parse(query), item, [], new HashSet<string>()).IsMatch).ToList();
        Assert.HasCount(2, matches);
        Assert.AreEqual(SystemPath, matches[0].Id);
        Assert.AreEqual(UserPath, matches[1].Id);
        Assert.IsNotNull(matches[0].OnExecute);
        Assert.IsNotNull(matches[1].OnExecute);
    }

    [TestMethod]
    public void MatchCatalogEntry_DefaultKeyword_UsesTaskIdentityAfterLanguageChange()
    {
        var item = SystemSettingsItemProvider.CreateItem("任意本地化标题", SystemPath, "");
        var match = SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse("env"), item, [], new HashSet<string>());
        Assert.AreEqual(new MatchRank(MatchRank.TierName, 0, 1), match);
    }

    [TestMethod]
    public void MatchCatalogEntry_SavedAndReloadedSharedAlias_MatchesBothTargets()
    {
        var settings = new UserSettings();
        var editor = new SettingsItemAliasViewModel(settings);
        editor.Items.Add(new SettingsItemAliasItem("tools", SystemTitle));
        editor.Items.Add(new SettingsItemAliasItem("TOOLS", UserTitle));
        editor.Save();
        var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        var targets = SearchableItemMapper.FindAliasTargets("tools", restored.SettingsItemAliases);
        var matches = new[] { SystemTitle, UserTitle, "事件查看器" }
            .Where(title => SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse("tools"),
                new SearchableItem { Title = title }, [], targets).IsMatch).ToList();
        Assert.HasCount(2, matches);
        Assert.Contains(SystemTitle, matches);
        Assert.Contains(UserTitle, matches);
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("nv")]
    [DataRow("env extra")]
    public void MatchCatalogEntry_PartialKeyword_DoesNotMatch(string query)
    {
        var item = SystemSettingsItemProvider.CreateItem(SystemTitle, SystemPath, "");
        var targets = SearchableItemMapper.FindAliasTargets(query,
            new Dictionary<string, List<string>> { ["env"] = [SystemTitle] });
        Assert.IsFalse(SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse(query), item, [], targets).IsMatch);
    }

    [TestMethod]
    public void MatchCatalogEntry_StableIdAlias_MatchesRenamedItem()
    {
        var targets = SearchableItemMapper.FindAliasTargets("custom",
            new Dictionary<string, List<string>> { ["custom"] = [SystemPath] });
        var item = SystemSettingsItemProvider.CreateItem("Changed name", SystemPath, "");
        Assert.IsTrue(SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse("custom"), item, [], targets).IsMatch);
    }

    [TestMethod]
    public void MatchCatalogEntry_TitleContainingQueryOperators_StillMatchesAlias()
    {
        var item = new SearchableItem { Title = "!Something | other" };
        var targets = SearchableItemMapper.FindAliasTargets("custom",
            new Dictionary<string, List<string>> { ["custom"] = [item.Title] });
        Assert.IsTrue(SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse("custom"), item, [], targets).IsMatch);
    }

    [TestMethod]
    public void MatchCatalogEntry_OrdinaryTransliteration_KeepsItsRank()
    {
        var item = new SearchableItem { Title = "环境" };
        var query = FuzzyQuery.Parse("huanjing");
        var aliases = new List<string> { "huanjing" };
        Assert.AreEqual(query.BestMatch(item.Title, aliases),
            SearchableItemMapper.MatchCatalogEntry(query, item, aliases, new HashSet<string>()));
    }
}
