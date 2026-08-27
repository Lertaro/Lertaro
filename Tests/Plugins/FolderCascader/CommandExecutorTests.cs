using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests;

// PluginSettingsService.GetSettingFunc/SetSettingFunc are shared static delegates -- [DoNotParallelize]
// plus resetting in TestCleanup keeps tests in this class from racing on them, matching the established
// pattern elsewhere (e.g. FileFiltersSearchableItemProviderTests).
[TestClass]
[DoNotParallelize]
public sealed class CommandExecutorTests
{
    private const string PluginId = "Lertaro.Plugins.FolderCascader";
    private const string TestEnvironmentVariable = "LERTARO_FOLDER_CASCADER_TEST_ROOT";
    private string? _originalEnvironmentValue;

    [TestInitialize]
    public void CaptureEnvironment() => _originalEnvironmentValue = Environment.GetEnvironmentVariable(TestEnvironmentVariable);

    [TestCleanup]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        PluginSettingsService.SetSettingFunc = null;
        Environment.SetEnvironmentVariable(TestEnvironmentVariable, _originalEnvironmentValue);
    }

    [TestMethod]
    public void AddCurrentFolder_ExistingDirectory_AppendsToFoldersSettingAtRoot()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) =>
        {
            if (pluginId == PluginId && key == "Folders")
                saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        };

        CommandExecutor.AddCurrentFolder(Path.GetTempPath(), "");

        var added = saved!.Single();
        Assert.AreEqual(Path.GetTempPath(), added.Path);
        Assert.AreEqual("", added.SubMenu);
    }

    [TestMethod]
    public void AddCurrentFolder_EnvironmentVariablePath_SavesOriginalPath()
    {
        Environment.SetEnvironmentVariable(TestEnvironmentVariable, Path.GetTempPath());
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) =>
            saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        var configuredPath = $"%{TestEnvironmentVariable}%";
        CommandExecutor.AddCurrentFolder(configuredPath, "");

        Assert.AreEqual(configuredPath, saved!.Single().Path);
    }

    [TestMethod]
    public void AddCurrentFolder_ValidVirtualFolder_AppendsToFoldersSetting()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) =>
            saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        const string virtualFolder = "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}";
        CommandExecutor.AddCurrentFolder(virtualFolder, "");

        Assert.AreEqual(virtualFolder, saved!.Single().Path);
    }

    [TestMethod]
    public void AddCurrentFolder_NestedSubMenu_IsSavedOnTheEntry()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        CommandExecutor.AddCurrentFolder(Path.GetTempPath(), "Tools/Network");

        Assert.AreEqual("Tools/Network", saved!.Single().SubMenu);
    }

    [TestMethod]
    public void AddCurrentFolder_NameProvided_IsSavedOnTheEntry()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        CommandExecutor.AddCurrentFolder(Path.GetTempPath(), "", "Custom Name");

        Assert.AreEqual("Custom Name", saved!.Single().Name);
    }

    [TestMethod]
    public void AddCurrentFolder_NoNameProvided_DefaultsToEmpty()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        CommandExecutor.AddCurrentFolder(Path.GetTempPath(), "");

        Assert.AreEqual("", saved!.Single().Name);
    }

    [TestMethod]
    public void AddCurrentFolder_PreservesExistingEntries()
    {
        var existing = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            new() { Name = "Existing", Path = @"C:\Existing" }
        };
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Folders" ? existing : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;

        CommandExecutor.AddCurrentFolder(Path.GetTempPath(), "");

        Assert.IsNotNull(saved);
        Assert.HasCount(2, saved);
        Assert.IsTrue(saved.Any(f => f.Path == @"C:\Existing"));
        Assert.IsTrue(saved.Any(f => f.Path == Path.GetTempPath()));
    }

    [TestMethod]
    public void AddCurrentFolder_NonExistentPath_DoesNotSave()
    {
        var called = false;
        PluginSettingsService.SetSettingFunc = (_, _, _) => called = true;

        CommandExecutor.AddCurrentFolder(@"Z:\definitely-not-a-real-lertaro-dir", "");

        Assert.IsFalse(called);
    }

    [TestMethod]
    public void AddCurrentFolder_EmptyPath_DoesNotSave()
    {
        var called = false;
        PluginSettingsService.SetSettingFunc = (_, _, _) => called = true;

        CommandExecutor.AddCurrentFolder("", "");

        Assert.IsFalse(called);
    }
}
