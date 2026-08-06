using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomCommands.Tests;

// PluginSettingsService.GetSettingFunc is a shared static delegate (the SDK's own seam for the host to
// wire in real settings access) -- safe to set here since it's reset every test, but [DoNotParallelize]
// keeps two tests in this class from racing on it (MSTest parallelizes at the method level by default).
[TestClass]
[DoNotParallelize]
public sealed class CustomCommandsInstantProviderTests
{
    [TestCleanup]
    public void ResetSettingsFunc() => PluginSettingsService.GetSettingFunc = null;

    private static void ConfigureCommands(List<CustomCommandsInstantProvider.CommandItem> commands) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "Lertaro.Plugins.CustomCommands" && key == "Commands" ? commands : defaultValue;

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() =>
        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_NoConfiguredCommands_ReturnsNothing() =>
        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults("build extra args"));

    [TestMethod]
    public void GetInstantResults_MatchingKeyword_SubstitutesAllArgsPlaceholder()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "build", Path = "msbuild.exe", Parameter = "%s" } });

        // "My App.sln" (with a space) so the substituted value actually needs ArgQuoting's quotes --
        // a space-free value like "MyApp.sln" would come back unquoted (Quote's no-op fast path).
        var result = new CustomCommandsInstantProvider().GetInstantResults("build My App.sln").Single();

        Assert.AreEqual("Execute", result.ActionType);
        Assert.AreEqual("msbuild.exe \"My App.sln\"", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DisabledCommand_IsExcluded()
    {
        ConfigureCommands(new() { new() { Enabled = false, Keyword = "build", Path = "x.exe" } });

        Assert.IsEmpty(new CustomCommandsInstantProvider().GetInstantResults("build"));
    }

    [TestMethod]
    public void GetInstantResults_KeywordMatchIsCaseInsensitive()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "Build", Path = "x.exe" } });

        Assert.HasCount(1, new CustomCommandsInstantProvider().GetInstantResults("build").ToList());
    }

    [TestMethod]
    public void GetInstantResults_PathWithSpace_IsQuotedInSimplePath()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = @"C:\Program Files\tool.exe" } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run").Single();

        Assert.AreEqual("\"C:\\Program Files\\tool.exe\"", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_RunAsAdminWithoutWorkingDir_PrefixesRunas()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "elevate", Path = "tool.exe", RunAsAdmin = true } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("elevate").Single();

        Assert.AreEqual("runas:tool.exe", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_WithWorkingDir_ProducesCcExecJsonPayload()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = "tool.exe", Parameter = "%s", WorkingDir = @"C:\work" } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run arg1").Single();

        Assert.StartsWith("cc_exec:", result.ActionArgument);
        using var doc = JsonDocument.Parse(result.ActionArgument["cc_exec:".Length..]);
        Assert.AreEqual("tool.exe", doc.RootElement.GetProperty("Path").GetString());
        Assert.AreEqual(@"C:\work", doc.RootElement.GetProperty("WorkingDir").GetString());
        Assert.AreEqual("arg1", doc.RootElement.GetProperty("Arguments").GetString());
    }

    [TestMethod]
    public void GetInstantResults_RunSilentlyWithoutWorkingDir_AlsoUsesJsonPayload()
    {
        ConfigureCommands(new() { new() { Enabled = true, Keyword = "run", Path = "tool.exe", RunSilently = true } });

        var result = new CustomCommandsInstantProvider().GetInstantResults("run").Single();

        Assert.StartsWith("cc_exec:", result.ActionArgument);
        using var doc = JsonDocument.Parse(result.ActionArgument["cc_exec:".Length..]);
        Assert.IsTrue(doc.RootElement.GetProperty("RunSilently").GetBoolean());
    }

    [TestMethod]
    public void GetHighlightMask_QueryKeywordFoundInText_HighlightsThatSpan()
    {
        var mask = new CustomCommandsInstantProvider().GetHighlightMask("Run Build Tool", "build extra");

        Assert.IsNotNull(mask);
        for (var i = 0; i < mask.Length; i++)
            Assert.AreEqual(i is >= 4 and < 9, mask[i], $"index {i}"); // "Build" spans [4,9)
    }

    [TestMethod]
    public void GetHighlightMask_KeywordNotInText_ReturnsNull() =>
        Assert.IsNull(new CustomCommandsInstantProvider().GetHighlightMask("Run Something", "zzz"));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() =>
        Assert.IsNull(new CustomCommandsInstantProvider().GetHighlightMask("text", ""));
}
