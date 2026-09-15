using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Helpers;

// The query-token trigger character is shared: the app-wide prefix, each plugin's own prefix, and the
// fixed sort/filter triggers all start a token, and the scanner resolves a shared character by whichever
// provider is asked first. That is silent, so these rules exist to surface the collision instead.
//
// Only the DECISION is covered here. Which prefixes exist is discovered by walking the loaded plugin
// assemblies' schemas, which is filesystem/assembly-bound with no injectable seam -- the candidate list
// is handed to FindOwner precisely so the decision can be tested without any of that.
[TestClass]
public sealed class QueryTokenPrefixRulesTests
{
    private static PrefixOwner Owned(string pluginId, string prefix)
        => new(pluginId, "Prefix", prefix);

    [TestMethod]
    public void FindOwner_PrefixMatchesAnExistingOwner_ReturnsThatOwner()
    {
        var candidates = new[] { Owned("Lertaro.Plugins.CoreExtensions", "\\") };

        var owner = QueryTokenPrefixRules.FindOwner('\\', candidates, null, null);

        Assert.IsNotNull(owner);
        Assert.AreEqual("Lertaro.Plugins.CoreExtensions", owner.Value.PluginId);
    }

    [TestMethod]
    public void FindOwner_NoCandidateUsesThePrefix_ReturnsNull()
    {
        var candidates = new[] { Owned("Lertaro.Plugins.CoreExtensions", "\\") };

        Assert.IsNull(QueryTokenPrefixRules.FindOwner('#', candidates, null, null));
    }

    [TestMethod]
    public void FindOwner_ExcludedFieldIsTheOnlyOwner_TreatsThePrefixAsFree()
    {
        // Re-validating a field the user has not actually changed must not report the field conflicting
        // with its own saved value.
        var candidates = new[] { Owned("Lertaro.Plugins.CoreExtensions", "\\") };

        Assert.IsNull(QueryTokenPrefixRules.FindOwner('\\', candidates, "Lertaro.Plugins.CoreExtensions", "Prefix"));
    }

    [TestMethod]
    public void FindOwner_AnotherPluginsFieldOwnsThePrefix_StillConflicts()
    {
        var candidates = new[]
        {
            Owned("Lertaro.Plugins.CoreExtensions", "\\"),
            Owned("Lertaro.Plugins.Other", "\\")
        };

        var owner = QueryTokenPrefixRules.FindOwner('\\', candidates, "Lertaro.Plugins.CoreExtensions", "Prefix");

        Assert.IsNotNull(owner);
        Assert.AreEqual("Lertaro.Plugins.Other", owner.Value.PluginId);
    }

    [TestMethod]
    public void FindOwner_PluginIdComparisonIsCaseInsensitive()
    {
        // Plugin ids come from a DLL file name, so casing must not decide whether a field is "itself".
        var candidates = new[] { Owned("Lertaro.Plugins.CoreExtensions", "\\") };

        Assert.IsNull(QueryTokenPrefixRules.FindOwner('\\', candidates, "lertaro.plugins.coreextensions", "Prefix"));
    }

    [TestMethod]
    public void FindOwner_ComparesOnlyTheFirstCharacter()
    {
        // The scanner only ever inspects one character, so a longer stored value still claims the prefix.
        var candidates = new[] { Owned("Lertaro.Plugins.CoreExtensions", "\\audio") };

        Assert.IsNotNull(QueryTokenPrefixRules.FindOwner('\\', candidates, null, null));
    }

    [TestMethod]
    public void FindOwner_EmptyCandidateValue_IsNotAnOwner()
        => Assert.IsNull(QueryTokenPrefixRules.FindOwner('\\', new[] { Owned("plugin", string.Empty) }, null, null));

    [TestMethod]
    public void GlobalPrefixConflict_EmptyPrefix_IsReported()
        // Nothing could be tokenized at all, which is worth saying out loud rather than silently
        // disabling every plugin token.
        => Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict(string.Empty, new UserSettings()));

    [TestMethod]
    public void GlobalPrefixConflict_SortFilterTrigger_IsReported()
    {
        // '<' and '>' are always pulled out by the scanner as sort/filter tokens, whatever the configured
        // prefix is, so using either as the prefix makes the plugin tokens unreachable.
        Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict("<", new UserSettings()));
        Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict(">", new UserSettings()));
    }

    [TestMethod]
    public void GlobalPrefixConflict_OrdinaryCharacter_IsAccepted()
    {
        Assert.IsNull(QueryTokenPrefixRules.GlobalPrefixConflict("\\", new UserSettings()));
        Assert.IsNull(QueryTokenPrefixRules.GlobalPrefixConflict("#", new UserSettings()));
    }

    [TestMethod]
    public void PluginPrefixConflict_MatchesTheMainPrefix_IsReported()
    {
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };
        var field = PrefixField();

        Assert.IsNotNull(QueryTokenPrefixRules.PluginPrefixConflict("plugin", field, "\\", settings));
    }

    [TestMethod]
    public void PluginPrefixConflict_DifferentFromTheMainPrefix_IsAccepted()
    {
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };
        var field = PrefixField();

        Assert.IsNull(QueryTokenPrefixRules.PluginPrefixConflict("plugin", field, "#", settings));
    }

    [TestMethod]
    public void PluginPrefixConflict_SortFilterTrigger_IsReported()
        => Assert.IsNotNull(QueryTokenPrefixRules.PluginPrefixConflict("plugin", PrefixField(), "<", new UserSettings()));

    [TestMethod]
    public void IsPrefixField_OnlySingleCharacterTextFields()
    {
        Assert.IsTrue(QueryTokenPrefixRules.IsPrefixField(PrefixField()));
        Assert.IsFalse(QueryTokenPrefixRules.IsPrefixField(new PluginConfigField { FieldType = ConfigFieldType.Text, MaxLength = 10 }));
        Assert.IsFalse(QueryTokenPrefixRules.IsPrefixField(new PluginConfigField { FieldType = ConfigFieldType.Boolean, MaxLength = 1 }));
    }

    [TestMethod]
    public void IsFixedTrigger_OnlyTheTwoSortFilters()
    {
        Assert.IsTrue(QueryTokenPrefixRules.IsFixedTrigger('<'));
        Assert.IsTrue(QueryTokenPrefixRules.IsFixedTrigger('>'));
        Assert.IsFalse(QueryTokenPrefixRules.IsFixedTrigger('\\'));
    }

    private static PluginConfigField PrefixField()
        => new() { Key = "Prefix", FieldType = ConfigFieldType.Text, MaxLength = 1, DefaultValue = "\\" };
}
