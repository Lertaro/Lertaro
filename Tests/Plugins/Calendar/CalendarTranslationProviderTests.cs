namespace Lertaro.Plugins.Calendar.Tests;

/// <summary>
/// Enforces the repo's all-locale rule for this plugin, rather than leaving it to be remembered.
/// </summary>
[TestClass]
public sealed class CalendarTranslationProviderTests
{
    private static readonly string[] ExpectedCultures =
        ["en-US", "es-ES", "ja-JP", "ko-KR", "zh-CN", "zh-HK", "zh-TW"];

    private static readonly string[] PlaceholderKeys =
        ["Calendar_DutyOff", "Calendar_DutyWork", "Calendar_NotificationLate"];

    private readonly CalendarTranslationProvider _provider = new();

    [TestMethod]
    public void SupportedCultures_AreExactlyTheSevenTheAppShips() => CollectionAssert.AreEquivalent(ExpectedCultures, _provider.SupportedCultures.ToList());

    [TestMethod]
    public void EveryLocale_CarriesEveryKeyWithSomethingToRender()
    {
        var reference = _provider.GetTranslations("en-US");
        Assert.IsTrue(reference.Count > 0);

        foreach (var culture in ExpectedCultures)
        {
            var translations = _provider.GetTranslations(culture);

            CollectionAssert.AreEquivalent(
                reference.Keys.ToList(),
                translations.Keys.ToList(),
                $"{culture} is out of step with en-US");

            foreach (var key in reference.Keys)
                Assert.IsFalse(string.IsNullOrWhiteSpace(translations[key]), $"{culture}/{key} is empty");
        }
    }

    [TestMethod]
    public void EveryLocale_KeepsThePlaceholdersOfItsFormattedStrings()
    {
        foreach (var culture in ExpectedCultures)
        {
            var translations = _provider.GetTranslations(culture);
            foreach (var key in PlaceholderKeys)
                StringAssert.Contains(translations[key], "{0}", $"{culture}/{key} lost its argument");
        }
    }

    [TestMethod]
    public void NoLocale_HardcodesATranslationKeyIntoAnother()
    {
        // A value that is itself a key means a copy-paste in the JSON, and it renders as the key text
        // rather than failing, so nothing but a check like this catches it.
        foreach (var culture in ExpectedCultures)
        {
            foreach (var pair in _provider.GetTranslations(culture))
                Assert.IsFalse(
                    pair.Value.StartsWith("Calendar_", StringComparison.Ordinal)
                    || pair.Value.StartsWith("Plugin_Comp_", StringComparison.Ordinal),
                    $"{culture}/{pair.Key} holds a key, not a translation");
        }
    }

    [TestMethod]
    public void GetTranslations_UnknownCultureYieldsNothingRatherThanThrowing() => Assert.AreEqual(0, _provider.GetTranslations("de-DE").Count);

    [TestMethod]
    public void ProviderNameIsNotATranslatedString()
    {
        // The provider's own Name is a discovery label, not UI text, so it must not depend on the
        // translation lookup being wired yet.
        Assert.AreEqual("Calendar Plugin Translation Provider", _provider.Name);
        Assert.AreSame(_provider.GetTranslations("en-US"), _provider.GetTranslations("en-US"), "lookups are cached");
    }

    [TestMethod]
    public void AssemblyNameSatisfiesTheLoaderNamingRule() => StringAssert.StartsWith(
            typeof(CalendarTranslationProvider).Assembly.GetName().Name!,
            "Lertaro.Plugins.",
            StringComparison.Ordinal);
}
