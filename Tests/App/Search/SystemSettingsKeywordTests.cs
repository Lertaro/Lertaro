using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.SystemSettings;

namespace Lertaro.App.Tests.Search;

[TestClass]
public sealed class SystemSettingsKeywordTests
{
    [TestMethod]
    public void ParseKeywords_ShellString_TrimsAndDeduplicatesWithoutSplittingPhrases() => CollectionAssert.AreEqual(new[] { "环境变量", "environment variables", "envvar" },
            SystemSettingsItemProvider.ParseKeywords(" 环境变量 ;environment variables;envvar;ENVVAR;; "));

    [TestMethod]
    public void ParseKeywords_SafeArray_CombinesStringValues() => CollectionAssert.AreEqual(new[] { "cpu", "running processes", "threads" },
            SystemSettingsItemProvider.ParseKeywords(new object[] { "cpu;running processes", "threads", 42 }));

    [TestMethod]
    public void ParseKeywords_MissingProperty_LeavesItemSearchableByTitle()
    {
        Assert.IsEmpty(SystemSettingsItemProvider.ParseKeywords(null));
        var entry = Entry("System settings", []);
        Assert.AreEqual(FuzzyQuery.Parse("System").BestMatch(entry.Item.Title), Match("System", entry));
    }

    [TestMethod]
    [DataRow("env", true)]
    [DataRow("ENV VAR", true)]
    [DataRow("环境变", true)]
    [DataRow("vironment", false)]
    [DataRow("env missing", false)]
    [DataRow("", false)]
    public void NativeKeywords_MatchAllWordPrefixes(string query, bool expected)
    {
        var entry = Entry("编辑账户的环境变量", ["environment", "variables", "环境变量", "envvar"]);
        Assert.AreEqual(expected, Match(query, entry).IsMatch);
    }

    [TestMethod]
    public void NativeKeywords_QueryCanCombineTitleAndKeywordWords()
    {
        var entry = Entry("See running processes", ["cpu", "threads"]);
        Assert.IsTrue(Match("CPU run", entry).IsMatch);
        Assert.IsFalse(Match("cpu level", entry).IsMatch);
        Assert.AreEqual(1d, Match("cpu", entry).Weight);
    }

    [TestMethod]
    public void NativeKeywords_CleanPrefixIsExactIdentity()
    {
        var entry = Entry("编辑账户的环境变量", ["environment", "variables", "envvar"]);

        var match = SearchableItemMapper.MatchCatalogEntryWithEvidence(
            FuzzyQuery.Parse("env"), ["env"], entry, "Settings");

        Assert.AreEqual(MatchEvidenceField.Keyword, match.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, match.Evidence.Grade);
        Assert.IsTrue(match.Rank.IsStrong);
        Assert.AreEqual(0.5d, match.Rank.Coverage);
    }

    [TestMethod]
    public void NativeKeywords_ShortLatinPrefixStaysSecondary()
    {
        var entry = Entry("编辑账户的环境变量", ["environment", "envvar"]);

        var match = SearchableItemMapper.MatchCatalogEntryWithEvidence(
            FuzzyQuery.Parse("e"), ["e"], entry, "Settings");

        Assert.AreEqual(MatchEvidenceField.Keyword, match.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, match.Evidence.Grade);
    }

    [TestMethod]
    public void SharedKeywords_FindBothEnvironmentTasksWithoutChangingTitleRanking()
    {
        var entries = new[] { Entry("编辑账户的环境变量", ["environment"]), Entry("编辑系统环境变量", ["environment"]) };
        Assert.AreEqual(2, entries.Count(entry => Match("env", entry).IsMatch));
        Assert.AreEqual(FuzzyQuery.Parse(entries[0].Item.Title).BestMatch(entries[0].Item.Title),
            Match(entries[0].Item.Title, entries[0]));
    }

    [TestMethod]
    public void EvidenceNamesTheRepresentationThatActuallyMatched()
    {
        var aliasEntry = new SearchableItemCache.CacheEntry(
            new SearchableItem { Title = "PixPin" },
            ["screen capture"],
            null);
        var alias = SearchableItemMapper.MatchCatalogEntryWithEvidence(
            FuzzyQuery.Parse("capture"), ["capture"], aliasEntry, "StartMenu");
        var keyword = SearchableItemMapper.MatchCatalogEntryWithEvidence(
            FuzzyQuery.Parse("env"), ["env"], Entry("System Settings", ["environment"]), "Settings");

        Assert.AreEqual(MatchEvidenceField.ProviderAlias, alias.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, alias.Evidence.Grade);
        Assert.AreEqual("StartMenu", alias.Evidence.ProviderId);
        Assert.AreEqual(MatchEvidenceField.Keyword, keyword.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, keyword.Evidence.Grade);
        Assert.AreEqual(MatchEvidenceMode.LegacyUnknown, alias.Evidence.Mode);
    }

    [TestMethod]
    public void SyntheticPath_UsesTheProviderTypeName_SoTheStockDemotionWorksInEveryLanguage()
    {
        var provider = new SystemSettingsItemProvider();
        var path = SearchableItemMapper.SyntheticPath(provider, "查看网络连接");

        Assert.AreEqual("__SEARCHABLE_ITEM__:" + nameof(SystemSettingsItemProvider) + ":查看网络连接", path);
        var rules = ExclusionRuleSet.From(new UserSettings());
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(path, isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Low,
            rules.GetPriority(SearchableItemMapper.SyntheticPath(provider, "View network connections"), isDirectory: false));
    }

    private static SearchableItemCache.CacheEntry Entry(string title, string[] keywords) =>
        new(new SearchableItem { Title = title, Keywords = keywords }, [], null);

    private static MatchRank Match(string query, SearchableItemCache.CacheEntry entry) =>
        SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse(query),
            query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), entry);
}
