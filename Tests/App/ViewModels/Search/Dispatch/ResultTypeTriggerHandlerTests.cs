using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

// Pins the quick window's per-type trigger CHARACTER strip. The rule is TriggerWord's (where the text
// starts) plus one piece of policy of its own: a configured trigger WORD outranks a character that merely
// starts the same token, because "set 路径" cut down to "et 路径" is text its owner never typed. That
// precedence is the part a running window cannot show, and the part that used to be wrong -- hence the pure
// Match under test rather than the handler, which reads UserSettings and the plugin registry.
[TestClass]
public sealed class ResultTypeTriggerHandlerTests
{
    private static readonly IReadOnlyDictionary<string, string> CommaTriggers =
        new Dictionary<string, string> { ["files-type-id"] = "，" };

    private static readonly IReadOnlyDictionary<string, string> STriggers =
        new Dictionary<string, string> { ["apps-type-id"] = "s" };

    private static (string CleanQuery, string? TriggeredTypeId) Match(
        string raw,
        string cleanQuery,
        bool isInlineSearchContext = false,
        IReadOnlyDictionary<string, string>? triggers = null,
        Func<string, bool>? claimsLeadingWord = null) =>
        ResultTypeTriggerHandler.Match(
            raw, cleanQuery, isInlineSearchContext, triggers ?? CommaTriggers, claimsLeadingWord ?? (_ => false));

    [TestMethod]
    public void Match_TriggerCharacter_ComesOffAndSelectsItsType()
    {
        var (cleanQuery, typeId) = Match("，环境", "，环境");

        Assert.AreEqual("环境", cleanQuery);
        Assert.AreEqual("files-type-id", typeId);
    }

    // A casing difference is not a different trigger: the character comparison follows the words' own
    // case-insensitive rule rather than silently depending on the Shift key.
    [TestMethod]
    public void Match_TriggerCharacterIsCaseInsensitive()
    {
        var (cleanQuery, typeId) = Match("S report", "S report", triggers: STriggers);

        Assert.AreEqual(" report", cleanQuery);
        Assert.AreEqual("apps-type-id", typeId);
    }

    // The inline window has no per-type trigger at all, and a first character the token/exclusion syntax
    // already removed from the clean query is not the character the user typed first.
    [TestMethod]
    public void Match_InlineWindowOrAlreadyStrippedFirstCharacter_LeavesTheQueryAlone()
    {
        var (inlineQuery, inlineTypeId) = Match("，环境", "，环境", isInlineSearchContext: true);
        Assert.AreEqual("，环境", inlineQuery);
        Assert.IsNull(inlineTypeId);

        var (tokenQuery, tokenTypeId) = Match("*，环境", "，环境");
        Assert.AreEqual("，环境", tokenQuery);
        Assert.IsNull(tokenTypeId);
    }

    [TestMethod]
    public void Match_NoTriggerConfiguredForThatCharacter_LeavesItAlone()
    {
        var (cleanQuery, typeId) = Match("x 环境", "x 环境");

        Assert.AreEqual("x 环境", cleanQuery);
        Assert.IsNull(typeId);
    }

    [TestMethod]
    public void Match_RegisteredWordClaimsTheToken_TheCharacterDoesNotCut()
    {
        var (cleanQuery, typeId) = Match("s 路径", "s 路径", triggers: STriggers, claimsLeadingWord: _ => true);

        Assert.AreEqual("s 路径", cleanQuery, "the word's owner still needs every character of its word");
        Assert.IsNull(typeId);
    }

    // The word inventory is only walked once a character actually matched, which is the shipped default
    // (no per-type trigger at all): the common keystroke pays nothing for the precedence rule.
    [TestMethod]
    public void Match_NoMatchingCharacter_DoesNotConsultTheWordInventoryAtAll()
    {
        var asked = false;

        var (cleanQuery, typeId) = Match("zzz report", "zzz report", claimsLeadingWord: _ => { asked = true; return true; });

        Assert.AreEqual("zzz report", cleanQuery);
        Assert.IsNull(typeId);
        Assert.IsFalse(asked);
    }

    [TestMethod]
    public void Match_EmptyInputs_AreLeftAlone()
    {
        var (cleanQuery, typeId) = Match(string.Empty, string.Empty);

        Assert.AreEqual(string.Empty, cleanQuery);
        Assert.IsNull(typeId);
    }
}
