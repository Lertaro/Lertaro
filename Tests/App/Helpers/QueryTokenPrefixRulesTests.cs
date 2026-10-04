using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// The query-token trigger character is shared with the search syntax: the app-wide prefix and the fixed
// sort/filter triggers all start a token, and a character the syntax reads first is consumed before any
// provider is asked. That is silent, so this rule exists to surface the collision instead.
//
// There is no per-plugin prefix any more -- a plugin reads the app-wide one back through the SDK instead of
// storing its own copy -- so the app-wide field is the only place a character can be chosen badly.
[TestClass]
public sealed class QueryTokenPrefixRulesTests
{
    [TestMethod]
    public void GlobalPrefixConflict_EmptyPrefix_IsReported()
        // Nothing could be tokenized at all, which is worth saying out loud rather than silently
        // disabling every plugin token.
        => Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict(string.Empty));

    [TestMethod]
    public void GlobalPrefixConflict_EverySyntaxCharacter_IsReported()
    {
        // '<'/'>' are the worst case -- the scanner always reads those as token starts, so plugin tokens
        // become permanently unreachable -- but ':' and '*' collide with exclusion syntax and the bypass
        // marker, '/' with the regex clause delimiter, and '?' would silently retire the precision-inversion
        // syntax for every word it lifts, so the field rejects those too rather than letting one meaning
        // silently win.
        foreach (var prefix in new[] { "<", ">", ":", "*", "/", "?" })
            Assert.IsNotNull(QueryTokenPrefixRules.GlobalPrefixConflict(prefix), prefix);
    }

    [TestMethod]
    // '\' is in SearchSyntaxReserved.LeadingCharacters because it IS the token prefix, not because
    // something else consumes it: QueryTokenScanner is handed GlobalTokenPrefix, so this field's value is
    // what gives '\' its meaning. Reporting the shipped default would leave every user looking at a
    // permanent error under a field they never touched.
    public void GlobalPrefixConflict_ShippedDefault_IsAccepted()
        => Assert.IsNull(QueryTokenPrefixRules.GlobalPrefixConflict("\\"));

    // Only the search SYNTAX is this field's business. '#' is claimed by an instant-answer provider, which
    // is a different surface with its own rule -- and deliberately not consulted here, or this field would
    // refuse characters over a collision it cannot see.
    [TestMethod]
    public void GlobalPrefixConflict_CharacterTheSyntaxDoesNotRead_IsAccepted()
        => Assert.IsNull(QueryTokenPrefixRules.GlobalPrefixConflict("#"));

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
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict("/tr"));
        Assert.IsNotNull(QueryTokenPrefixRules.TriggerKeywordConflict("?tr"));
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

    // Whether an unusable value merely warns next to its field or actually refuses to save anything.
    //
    // A value already in effect -- what the settings file carries, or a blank the applier resolves to the
    // default -- must not block: the user is not entering it now, and blocking would refuse every other
    // setting in the window over one field they may not even be looking at (which is exactly the lockout an
    // upgraded install with the previous release's ':' prefix hit). A value the user IS entering does block,
    // which is the gate's whole purpose.
    //
    // The rule is only ever consulted for a value that is ALREADY known to be unusable, which is why "the
    // user typed something else, usable" is not a case here: that value raises no error to consult it about.
    [TestMethod]
    [DataRow(":", "\\", true, DisplayName = "typed an unusable value")]
    [DataRow(":", ":", false, DisplayName = "the unusable value is already in effect")]
    [DataRow("", ":", false, DisplayName = "cleared: the applier resolves blank to the default")]
    public void BlocksSaving_OnlyAValueTheUserJustTyped(string typed, string saved, bool expected)
        => Assert.AreEqual(expected, QueryTokenPrefixRules.BlocksSaving(typed, saved));

    [TestMethod]
    public void BlocksSaving_NothingPersistedYet_BlocksATypedValue()
        // A fresh install has no stored prefix, so an unusable value typed there is still one the user is
        // entering right now.
        => Assert.IsTrue(QueryTokenPrefixRules.BlocksSaving(":", null));
}
