namespace Lertaro.Core.Tests;

[TestClass]
public sealed class IndexRuleSyntaxTests
{
    private static ParsedIndexRule Parse(string input, IndexRuleEffect effect = IndexRuleEffect.Normal)
    {
        Assert.IsTrue(IndexRuleSyntax.TryParse(input, effect, out var rule, out var error), $"parse failed: {error}");
        return rule;
    }

    [TestMethod]
    public void CompletePaths_AreClassifiedAsPath()
    {
        Assert.AreEqual(IndexRuleKind.Path, Parse(@"C:\abc\Def\\").Kind);
        Assert.AreEqual(IndexRuleKind.Path, Parse(@"\\server\share\folder").Kind);
        Assert.AreEqual(IndexRuleKind.Path, Parse(@"%APPDATA%\aaa").Kind);
        Assert.AreEqual(IndexRuleKind.Path, Parse("shell:Music/c").Kind);
        Assert.AreEqual(IndexRuleKind.Path, Parse(":: {20D04FE0-3AEA-1069-A2D8-08002B30309D}").Kind);
    }

    [TestMethod]
    public void CompletePath_RemovesTrailingSeparatorsAndQuotes()
    {
        Assert.AreEqual(@"C:\abc\Def", Parse("  \"C:\\abc\\Def\\\\\"  ").Text);
        Assert.AreEqual(@"C:\", Parse(@"C:\").Text);
    }

    [TestMethod]
    public void SlashSeparators_AreConvertedToBackslashes()
    {
        Assert.AreEqual(@"shell:Music\c", Parse("shell:Music/c").Text);
        Assert.AreEqual(@"C:\abc\def", Parse("C:/abc/def").Text);
    }

    [TestMethod]
    public void Wildcards_AreClassifiedAsGlob()
    {
        Assert.AreEqual(IndexRuleKind.Glob, Parse(@"**\node_modules\**").Kind);
        Assert.AreEqual(IndexRuleKind.Glob, Parse(@"Debug\**").Kind);
        Assert.AreEqual(IndexRuleKind.Glob, Parse("target\\").Kind);
        Assert.AreEqual(IndexRuleKind.Glob, Parse("*.tmp").Kind);
        Assert.AreEqual(IndexRuleKind.Glob, Parse("file?.txt").Kind);
    }

    [TestMethod]
    public void TrailingSeparator_IsRemovedFromGlob()
    {
        Assert.AreEqual("target", Parse("target\\").Text);
    }

    [TestMethod]
    public void ExtendedPathPrefixQuestionMark_IsNotAWildcard()
    {
        Assert.AreEqual(IndexRuleKind.Path, Parse(@"\\?\C:\very\long\path").Kind);
    }

    [TestMethod]
    public void DelimitedInput_IsClassifiedAsRegexAndStoresTheBody()
    {
        var rule = Parse(@"/^\$*/");

        Assert.AreEqual(IndexRuleKind.Regex, rule.Kind);
        Assert.AreEqual(@"^\$*", rule.Pattern);
        Assert.AreEqual(@"/^\$*/", rule.Text);
    }

    [TestMethod]
    public void Regex_DelimitersAreStrippedFromPattern()
    {
        var rule = Parse("/^[^.]+$/");

        Assert.AreEqual("^[^.]+$", rule.Pattern);
    }

    [TestMethod]
    public void InvalidRegex_ReportsInvalidRegex()
    {
        Assert.IsFalse(IndexRuleSyntax.TryParse("/[/", IndexRuleEffect.Normal, out _, out var error));
        Assert.AreEqual(IndexRuleError.InvalidRegex, error);
    }

    [TestMethod]
    public void EmptyRegex_ReportsEmptyRegex()
    {
        Assert.IsFalse(IndexRuleSyntax.TryParse("//", IndexRuleEffect.Normal, out _, out var error));
        Assert.AreEqual(IndexRuleError.EmptyRegex, error);
    }

    [TestMethod]
    public void BlankInput_ReportsEmpty()
    {
        Assert.IsFalse(IndexRuleSyntax.TryParse("  \"  \" ", IndexRuleEffect.Normal, out _, out var error));
        Assert.AreEqual(IndexRuleError.Empty, error);
    }

    [TestMethod]
    public void Effect_IsCarriedThrough()
    {
        Assert.AreEqual(IndexRuleEffect.High, Parse("build\\", IndexRuleEffect.High).Effect);
    }

    [TestMethod]
    public void ResolvePath_ExpandsEnvironmentVariables()
    {
        Assert.IsTrue(IndexRuleSyntax.TryResolvePath(@"%SystemRoot%\temp", out var resolved, out _));
        Assert.IsFalse(resolved.Contains('%'));
    }

    [TestMethod]
    public void ResolvePath_UnresolvedVariable_ReportsError()
    {
        Assert.IsFalse(IndexRuleSyntax.TryResolvePath(@"%LERATO_NO_SUCH_VAR_12345%\x", out _, out var error));
        Assert.AreEqual(IndexRuleError.UnresolvedEnvironmentVariable, error);
    }

    [TestMethod]
    public void ResolvePath_AbsolutePath_ReturnsRootedPath()
    {
        Assert.IsTrue(IndexRuleSyntax.TryResolvePath(@"C:\a\..\b", out var resolved, out _));
        Assert.AreEqual(@"C:\b", resolved);
    }

    [TestMethod]
    public void ResolvePath_KeepsWildcardsUntouched()
    {
        // Path.GetFullPath accepts wildcards, so the resolver leaves them in place; only rules that were
        // classified as paths ever reach it, and those carry no wildcard.
        Assert.IsTrue(IndexRuleSyntax.TryResolvePath(@"C:\build\**", out var resolved, out _));
        Assert.AreEqual(@"C:\build\**", resolved);
    }

    [TestMethod]
    public void Clean_UnwrapsRepeatedQuotes()
    {
        Assert.AreEqual("a b", IndexRuleSyntax.Clean("  \"'a b'\"  "));
    }
}
