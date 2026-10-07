using System.IO;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class PluginLoaderHelperTests
{
    [TestMethod]
    public void DisabledPluginCard_IsAvailableWithoutOpeningDllOrCreatingConfig()
    {
        var directory = Directory.CreateTempSubdirectory("Lertaro-disabled-card-");
        try
        {
            const string name = "Lertaro.Plugins.Disabled.dll";
            using var locked = new FileStream(Path.Combine(directory.FullName, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var plugins = new List<PluginInfoViewModel>();
            var settings = new UserSettings { DisabledPluginAssemblies = [name] };
            PluginLoaderHelper.AddDisabledPlugins(plugins, directory.FullName, settings);
            PluginLoaderHelper.AddDisabledPlugins(plugins, directory.FullName, settings);
            Assert.HasCount(1, plugins);
            Assert.IsFalse(plugins[0].IsPluginEnabled);
            Assert.IsFalse(plugins[0].IsPluginEnablementDirty);
            Assert.IsEmpty(plugins[0].ConfigFields);
            Assert.IsNull(plugins[0].OnSave);
            plugins[0].IsPluginEnabled = true;
            PluginManagementViewModel.SavePluginEnablement(settings, plugins);
            Assert.IsEmpty(settings.DisabledPluginAssemblies);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void HostTranslations_ProvideSettingsAndPluginSwitchWithoutCoreExtensionsAssembly()
    {
        var translations = PluginSdk.Services.TranslationService.LoadEmbeddedTranslations(typeof(PluginLoaderHelper).Assembly, "zh-CN", "App");
        Assert.AreEqual("启用插件", translations["Plugins_EnableAssembly"]);
        Assert.IsFalse(string.IsNullOrEmpty(translations["Settings_Title"]));
    }
}
