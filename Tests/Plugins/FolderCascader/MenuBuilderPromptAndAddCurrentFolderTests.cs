using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

// Wires PluginSettingsService.GetSettingFunc/SetSettingFunc/PluginPromptService.PromptFunc (shared static
// delegates) -- [DoNotParallelize] keeps it from racing against other tests in this class touching the
// same statics.
[TestClass]
[DoNotParallelize]
public sealed class MenuBuilderPromptAndAddCurrentFolderTests
{
    [TestMethod]
    public void PromptAndAddCurrentFolder_PromptsWithAllThreeFieldsPreFilled()
    {
        using var dir = new TempDirectory();
        var subDir = Directory.CreateDirectory(Path.Combine(dir.Path, "MyStuff"));
        IReadOnlyList<PluginConfigField>? promptedFields = null;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
        {
            promptedFields = fields;
            return null; // cancel -- this test only cares about what was asked, not the save
        };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(subDir.FullName, "Tools/Network");

            Assert.IsNotNull(promptedFields);
            Assert.HasCount(3, promptedFields);
            var nameField = promptedFields.Single(f => f.Key == "Name");
            Assert.AreEqual(ConfigFieldType.Text, nameField.FieldType);
            Assert.AreEqual("MyStuff", nameField.DefaultValue);

            var pathField = promptedFields.Single(f => f.Key == "Path");
            Assert.AreEqual(ConfigFieldType.FolderPath, pathField.FieldType);
            Assert.AreEqual(subDir.FullName, pathField.DefaultValue);

            var subMenuField = promptedFields.Single(f => f.Key == "SubMenu");
            Assert.AreEqual(ConfigFieldType.Text, subMenuField.FieldType);
            Assert.AreEqual("Tools/Network", subMenuField.DefaultValue);
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void PromptAndAddCurrentFolder_PromptConfirmed_SavesEnteredNamePathAndSubMenu()
    {
        using var dir = new TempDirectory();
        using var editedDir = new TempDirectory();
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            new Dictionary<string, object?> { ["Name"] = "Custom Name", ["Path"] = editedDir.Path, ["SubMenu"] = "NewCategory" };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(dir.Path, "");

            var added = saved!.Single();
            Assert.AreEqual("Custom Name", added.Name);
            Assert.AreEqual(editedDir.Path, added.Path);
            Assert.AreEqual("NewCategory", added.SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void PromptAndAddCurrentFolder_EditedPathClearedToBlank_FallsBackToOriginalFolder()
    {
        using var dir = new TempDirectory();
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            new Dictionary<string, object?> { ["Name"] = "", ["Path"] = "   ", ["SubMenu"] = "" };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(dir.Path, "");

            Assert.AreEqual(dir.Path, saved!.Single().Path);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }
}
