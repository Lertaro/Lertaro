using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomActions.Tests;

// PluginSettingsService.GetSettingFunc and ClipboardService.GetTextFunc are shared static delegates,
// and DynamicActionProvider caches what it loads in a static field -- [DoNotParallelize] plus resetting
// all of that in TestInitialize keeps tests in this class from seeing each other's configuration.
[TestClass]
[DoNotParallelize]
public sealed class DynamicActionProviderTests
{
    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        ClipboardService.GetTextFunc = null;
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
        bool folderOnly = false, string extensions = "", bool multiSelect = false, string hotkey = "",
        string icon = "") => new()
        {
            Enabled = enabled,
            Title = title,
            Path = path,
            FolderOnly = folderOnly,
            Extensions = extensions,
            MultiSelect = multiSelect,
            Hotkey = hotkey,
            Icon = icon,
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
    public void GetMenuItems_ActionWithConfiguredIcon_CarriesItsPathData()
    {
        // The Icon field is stored as WPF path data (the settings field converts a pasted SVG document to
        // it), and the menu row can only show it if the provider hands it over -- an item that arrives with
        // no icon at all is exactly what the configured field used to produce.
        ConfigureActions(new()
        {
            MakeAction(title: "Compress", icon: "M7 2v11h3v9l7-12h-4l3-8z"),
            MakeAction(title: "Reveal"),
        });

        var items = new DynamicActionProvider().GetMenuItems(new[] { new FakeResult() }, IntPtr.Zero).ToList();

        Assert.AreEqual("M7 2v11h3v9l7-12h-4l3-8z", items[0].IconPathData);
        Assert.AreEqual(string.Empty, items[1].IconPathData);
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
    public void BuildArguments_NoTemplate_PassesEveryPathQuoted() =>
        Assert.AreEqual("\"C:\\a b.txt\" C:\\c.txt",
            DynamicActionProvider.BuildArguments(null, [@"C:\a b.txt", @"C:\c.txt"]));

    [TestMethod]
    public void BuildArguments_BothPlaceholders_EachGetsThePaths() =>
        Assert.AreEqual("--left C:\\c.txt --right C:\\c.txt",
            DynamicActionProvider.BuildArguments("--left %s --right {}", [@"C:\c.txt"]));

    [TestMethod]
    public void BuildArguments_PathContainingAPlaceholder_IsSubstitutedExactlyOnce() =>
        // A file named with "{}", dropped in for %s, must not then be expanded again by the {} pass.
        Assert.AreEqual("--open \"C:\\odd {} name.txt\"",
            DynamicActionProvider.BuildArguments("--open %s", [@"C:\odd {} name.txt"]));

    [TestMethod]
    public void BuildArguments_ClipboardPlaceholders_SubstituteTheQuotedTextReadOnce()
    {
        var reads = 0;
        ClipboardService.GetTextFunc = () => { reads++; return "copied text"; };

        var arguments = DynamicActionProvider.BuildArguments("--from %c --to {c}", [@"C:\c.txt"]);

        Assert.AreEqual("--from \"copied text\" --to \"copied text\"", arguments);
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public void BuildArguments_NoClipboardPlaceholder_NeverReadsTheClipboard()
    {
        var reads = 0;
        ClipboardService.GetTextFunc = () => { reads++; return "unused"; };

        DynamicActionProvider.BuildArguments("--open %s --also {}", [@"C:\c.txt"]);

        Assert.AreEqual(0, reads);
    }

    [TestMethod]
    public void BuildArguments_ClipboardHoldsNoText_PlaceholderVanishes()
    {
        ClipboardService.GetTextFunc = () => null;

        Assert.AreEqual("[]", DynamicActionProvider.BuildArguments("[%c]", [@"C:\c.txt"]));
    }

    [TestMethod]
    public void BuildArguments_LongerPercentToken_IsLeftAloneAndNeverReadsTheClipboard()
    {
        // A letter after %s or %c means it is some other program's own token, not a placeholder here --
        // and a template with no placeholder must not open the clipboard either.
        var reads = 0;
        ClipboardService.GetTextFunc = () => { reads++; return "unused"; };

        Assert.AreEqual("--pretty=%cd --dir %cd% --root=%systemroot%",
            DynamicActionProvider.BuildArguments("--pretty=%cd --dir %cd% --root=%systemroot%", [@"C:\c.txt"]));
        Assert.AreEqual(0, reads);
    }

    [TestMethod]
    public void BuildArguments_ClipboardTextContainingAPlaceholder_IsSubstitutedExactlyOnce()
    {
        // Clipboard text naming "{}", dropped in for %c, must not then be expanded by the {} pass.
        ClipboardService.GetTextFunc = () => "a {} b";

        Assert.AreEqual("--paste \"a {} b\"",
            DynamicActionProvider.BuildArguments("--paste %c", [@"C:\c.txt"]));
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
