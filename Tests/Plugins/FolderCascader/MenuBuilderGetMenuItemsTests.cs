using System.Collections.Concurrent;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

// Wires PluginSettingsService.GetSettingFunc/SetSettingFunc/PluginPromptService.PromptFunc (shared static
// delegates) -- [DoNotParallelize] keeps it from racing against other tests in this class touching the
// same statics.
[TestClass]
[DoNotParallelize]
public sealed class MenuBuilderGetMenuItemsTests
{
    [TestMethod]
    public void GetMenuItems_RootLevel_NeverIncludesAHeaderOrStandaloneAddItem()
    {
        // Root's own "+" comes from Provider.HeaderAction, rendered by the host directly into the
        // group header row (see QuickNavigationMenu.Show) -- it's never one of GetMenuItems' own
        // returned DynamicMenuItems the way a category submenu's header is.
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders"
                ? new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Downloads", @"C:\Downloads") }
                : defaultValue;
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, IntPtr.Zero, provider).ToList();

            Assert.IsFalse(items.Any(i => i.IsHeader));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void Provider_HeaderAction_IsWiredWithATooltip()
    {
        var provider = new Provider();

        Assert.IsNotNull(provider.HeaderAction);
        Assert.IsFalse(string.IsNullOrEmpty(provider.HeaderActionTooltip));
    }

    [TestMethod]
    public void Provider_HeaderAction_PromptsThenSavesAtRootLevel()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            fields.ToDictionary(f => f.Key, object? (f) => f.DefaultValue);
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            provider.HeaderAction!(result);

            var added = saved!.Single();
            Assert.AreEqual(Path.GetTempPath(), added.Path);
            Assert.AreEqual("", added.SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void GetMenuItems_CategoryLevel_FirstItemIsHeaderNamedAfterTheCategorysLastSegment()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders"
                ? new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Router UI", @"C:\Net\Router", "Tools/Network") }
                : defaultValue;
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            var header = items[0];
            Assert.IsTrue(header.IsHeader);
            Assert.AreEqual("Network", header.Text);
            Assert.IsNotNull(header.OnExecute);
            Assert.IsTrue(items.Any(i => i.Text == "Router UI"));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void GetMenuItems_EmptyCategoryLevel_StillGetsAHeader()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Empty" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            Assert.IsTrue(items[0].IsHeader);
            Assert.AreEqual("Empty", items[0].Text);
            Assert.IsTrue(items.Any(i => i.IsDisabled));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public async Task GetMenuItems_ThisPcOnABackgroundThread_ReturnsItsDrives()
    {
        const string thisPc = "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}";
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders"
                ? new List<FolderCascaderPlugin.FolderConfigItem> { Folder("This PC", thisPc) }
                : defaultValue;
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var root = await Task.Run(() => MenuBuilder.GetMenuItems(result, IntPtr.Zero, provider).ToList());
            var thisPcItem = root.Single(item => item.Text == "This PC");

            var children = await Task.Run(() => MenuBuilder.GetMenuItems(result, thisPcItem.SubMenuHandle, provider).ToList());

            // A real Windows host always has at least the system drive in This PC. Asserting one child
            // resolves to a drive root (locale-independent: "C:\", never the localized "Windows (C:)")
            // proves the enumeration actually descended into This PC: a bare HasSubMenu check would also
            // pass on the fallback root menu, whose lone "This PC" row is a submenu too.
            Assert.IsTrue(children.Count > 0, "This PC should enumerate at least its drives.");
            Assert.IsTrue(children.All(child => child.HasSubMenu), "Every This PC child should be a folder submenu.");
            Assert.IsTrue(
                children.Any(child =>
                {
                    if (!child.HasSubMenu || child.SubMenuHandle == IntPtr.Zero) return false;
                    var childPath = GetPath(provider, child.SubMenuHandle);
                    return Path.GetPathRoot(childPath) is { Length: > 0 } root
                           && string.Equals(root, childPath, StringComparison.OrdinalIgnoreCase);
                }),
                "This PC should enumerate at least one drive root.");
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void RunOnSta_ConcurrentCalls_ShareOneStaApartment()
    {
        // Two apartments holding the same agile shell folder object is what let one enumeration release the
        // other's RCW mid-Items(), which is how the first open of This PC came back as the empty-folder
        // placeholder. Every call has to land on the one single-threaded apartment.
        var seen = new ConcurrentQueue<string>();
        using var go = new ManualResetEventSlim(false);

        void Call() => ShellEnumerator.RunOnSta(() =>
        {
            seen.Enqueue($"t{Environment.CurrentManagedThreadId}:{Thread.CurrentThread.GetApartmentState()}");
            Thread.Sleep(30);
        });

        var first = Task.Run(() => { go.Wait(); Call(); });
        var second = Task.Run(() => { go.Wait(); Call(); });
        go.Set();
        first.Wait();
        second.Wait();

        var entries = seen.ToArray();
        Assert.HasCount(2, entries, "one of the concurrent calls never ran");
        Assert.HasCount(1, entries.Select(entry => entry.Split(':')[0]).Distinct(), "shell work ran on more than one apartment");
        Assert.IsTrue(entries.All(entry => entry.EndsWith(":STA")), "shell work needs a single-threaded apartment");
    }

    [TestMethod]
    public void GetMenuItems_CategoryLevel_HeaderOnExecute_PromptsThenSavesAtThatSubMenu()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            fields.ToDictionary(f => f.Key, object? (f) => f.DefaultValue);
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };
            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            items[0].OnExecute!();

            Assert.AreEqual("Tools/Network", saved!.Single().SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }
}
