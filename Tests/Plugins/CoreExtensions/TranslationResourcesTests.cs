using System.Reflection;
using System.Text.Json;
using Lertaro.Plugins.CoreExtensions.Providers.Theming;

namespace Lertaro.Plugins.CoreExtensions.Tests;

// Every translation file ships as an embedded resource and is only ever parsed at runtime, inside a
// catch that used to swallow the failure outright. A single stray comma therefore turned a whole
// locale into "[Key]" on screen with nothing in the logs -- which is exactly what shipped. These
// tests read the resources the same way the app does, so a malformed file fails the build instead.
[TestClass]
public sealed class TranslationResourcesTests
{
    private static readonly Assembly PluginAssembly = typeof(CoreTranslationProvider).Assembly;

    private static IEnumerable<string> TranslationResources()
        => PluginAssembly.GetManifestResourceNames().Where(name => name.Contains(".Translations."));

    [TestMethod]
    public void EveryTranslationResource_IsValidJson()
    {
        var names = TranslationResources().ToList();
        Assert.IsNotEmpty(names, "the plugin must embed its translation files");

        var broken = new List<string>();
        foreach (var name in names)
        {
            using var stream = PluginAssembly.GetManifestResourceStream(name);
            Assert.IsNotNull(stream, $"{name} is listed but cannot be opened");
            try
            {
                JsonSerializer.Deserialize<Dictionary<string, string>>(new StreamReader(stream).ReadToEnd());
            }
            catch (JsonException ex)
            {
                broken.Add($"{name}: {ex.Message}");
            }
        }

        Assert.AreEqual(0, broken.Count, "malformed translation resources:\n" + string.Join("\n", broken));
    }

    // The provider merges App.json and Plugin.json before handing anything to the app. A file that
    // parses but deserializes to null (say, a top-level array) would also leave the locale empty.
    [TestMethod]
    public void EveryTranslationResource_DeserializesToAKeyValueMap()
    {
        foreach (var name in TranslationResources())
        {
            using var stream = PluginAssembly.GetManifestResourceStream(name)!;
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(new StreamReader(stream).ReadToEnd());
            Assert.IsNotNull(dict, $"{name} must deserialize to a key/value map");
            Assert.IsNotEmpty(dict!, $"{name} must not be empty");
        }
    }

    // These keys are the ones the app asks for by literal string, so a rename here silently regresses
    // a shipped string back to "[Key]" without breaking compilation.
    [DataTestMethod]
    [DataRow("Plugins_CoreActionPluginName")]
    [DataRow("CoreExtensions_PluginDesc")]
    [DataRow("CoreExtensions_Config_WildcardFilterPrefixLabel")]
    public void PluginTranslations_ResolveTheKeysTheAppLooksUpByLiteral(string key)
    {
        var translations = new CoreTranslationProvider().GetTranslations("en-US");
        Assert.IsTrue(translations.ContainsKey(key), $"en-US is missing '{key}'");
        Assert.IsFalse(string.IsNullOrWhiteSpace(translations[key]), $"en-US has an empty value for '{key}'");
    }

    // SupportedCultures is what the app iterates to discover locales; it derives them from the same
    // resource names, so a missing locale there means that language never loads at all.
    [TestMethod]
    public void SupportedCultures_CoversEveryEmbeddedLocale()
    {
        var cultures = new CoreTranslationProvider().SupportedCultures;

        CollectionAssert.AreEquivalent(
            new[] { "en-US", "es-ES", "ja-JP", "ko-KR", "zh-CN", "zh-HK", "zh-TW" },
            cultures.ToList());
    }
}
