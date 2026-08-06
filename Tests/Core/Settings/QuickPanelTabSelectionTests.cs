namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class QuickPanelTabSelectionTests
{
    private static List<QuickPanelTab> BuildTabs() => new()
    {
        new QuickPanelTab { Id = "general" },
        new QuickPanelTab { Id = "design", Processes = { "photoshop", "illustrator.exe" } },
        new QuickPanelTab { Id = "code", Processes = { "Code.exe" } },
    };

    [TestMethod]
    public void SelectTabId_ProcessClaimedByATab_PicksThatTab()
    {
        Assert.AreEqual("design", QuickPanelTabSelection.SelectTabId("photoshop.exe", BuildTabs()));
        Assert.AreEqual("design", QuickPanelTabSelection.SelectTabId("illustrator", BuildTabs()));
        Assert.AreEqual("code", QuickPanelTabSelection.SelectTabId("code.exe", BuildTabs()));
    }

    // Null, not the first tab: the caller keeps whatever the user last had open, which is a different
    // answer from "no tab claims this app".
    [TestMethod]
    public void SelectTabId_UnclaimedProcess_LeavesTheChoiceToTheCaller()
    {
        Assert.IsNull(QuickPanelTabSelection.SelectTabId("notepad", BuildTabs()));
        Assert.IsNull(QuickPanelTabSelection.SelectTabId("", BuildTabs()));
        Assert.IsNull(QuickPanelTabSelection.SelectTabId("photoshop", null));
    }

    [TestMethod]
    public void SelectTabId_ProcessClaimedTwice_TakesTheFirstTab()
    {
        var tabs = new List<QuickPanelTab>
        {
            new() { Id = "first", Processes = { "shared" } },
            new() { Id = "second", Processes = { "shared" } },
        };

        Assert.AreEqual("first", QuickPanelTabSelection.SelectTabId("shared.exe", tabs));
    }

    [TestMethod]
    public void IsBlocked_ProcessOnThePanelsOwnList_BlocksThePanel()
    {
        var settings = new UserSettings();
        settings.QuickPanel.BlacklistedProcesses.Add("game");
        settings.QuickPanel.BlacklistedProcesses.Add("vlc.exe");

        Assert.IsTrue(QuickPanelTabSelection.IsBlocked("game.exe", settings));
        Assert.IsTrue(QuickPanelTabSelection.IsBlocked("VLC", settings));
        Assert.IsFalse(QuickPanelTabSelection.IsBlocked("explorer", settings));
    }

    // The panel's list adds to the global one; it never replaces it. The hotkey path is gated upstream
    // by the keyboard hook, but that gate exempts file dialogs and Toggle() is reachable without it, so
    // the global list has to hold here on its own.
    [TestMethod]
    public void IsBlocked_ProcessOnTheGlobalList_BlocksThePanelToo()
    {
        var settings = new UserSettings { BlacklistedProcesses = { "game" } };

        Assert.IsTrue(QuickPanelTabSelection.IsBlocked("game.exe", settings));
        Assert.IsEmpty(settings.QuickPanel.BlacklistedProcesses, "blocked by the global list alone");
    }

    [TestMethod]
    public void IsBlocked_ProcessOnNeitherList_DoesNotBlock()
    {
        var settings = new UserSettings { BlacklistedProcesses = { "game" } };
        settings.QuickPanel.BlacklistedProcesses.Add("vlc");

        Assert.IsFalse(QuickPanelTabSelection.IsBlocked("explorer", settings));
        Assert.IsFalse(QuickPanelTabSelection.IsBlocked("explorer", null));
    }
}

[TestClass]
public sealed class ProcessNameFilterTests
{
    // Both spellings, both sides: the image path gives "chrome.exe" while Process.ProcessName gives
    // "chrome", and users type whichever they think of.
    [TestMethod]
    public void Matches_WithOrWithoutExtension_OnEitherSide()
    {
        Assert.IsTrue(ProcessNameFilter.Matches("chrome.exe", new[] { "chrome" }));
        Assert.IsTrue(ProcessNameFilter.Matches("chrome", new[] { "chrome.exe" }));
        Assert.IsTrue(ProcessNameFilter.Matches("CHROME.EXE", new[] { " chrome " }));
    }

    [TestMethod]
    public void Matches_DifferentProcess_DoesNot()
    {
        Assert.IsFalse(ProcessNameFilter.Matches("chrome.exe", new[] { "chromium" }));
        Assert.IsFalse(ProcessNameFilter.Matches("chrome.exe", new[] { "" }));
        Assert.IsFalse(ProcessNameFilter.Matches("", new[] { "chrome" }));
        Assert.IsFalse(ProcessNameFilter.Matches("chrome", null));
    }
}
