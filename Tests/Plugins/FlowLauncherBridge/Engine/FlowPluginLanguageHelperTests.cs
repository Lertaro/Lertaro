using System.Globalization;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginLanguageHelperTests
{
    [TestMethod]
    public void FindLanguageFile_MatchesCultureOrFallsBackToEnglish()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_lang_test_{Guid.NewGuid():N}");
        var langDir = Path.Combine(tempDir, "Languages");
        Directory.CreateDirectory(langDir);

        try
        {
            File.WriteAllText(Path.Combine(langDir, "zh-cn.xaml"), "<ResourceDictionary />");
            File.WriteAllText(Path.Combine(langDir, "en.xaml"), "<ResourceDictionary />");

            var zhFile = FlowPluginLanguageHelper.FindLanguageFile(langDir, new CultureInfo("zh-CN"));
            var jaFile = FlowPluginLanguageHelper.FindLanguageFile(langDir, new CultureInfo("ja-JP"));

            Assert.IsTrue(zhFile.EndsWith("zh-cn.xaml", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(jaFile.EndsWith("en.xaml", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void LoadPluginLanguage_PopulatesTranslationCacheAndResolvesViaGetTranslation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flow_lang_cache_test_{Guid.NewGuid():N}");
        var langDir = Path.Combine(tempDir, "Languages");
        Directory.CreateDirectory(langDir);

        try
        {
            var xaml = """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                    xmlns:system="clr-namespace:System;assembly=mscorlib">
                    <system:String x:Key="flowlauncher_test_title">测试标题</system:String>
                    <system:String x:Key="flowlauncher_test_desc">测试描述</system:String>
                </ResourceDictionary>
                """;
            File.WriteAllText(Path.Combine(langDir, "zh-cn.xaml"), xaml);

            FlowPluginLanguageHelper.LoadPluginLanguage(tempDir);

            var title = FlowPluginLanguageHelper.GetTranslation("flowlauncher_test_title");
            var desc = FlowPluginLanguageHelper.GetTranslation("flowlauncher_test_desc");
            var nonExistent = FlowPluginLanguageHelper.GetTranslation("non_existent_key");

            Assert.AreEqual("测试标题", title);
            Assert.AreEqual("测试描述", desc);
            Assert.AreEqual("non_existent_key", nonExistent);

            var api = new FlowPublicApi(new PluginMetadata { ID = "TEST" }, new FlowSettingsStorage(tempDir), () => []);
            Assert.AreEqual("测试标题", api.GetTranslation("flowlauncher_test_title"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
