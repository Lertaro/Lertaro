namespace Lertaro.Plugins.Translator.Tests;

[TestClass]
public sealed class TranslationQueryParserTests
{
    [TestMethod]
    public void Parse_WithoutTargetLanguage_UsesDefaultCulture()
    {
        var result = TranslationQueryParser.Parse("hello world", "zh-CN");

        Assert.AreEqual("zh-CN", result.TargetLanguage);
        Assert.AreEqual("hello world", result.Text);
    }

    [TestMethod]
    public void Parse_WithTargetLanguage_UsesCanonicalCultureName()
    {
        var result = TranslationQueryParser.Parse("en hello world", "zh-CN");

        Assert.AreEqual("en", result.TargetLanguage);
        Assert.AreEqual("hello world", result.Text);
    }

    [TestMethod]
    public void Parse_NormalizesUnderscoreInTargetLanguage()
    {
        var result = TranslationQueryParser.Parse("zh_CN hello", "en");

        Assert.AreEqual("zh-hans", result.TargetLanguage);
        Assert.AreEqual("hello", result.Text);
    }

    [TestMethod]
    public void Parse_UnsupportedCultureRemainsTranslationText()
    {
        var result = TranslationQueryParser.Parse("an apple", "en");

        Assert.AreEqual("en", result.TargetLanguage);
        Assert.AreEqual("an apple", result.Text);
    }

    [TestMethod]
    public void Parse_RecognizesOfficialTargetLanguages()
    {
        var slovak = TranslationQueryParser.Parse("sk hello", "en");
        var slovenian = TranslationQueryParser.Parse("sl hello", "en");

        Assert.AreEqual("sk", slovak.TargetLanguage);
        Assert.AreEqual("hello", slovak.Text);
        Assert.AreEqual("sl", slovenian.TargetLanguage);
        Assert.AreEqual("hello", slovenian.Text);
    }

    [TestMethod]
    public void Parse_UnknownFirstWordRemainsTranslationText()
    {
        var result = TranslationQueryParser.Parse("hello world", "en");

        Assert.AreEqual("en", result.TargetLanguage);
        Assert.AreEqual("hello world", result.Text);
    }

    [TestMethod]
    public void Parse_SingleWordRemainsTranslationText()
    {
        var result = TranslationQueryParser.Parse("ja", "en");

        Assert.AreEqual("en", result.TargetLanguage);
        Assert.AreEqual("ja", result.Text);
    }
}
