namespace Lertaro.Plugins.Translator.Tests;

[TestClass]
public sealed class MicrosoftTranslationResponseParserTests
{
    [TestMethod]
    public void TryParse_ValidResponse_ReturnsTranslationAndDetectedLanguage()
    {
        const string json = "[{\"translations\":[{\"text\":\"你好\",\"to\":\"zh-Hans\"}],\"detectedLanguage\":{\"language\":\"en\"}}]";

        var parsed = MicrosoftTranslationResponseParser.TryParse(json, out var response);

        Assert.IsTrue(parsed);
        Assert.AreEqual("你好", response.Text);
        Assert.AreEqual("en", response.DetectedLanguage);
        Assert.AreEqual("zh-Hans", response.TargetLanguage);
    }

    [TestMethod]
    public void TryParse_ResponseWithoutTranslation_ReturnsFalse() =>
        Assert.IsFalse(MicrosoftTranslationResponseParser.TryParse("[]", out _));

    [TestMethod]
    public void TryParse_ResponseWithoutDetectedLanguage_ReturnsTranslationWithEmptyLanguage()
    {
        const string json = "[{\"translations\":[{\"text\":\"hello\"}]}]";

        var parsed = MicrosoftTranslationResponseParser.TryParse(json, out var response);

        Assert.IsTrue(parsed);
        Assert.AreEqual(string.Empty, response.DetectedLanguage);
    }
}
