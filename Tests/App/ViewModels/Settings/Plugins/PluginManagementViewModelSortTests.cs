using System.Collections.ObjectModel;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginManagementViewModelSortTests
{
    // Name order and disabled state, without a live plugin dir. A translation/theme-only component has no
    // switch, so its plugin can never count as fully disabled.
    private static PluginInfoViewModel MakePlugin(
        string name,
        bool fullyDisabled = false,
        bool hasToggleable = true,
        string? dll = null)
    {
        var components = new List<PluginComponentViewModel>
        {
            new(name + "::c", hasToggleable ? PluginComponentType.Action : PluginComponentType.TranslationProvider,
                name, isEnabled: hasToggleable ? !fullyDisabled : true)
        };
        return new PluginInfoViewModel(name, "1.0", dll ?? name + ".dll", "1.0-sdk", components, []);
    }

    private static List<string> Names(IEnumerable<PluginInfoViewModel> plugins) =>
        plugins.Select(p => p.Name).ToList();

    [TestMethod]
    public void DisplayNameOrder_ZhCN_SortsChineseNamesByPinyin()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("自定义动作插件"),
            MakePlugin("内容搜索"),
            MakePlugin("动漫主题"),
            MakePlugin("窗口切换器"),
        };

        CollectionAssert.AreEqual(
            new[] { "窗口切换器", "动漫主题", "内容搜索", "自定义动作插件" },
            Names(plugins.OrderBy(p => p, PluginLoaderHelper.DisplayNameOrder("zh-CN"))));
    }

    [TestMethod]
    public void DisplayNameOrder_AsciiNames_SortAtoZIgnoringCase()
    {
        var plugins = new List<PluginInfoViewModel>
            { MakePlugin("wps"), MakePlugin("Bandizip"), MakePlugin("AutoCAD"), MakePlugin("files") };

        CollectionAssert.AreEqual(
            new[] { "AutoCAD", "Bandizip", "files", "wps" },
            Names(plugins.OrderBy(p => p, PluginLoaderHelper.DisplayNameOrder("zh-CN"))));
    }

    [TestMethod]
    public void DisplayNameOrder_MixedName_JoinsTheBlockOfItsLeadingCharacter()
    {
        // The leading "F" makes this a Latin name even though most of it is Chinese, and the Chinese
        // block goes first.
        var plugins = new List<PluginInfoViewModel>
            { MakePlugin("Flow.Launcher 插件桥接"), MakePlugin("网页搜索与快捷直达插件") };

        CollectionAssert.AreEqual(
            new[] { "网页搜索与快捷直达插件", "Flow.Launcher 插件桥接" },
            Names(plugins.OrderBy(p => p, PluginLoaderHelper.DisplayNameOrder("zh-CN"))));
    }

    [TestMethod]
    public void DisplayNameOrder_FollowsTheInterfaceLanguageNotTheOsCulture()
    {
        // zh-CN orders Han by pinyin (chuang before dong) while the Traditional locales order them by
        // stroke count (动's 6 beats 窗's 12). Pinning both keeps the order tied to the UI language.
        var plugins = new List<PluginInfoViewModel> { MakePlugin("窗口切换器"), MakePlugin("动漫主题") };

        CollectionAssert.AreEqual(new[] { "窗口切换器", "动漫主题" },
            Names(plugins.OrderBy(p => p, PluginLoaderHelper.DisplayNameOrder("zh-CN"))));
        CollectionAssert.AreEqual(new[] { "动漫主题", "窗口切换器" },
            Names(plugins.OrderBy(p => p, PluginLoaderHelper.DisplayNameOrder("zh-TW"))));
    }

    [TestMethod]
    public void SortForDisplay_NameOrder_SplitsNativeScriptFromAsciiAndPinsGalleriesLast()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Bandizip"),
            MakePlugin("内容搜索"),
            MakePlugin("AutoCAD"),
            MakePlugin("动漫主题", hasToggleable: false, dll: "Lertaro.Plugins.AnimeThemes.dll"),
        };

        CollectionAssert.AreEqual(
            new[] { "内容搜索", "AutoCAD", "Bandizip", "动漫主题" },
            Names(PluginLoaderHelper.SortForDisplay(plugins)));
    }

    [TestMethod]
    public void SortPluginsList_DisabledLast_SinksDisabledBelowActive()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Zed"),
            MakePlugin("DisabledB", fullyDisabled: true),
            MakePlugin("Alpha"),
            MakePlugin("DisabledA", fullyDisabled: true),
        };

        CollectionAssert.AreEqual(new[] { "Alpha", "Zed", "DisabledA", "DisabledB" },
            Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast: true)));

        // The name-first mode leaves a disabled plugin in its alphabetical position.
        CollectionAssert.AreEqual(new[] { "Alpha", "DisabledA", "DisabledB", "Zed" },
            Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast: false)));
    }

    [TestMethod]
    public void SortPluginsList_PinnedGalleries_StayLastInBothModes()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Alpha"),
            MakePlugin("Anime", hasToggleable: false, dll: "Lertaro.Plugins.AnimeThemes.dll"),
            MakePlugin("Zed"),
            MakePlugin("Curated", hasToggleable: false, dll: "Lertaro.Plugins.CuratedThemes.dll"),
        };

        foreach (var disabledLast in new[] { true, false })
            CollectionAssert.AreEqual(new[] { "Alpha", "Zed", "Anime", "Curated" },
                Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast)),
                "disabledLast=" + disabledLast);
    }

    [TestMethod]
    public void SortPluginsList_CoreExtensionsLeadsItsBlock()
    {
        // Named so plain name order would file it last, isolating the leading pin from the collation.
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Alpha"),
            MakePlugin("Zulu", dll: "Lertaro.Plugins.CoreExtensions.dll"),
            MakePlugin("Bravo", fullyDisabled: true),
            MakePlugin("Zed"),
        };

        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha", "Zed", "Bravo" },
            Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast: true)));

        // The name-first mode has no blocks to lead, so it is simply first.
        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha", "Bravo", "Zed" },
            Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast: false)));
    }

    [TestMethod]
    public void SortPluginsList_DisabledCoreExtensions_LeadsTheDisabledBlock()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("Alpha"),
            MakePlugin("Zulu", fullyDisabled: true, dll: "Lertaro.Plugins.CoreExtensions.dll"),
            MakePlugin("Bravo", fullyDisabled: true),
        };

        // It leads the disabled tail rather than jumping above the enabled block it left.
        CollectionAssert.AreEqual(new[] { "Alpha", "Zulu", "Bravo" },
            Names(PluginManagementViewModel.SortPluginsList(plugins, disabledLast: true)));
    }

    [TestMethod]
    public void SortForDisplay_CoreExtensionsLeadsAndGalleriesRemainLast()
    {
        var plugins = new List<PluginInfoViewModel>
        {
            MakePlugin("内容搜索"),
            MakePlugin("Zulu", dll: "Lertaro.Plugins.CoreExtensions.dll"),
            MakePlugin("动漫主题", hasToggleable: false, dll: "Lertaro.Plugins.AnimeThemes.dll"),
        };

        CollectionAssert.AreEqual(new[] { "Zulu", "内容搜索", "动漫主题" },
            Names(PluginLoaderHelper.SortForDisplay(plugins)));
    }

    [TestMethod]
    public void SyncRuntimeStatusCollection_UnchangedOrder_DoesNotRaiseCollectionChanges()
    {
        var first = new PluginRuntimeStatusItemViewModel(MakePlugin("First"));
        var second = new PluginRuntimeStatusItemViewModel(MakePlugin("Second"));
        var statuses = new ObservableCollection<PluginRuntimeStatusItemViewModel> { first, second };
        var changeCount = 0;
        statuses.CollectionChanged += (_, _) => changeCount++;

        PluginManagementViewModel.SyncRuntimeStatusCollection(statuses, [first, second]);

        Assert.AreEqual(0, changeCount);
    }

    [TestMethod]
    public void SyncRuntimeStatusCollection_NewOrder_MovesExistingRowsAndRemovesMissingRows()
    {
        var first = new PluginRuntimeStatusItemViewModel(MakePlugin("First"));
        var second = new PluginRuntimeStatusItemViewModel(MakePlugin("Second"));
        var replacement = new PluginRuntimeStatusItemViewModel(MakePlugin("Replacement"));
        var statuses = new ObservableCollection<PluginRuntimeStatusItemViewModel> { first, second };

        PluginManagementViewModel.SyncRuntimeStatusCollection(statuses, [second, replacement]);

        Assert.HasCount(2, statuses);
        Assert.AreSame(second, statuses[0]);
        Assert.AreSame(replacement, statuses[1]);
    }
}
