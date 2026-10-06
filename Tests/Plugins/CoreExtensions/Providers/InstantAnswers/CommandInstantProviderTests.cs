using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
[DoNotParallelize]
public sealed class CommandInstantProviderTests
{
    private static readonly CommandInstantProvider Provider = new();
    private Func<string, string, object?, object?>? _oldSettings;

    [TestInitialize]
    public void Initialize()
    {
        _oldSettings = PluginSettingsService.GetSettingFunc;
        PluginSettingsService.GetSettingFunc = null;
    }

    [TestCleanup]
    public void Cleanup() => PluginSettingsService.GetSettingFunc = _oldSettings;

    [TestMethod]
    [DataRow("$dir", null)]
    [DataRow("#dir", null)]
    [DataRow("$dir", "")]
    [DataRow("#dir", "")]
    [DataRow("$dir", "   ")]
    [DataRow("#dir", "   ")]
    public void GetInstantResults_MissingCurrentDirectory_UsesUserProfile(string query, string? contextDirectory)
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "CommandUseCurrentDirectory" ? true : fallback;
        (bool Current, string? Directory)? executed = null;
        var provider = new CommandInstantProvider((_, _, _, current, directory) => executed = (current, directory));
        var expectedDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsNotEmpty(expectedDirectory);

        var result = Assert.ContainsSingle(provider.GetInstantResults(query, contextDirectory));
        Assert.IsNotNull(result.OnExecute);
        result.OnExecute();

        Assert.AreEqual((true, expectedDirectory), executed);
        Assert.EndsWith(" · " + expectedDirectory, result.Description);
    }

    [TestMethod]
    public void GetInstantResults_CurrentDirectoryDisabled_DoesNotSelectUserProfile()
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "CommandUseCurrentDirectory" ? false : fallback;
        (bool Current, string? Directory)? executed = null;
        var provider = new CommandInstantProvider((_, _, _, current, directory) => executed = (current, directory));

        var result = Assert.ContainsSingle(provider.GetInstantResults("$dir"));
        Assert.IsNotNull(result.OnExecute);
        result.OnExecute();

        Assert.AreEqual((false, (string?)null), executed);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), result.Description);
    }

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    [DataRow("#dir", true)]
    [DataRow("$dir", false)]
    public void GetInstantResults_PrefixControlsElevationWhenExecuted(string query, bool expectedAdmin)
    {
        (string Command, bool Admin)? executed = null;
        var provider = new CommandInstantProvider((command, _, admin, _, _) => executed = (command, admin));
        var result = Assert.ContainsSingle(provider.GetInstantResults(query));
        Assert.AreEqual("Execute", result.ActionType);
        Assert.IsNull(executed);
        Assert.IsNotNull(result.OnExecute);
        result.OnExecute();
        Assert.AreEqual(("dir", expectedAdmin), executed);
    }

    [TestMethod]
    public void GetInstantResults_NoRecognizedPrefix_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("dir"));

    [TestMethod]
    public void GetInstantResults_PrefixWithNoTarget_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("#"));

    [TestMethod]
    public void GetInstantResults_PrefixWithOnlyWhitespaceTarget_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("#   "));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() => Assert.IsNull(Provider.GetHighlightMask("text", ""));

    [TestMethod]
    public void GetHighlightMask_EmptyTarget_ReturnsAllFalseMask()
    {
        var mask = Provider.GetHighlightMask("text", "#");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }
}
