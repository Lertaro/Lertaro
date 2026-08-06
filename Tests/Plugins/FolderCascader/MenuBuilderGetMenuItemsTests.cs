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
