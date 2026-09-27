using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

// Pins the rule behind the amber warning under a trigger-word field (and the one-line log entry a collision
// produces): who else already answers to this word. What the warning says about the effect is what the strip
// actually does -- two features on one word both run while the word comes off the file search once -- so the
// report has to read the same inventory the strip does, which is why it takes entries rather than collecting.
[TestClass]
public sealed class PluginTriggerCollisionReportTests
{
    private static PluginTriggerQuery.Entry E(string word, string owner, bool strips = true, string ownerId = "") =>
        new(word, owner, strips, ownerId);

    [TestMethod]
    public void FirstOtherOwner_TwoFeaturesOnOneWord_NamesTheOtherOwner()
    {
        var entries = new[] { E("cs", "内容搜索"), E("cs", "浏览器书签") };

        Assert.AreEqual("内容搜索", PluginTriggerCollisionReport.FirstOtherOwner(entries, "cs", "浏览器书签"));
    }

    // Case-insensitive, because the strip matches case-insensitively too: "CS" and "cs" are one collision.
    [TestMethod]
    public void FirstOtherOwner_DifferentCase_IsStillACollision()
        => Assert.AreEqual("XYplorer", PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("CS", "XYplorer") }, "cs", "内容搜索"));

    // Surrounding spaces in a stored value must not hide a clash from the user.
    [TestMethod]
    public void FirstOtherOwner_PaddedValues_AreTrimmedForComparison()
        => Assert.AreEqual("进程管理", PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("ps", "进程管理") }, "  ps  ", "内容搜索"));

    [TestMethod]
    public void FirstOtherOwner_FreeWord_ReportsNothing()
        => Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("cs", "内容搜索"), E("ps", "进程管理") }, "cs", "内容搜索"));

    // A plugin reusing one word across two of its own fields is not a clash between features.
    [TestMethod]
    public void FirstOtherOwner_OwnerMatchingItself_IsNotAClash()
        => Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("cs", "内容搜索"), E("cs", "内容搜索") }, "cs", "内容搜索"));

    // Words that only the other resolvers consume (a scope keyword, a per-type trigger) still collide with a
    // provider's word, so the report must see them -- they are in the inventory, just not in the strip.
    [TestMethod]
    public void FirstOtherOwner_SeesNonStrippingWordsFromOtherFeatures()
        => Assert.AreEqual("文件筛选", PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("tf", "文件筛选", strips: false) }, "tf", "内容搜索"));

    [TestMethod]
    public void FirstOtherOwner_EmptyOrMissingInputs_ReportNothing()
    {
        Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(new[] { E("cs", "内容搜索") }, "   ", "浏览器书签"));
        Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(Array.Empty<PluginTriggerQuery.Entry>(), "cs", "浏览器书签"));
    }

    // The Settings page identifies a plugin by its id (the assembly name it is configuring), never by the
    // localized name an entry displays -- so an entry that carries an id is excluded by id, which is what
    // lets a field warn about every other feature while staying quiet about its own plugin's two fields.
    [TestMethod]
    public void FirstOtherOwner_WithOwnerIds_ComparesById()
        => Assert.AreEqual("内容搜索", PluginTriggerCollisionReport.FirstOtherOwner(
               new[] { E("cs", "内容搜索", ownerId: "Lertaro.Plugins.ContentSearch") }, "cs", "Lertaro.Plugins.WindowSwitcher"));

    [TestMethod]
    public void FirstOtherOwner_TwoFieldsOfOnePlugin_AreNotAClashById()
        => Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(
               new[]
               {
                   E("bb", "浏览器书签", ownerId: "Lertaro.Plugins.BrowserData"),
                   E("bb", "浏览器历史", ownerId: "Lertaro.Plugins.BrowserData"),
               },
               "bb", "Lertaro.Plugins.BrowserData"));

    // The host's own per-type trigger characters are one component, which is exactly why the per-type editor
    // checks duplicates among its own rows separately (ResultTypeOrderViewModel.FindDuplicateTrigger): asking
    // this rule about two of them would answer "no clash" by construction.
    [TestMethod]
    public void FirstOtherOwner_TwoPerTypeTriggersOnOneCharacter_AreNotAClashByTheHostsOwnId()
    {
        var hostId = PluginTriggerCollisionReport.HostTriggerOwnerId;
        var entries = new[]
        {
            E("f", "files-type-id", strips: false, ownerId: hostId),
            E("f", "settings-type-id", strips: false, ownerId: hostId),
        };

        Assert.IsNull(PluginTriggerCollisionReport.FirstOtherOwner(entries, "f", hostId));
    }
}
