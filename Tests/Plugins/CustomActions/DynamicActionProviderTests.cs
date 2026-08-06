using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomActions.Tests;

// PluginSettingsService.GetSettingFunc is a shared static delegate, and DynamicActionProvider caches
// what it loads in a static field -- [DoNotParallelize] plus resetting both in TestInitialize keeps
// tests in this class from seeing each other's configured actions.
[TestClass]
[DoNotParallelize]
public sealed class DynamicActionProviderTests
{
    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        DynamicActionProvider.InvalidateCache();
    }

    private static void ConfigureActions(List<DynamicActionProvider.ActionItem> actions) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.CustomActions" && key == "Actions" ? actions : defaultValue;

    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "file.txt";
        public string FullPath { get; init; } = @"C:\file.txt";
        public string ContextDirectory { get; init; } = @"C:\";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private static DynamicActionProvider.ActionItem MakeAction(
        bool enabled = true, string title = "Do Thing", string path = "tool.exe",
        bool folderOnly = false, string extensions = "", bool multiSelect = false, string hotkey = "") => new()
        {
            Enabled = enabled,
            Title = title,
            Path = path,
            FolderOnly = folderOnly,
            Extensions = extensions,
            MultiSelect = multiSelect,
            Hotkey = hotkey,
        };

    [TestMethod]
    public void IsVisibleInMenu_NoConfiguredActions_ReturnsFalse() =>
        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult() }, SearchWindowType.Main));

    [TestMethod]
    public void IsVisibleInMenu_ApplicableAction_ReturnsTrue()
    {
        ConfigureActions(new() { MakeAction() });

        Assert.IsTrue(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult() }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_DisabledAction_ReturnsFalse()
    {
        ConfigureActions(new() { MakeAction(enabled: false) });

        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult() }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_FolderOnlyActionOnFile_ReturnsFalse()
    {
        ConfigureActions(new() { MakeAction(folderOnly: true) });

        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult { IsDir = false } }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_FolderOnlyActionOnFolder_ReturnsTrue()
    {
        ConfigureActions(new() { MakeAction(folderOnly: true) });

        Assert.IsTrue(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult { IsDir = true } }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_ExtensionFilterMatchingExtension_ReturnsTrue()
    {
        ConfigureActions(new() { MakeAction(extensions: ".txt,.md") });

        Assert.IsTrue(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult { FullPath = @"C:\a.txt" } }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_ExtensionFilterNonMatchingExtension_ReturnsFalse()
    {
        ConfigureActions(new() { MakeAction(extensions: ".txt,.md") });

        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(new[] { new FakeResult { FullPath = @"C:\a.exe" } }, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_MultipleResultsWithoutMultiSelect_ReturnsFalse()
    {
        ConfigureActions(new() { MakeAction(multiSelect: false) });
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt" }, new FakeResult { FullPath = @"C:\b.txt" } };

        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(results, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_MultipleResultsWithMultiSelectEnabled_ReturnsTrue()
    {
        ConfigureActions(new() { MakeAction(multiSelect: true) });
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt" }, new FakeResult { FullPath = @"C:\b.txt" } };

        Assert.IsTrue(new DynamicActionProvider().IsVisibleInMenu(results, SearchWindowType.Main));
    }

    [TestMethod]
    public void IsVisibleInMenu_EmptyResultList_ReturnsFalse() =>
        Assert.IsFalse(new DynamicActionProvider().IsVisibleInMenu(Array.Empty<ISearchResult>(), SearchWindowType.Main));

    [TestMethod]
    public void GetMenuItems_ApplicableAction_YieldsItemWithMatchingText()
    {
        ConfigureActions(new() { MakeAction(title: "Compress") });

        var items = new DynamicActionProvider().GetMenuItems(new[] { new FakeResult() }, IntPtr.Zero).ToList();

        Assert.HasCount(1, items);
        Assert.AreEqual("Compress", items[0].Text);
        Assert.IsNotNull(items[0].OnExecute);
    }

    [TestMethod]
    public void GetMenuItems_NonZeroHMenu_YieldsNothing()
    {
        ConfigureActions(new() { MakeAction() });

        var items = new DynamicActionProvider().GetMenuItems(new[] { new FakeResult() }, new IntPtr(1)).ToList();

        Assert.IsEmpty(items);
    }

    [TestMethod]
    public void GetHotkeyActions_ActionWithHotkey_IsReturned()
    {
        ConfigureActions(new() { MakeAction(hotkey: "Ctrl+Shift+X") });

        var actions = new DynamicActionProvider().GetHotkeyActions(new[] { new FakeResult() }).ToList();

        Assert.HasCount(1, actions);
        Assert.AreEqual("Ctrl+Shift+X", actions[0].Hotkey);
    }

    [TestMethod]
    public void GetHotkeyActions_ActionWithoutHotkey_IsExcluded()
    {
        ConfigureActions(new() { MakeAction(hotkey: "") });

        var actions = new DynamicActionProvider().GetHotkeyActions(new[] { new FakeResult() }).ToList();

        Assert.IsEmpty(actions);
    }

    [TestMethod]
    public void ClearSession_InvalidatesCache_SoLaterConfigChangeIsPickedUp()
    {
        ConfigureActions(new() { MakeAction(enabled: false) });
        var provider = new DynamicActionProvider();
        Assert.IsFalse(provider.IsVisibleInMenu(new[] { new FakeResult() }, SearchWindowType.Main));

        provider.ClearSession();
        ConfigureActions(new() { MakeAction(enabled: true) });

        Assert.IsTrue(provider.IsVisibleInMenu(new[] { new FakeResult() }, SearchWindowType.Main));
    }
}
