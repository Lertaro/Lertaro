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
    public void GlobalPrefixConflict_EverySyntaxCharacter_IsReported()
    {
        // '<'/'>' are the worst case -- the scanner always reads those as token starts, so plugin tokens
        // become permanently unreachable -- but ':' and '*' collide with exclusion syntax and the bypass
        // marker, so the field rejects those too rather than letting one meaning silently win.
        foreach (var prefix in new[] { "<", ">", ":", "*" })
            Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict(prefix, new UserSettings()), prefix);
    }

    [TestMethod]
    public void GlobalPrefixConflict_CharacterNoPluginUses_IsAccepted()
    {
        // '\\' is the shipped default, so it is deliberately NOT free: the CoreExtensions custom-filter
        // prefix also defaults to it (see the plugin-conflict case above).
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
    public void IsAlwaysTokenTrigger_OnlyTheTwoSortFilters()
    {
        // Narrower than SearchSyntaxReserved.IsReserved on purpose: only these two are read as token starts
        // regardless of the configured prefix, which is a different (worse) situation than colliding with
        // the exclusion or bypass character.
        Assert.IsTrue(QueryTokenPrefixRules.IsAlwaysTokenTrigger('<'));
        Assert.IsTrue(QueryTokenPrefixRules.IsAlwaysTokenTrigger('>'));
        Assert.IsFalse(QueryTokenPrefixRules.IsAlwaysTokenTrigger('\\'));
        Assert.IsFalse(QueryTokenPrefixRules.IsAlwaysTokenTrigger(':'));
    }

    // An instant-answer trigger keyword is matched against the START of the query, so it competes with the
    // search syntax for the same character position. One starting with a reserved character is stripped
    // before the provider is ever asked, so the trigger silently never fires.
    [TestMethod]
    public void TriggerKeywordConflict_ReservedFirstCharacter_IsReported()
    {
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict("\\tr"));
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict(":tr"));
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict("<tr"));
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict(">tr"));
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict("*tr"));
    }

    [TestMethod]
    public void TriggerKeywordConflict_EveryDefaultPluginKeyword_IsAccepted()
    {
        // The keywords the bundled providers ship with, so a future reserved character cannot quietly
        // break one of them.
        foreach (var keyword in new[] { "bb", "bh", "tr", "ps", "ad", "win", "cs", "set", "flow", "g", "bd", "bing", "gh", "wiki", "yt" })
            Assert.IsNull(QueryTokenPrefixRules.TriggerKeywordConflict(keyword), keyword);
    }

    [TestMethod]
    public void TriggerKeywordConflict_Empty_IsReported()
        => Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict(string.Empty));

    private static PluginConfigField PrefixField()
        => new() { Key = "Prefix", FieldType = ConfigFieldType.Text, MaxLength = 1, DefaultValue = "\\" };
}
