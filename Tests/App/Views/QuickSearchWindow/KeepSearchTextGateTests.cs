using Lertaro.App.Views.QuickSearchWindow.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// The gate behind the "Keep search box content" setting. Only the decision is covered: actually keeping the
// text needs a live window (FinishHide skipping the reset, ShowSupport selecting the leftovers), which is
// why the decision was pulled out of those two spots and takes the two queries as plain strings.
[TestClass]
public sealed class KeepSearchTextGateTests
{
    [TestMethod]
    public void DisabledIsTheOldBehaviour() =>
        // Off by default, so an upgrade keeps clearing the box for everyone who never touched the setting.
        Assert.IsFalse(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: false, newQuery: "", existingQuery: "old"));

    [TestMethod]
    public void APlainSummonReopensTheLastText() =>
        Assert.IsTrue(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: "", existingQuery: "old"));

    [TestMethod]
    public void AnIncomingQueryStillReplacesIt() =>
        // Hotkey with a selection, or a typed prefill: that is a new search, not a resume.
        Assert.IsFalse(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: "new", existingQuery: "old"));

    [TestMethod]
    public void TheEarlierClipboardRefillStillReplacesIt() =>
        // ShowSupport turns clipboard text into the incoming query before this gate sees it, so a refill
        // reads exactly like the case above.
        Assert.IsFalse(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: "clipboard", existingQuery: "old"));

    [TestMethod]
    public void NothingToKeepWhenTheBoxWasAlreadyEmpty() =>
        // The user cleared the box before dismissing the window; re-selecting an empty string would leave
        // the caret mid-nothing instead of on the startup panel.
        Assert.IsFalse(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: "", existingQuery: ""));

    [TestMethod]
    public void ANullBoxIsTheFirstSummonNotSomethingToKeep() =>
        // The view model's query field starts out null and is only ever assigned by the show path, so the
        // very first summon reaches this gate with null. Reading .Length on it crashed the tray icon.
        Assert.IsFalse(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: "", existingQuery: null));

    [TestMethod]
    public void ANullIncomingQueryCountsAsNoIntent() =>
        // Symmetrical guard: the show path supplies a non-null query today, but the gate does not lean on
        // that staying true.
        Assert.IsTrue(QuickSearchWindowShowSupport.ShouldKeepSearchText(settingEnabled: true, newQuery: null, existingQuery: "old"));

    [TestMethod]
    public void TheSettingDefaultsToOff() =>
        // Keeping the box dirty is a change nobody asked for on upgrade.
        Assert.IsFalse(new SearchWindowSettings().KeepSearchText);
}
