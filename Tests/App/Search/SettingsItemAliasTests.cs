using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.Search;

// The alias table is a whole-word lookup the user edits by hand, so its two failure modes are the ones
// worth pinning: a word that only partially equals an alias must not resolve (otherwise typing would
// start hijacking the ordinary search), and a row the editor saved with a blank target must not resolve
// to an empty name.
[TestClass]
public sealed class SettingsItemAliasTests
{
    private const string EnvItem = "编辑系统环境变量";

    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["env"] = EnvItem,
        ["log"] = "事件查看器",
        ["dash"] = "  ",
    };

    [TestMethod]
    [DataRow("env", "编辑系统环境变量")]
    [DataRow("ENV", "编辑系统环境变量")]
    [DataRow("log", "事件查看器")]
    public void FindAliasTarget_WholeWordQuery_ReturnsTarget(string query, string expected) =>
        Assert.AreEqual(expected, SearchableItemMapper.FindAliasTarget(query, Table));

    [TestMethod]
    [DataRow("en")]
    [DataRow("environment")]
    [DataRow("nv")]
    [DataRow("log ")]
    public void FindAliasTarget_NotTheWholeWord_ReturnsNull(string query) =>
        Assert.IsNull(SearchableItemMapper.FindAliasTarget(query, Table));

    [TestMethod]
    public void FindAliasTarget_BlankTarget_ReturnsNull() =>
        Assert.IsNull(SearchableItemMapper.FindAliasTarget("dash", Table));

    [TestMethod]
    public void FindAliasTarget_EmptyTable_ReturnsNull() =>
        Assert.IsNull(SearchableItemMapper.FindAliasTarget("env", new Dictionary<string, string>()));

    // An alias hit is worth exactly what typing the item's own name is worth, since that is the only
    // comparison the merged ranking knows how to make.
    [TestMethod]
    public void MatchCatalogEntry_AliasResolvesToThisTitle_RanksLikeTheNameItself()
    {
        var aliasQuery = FuzzyQuery.Parse(EnvItem);

        var match = SearchableItemMapper.MatchCatalogEntry(
            FuzzyQuery.Parse("env"), EnvItem, new List<string>(), aliasQuery, EnvItem);

        Assert.IsTrue(match.IsMatch);
        Assert.AreEqual(0, match.Start);
        Assert.AreEqual(FuzzyQuery.Parse(EnvItem).BestMatch(EnvItem).Weight, match.Weight);
    }

    [TestMethod]
    public void MatchCatalogEntry_AliasResolvesToAnotherTitle_DoesNotMatch() =>
        Assert.IsFalse(SearchableItemMapper.MatchCatalogEntry(
            FuzzyQuery.Parse("env"), "事件查看器", new List<string>(), FuzzyQuery.Parse(EnvItem), EnvItem).IsMatch);

    // The alias must not rewrite a candidate the query already reached: its own rank is what the rest of
    // the results are ordered against.
    [TestMethod]
    public void MatchCatalogEntry_TitleAlreadyMatches_KeepsTheOrdinaryRank()
    {
        var query = FuzzyQuery.Parse("记事本");
        var title = "记事本";
        var expected = query.BestMatch(title, new List<string>());

        var match = SearchableItemMapper.MatchCatalogEntry(query, title, new List<string>(), FuzzyQuery.Parse(EnvItem), EnvItem);

        Assert.AreEqual(expected, match);
    }

    [TestMethod]
    public void MatchCatalogEntry_NoAliasConfigured_BehavesLikeAPlainScan()
    {
        var query = FuzzyQuery.Parse("env");
        var aliases = new List<string> { "环境变量" };

        Assert.AreEqual(query.BestMatch(EnvItem, aliases),
            SearchableItemMapper.MatchCatalogEntry(query, EnvItem, aliases, null, null));
    }

    // What the transliterating providers hand in still decides a match the title alone cannot: nothing in
    // 环境 is Latin, so only the reading registered for it can answer this query.
    [TestMethod]
    public void MatchCatalogEntry_ProviderAliasStillWorks()
    {
        var match = SearchableItemMapper.MatchCatalogEntry(
            FuzzyQuery.Parse("huanjing"), "环境", new List<string> { "huanjing" }, null, null);

        Assert.IsTrue(match.IsMatch);
    }
}
