namespace Lertaro.Plugins.CustomCommands.Tests;

[TestClass]
public sealed class CommandRunnerTests
{
    private static CustomCommandsInstantProvider.CommandItem MakeCommand(string parameter) =>
        new() { Parameter = parameter };

    [TestMethod]
    public void ResolveParameter_NoPlaceholders_ReturnsParameterUnchanged()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("--flag"), "ignored");

        Assert.AreEqual("--flag", result);
    }

    [TestMethod]
    public void ResolveParameter_PercentSPositional_SubstitutesNthArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s1"), "hello world");

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void ResolveParameter_BraceStylePositional_SubstitutesNthArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("{2}"), "a b");

        Assert.AreEqual("b", result);
    }

    [TestMethod]
    public void ResolveParameter_OutOfRangePositional_ResolvesToEmptyNotLiteral()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("[%s5]"), "a");

        Assert.AreEqual("[]", result);
    }

    [TestMethod]
    public void ResolveParameter_DoubleDigitPositional_MatchesWholeNumberGreedily()
    {
        // %s10 must be read as index 10 (out of range here), not %s1 followed by a literal '0'.
        var result = CommandRunner.ResolveParameter(MakeCommand("[%s10]"), "a");

        Assert.AreEqual("[]", result);
    }

    [TestMethod]
    public void ResolveParameter_PercentSAllArgs_SubstitutesWholeSuffixQuoted()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s"), "hello world");

        Assert.AreEqual("\"hello world\"", result);
    }

    [TestMethod]
    public void ResolveParameter_BraceAllArgs_SubstitutesWholeSuffixQuoted()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("{}"), "a b");

        Assert.AreEqual("\"a b\"", result);
    }

    [TestMethod]
    public void ResolveParameter_EmptyArgSuffix_AllArgsPlaceholderResolvesToEmpty()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s"), "");

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void ResolveParameter_QuotedSegmentInArgSuffix_ParsedAsOneArgument()
    {
        var result = CommandRunner.ResolveParameter(MakeCommand("%s1 %s2"), "\"a b\" c");

        Assert.AreEqual("\"a b\" c", result);
    }

    [TestMethod]
    public void ResolveParameter_NullParameter_TreatedAsEmptyTemplate()
    {
        var cmd = new CustomCommandsInstantProvider.CommandItem { Parameter = null! };

        var result = CommandRunner.ResolveParameter(cmd, "anything");

        Assert.AreEqual("", result);
    }
}
