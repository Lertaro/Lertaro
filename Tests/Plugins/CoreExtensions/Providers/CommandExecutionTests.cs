using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class CommandExecutionTests
{
    private readonly Func<string, string, object?, object?>? _oldSettings = PluginSettingsService.GetSettingFunc;
    private readonly Func<string, IReadOnlyList<PluginConfigField>, IReadOnlyDictionary<string, object?>?, IReadOnlyDictionary<string, object?>?>? _oldPrompt = PluginPromptService.PromptFunc;

    [TestCleanup]
    public void Cleanup()
    {
        PluginSettingsService.GetSettingFunc = _oldSettings;
        PluginPromptService.PromptFunc = _oldPrompt;
    }

    [TestMethod]
    [DataRow("# dir", false, "dir", true)]
    [DataRow("$ dir", true, "dir", false)]
    [DataRow("Get-ChildItem", true, "Get-ChildItem", true)]
    [DataRow("", false, "", false)]
    public void Prompt_UsesSelectionAndLetsPrefixOverrideElevation(string input, bool checkbox, string expectedCommand, bool expectedAdmin)
    {
        PluginPromptService.PromptFunc = (_, _, _) => new Dictionary<string, object?>
        {
            ["Command"] = input, ["Admin"] = checkbox, ["CommandShell"] = "pwsh"
        };
        var calls = 0;
        CommandQuickNavigationProvider.Prompt(@"D:\source", (command, shell, admin, current, directory) =>
        {
            calls++;
            Assert.AreEqual(expectedCommand, command);
            Assert.AreEqual("pwsh", shell);
            Assert.AreEqual(expectedAdmin, admin);
            Assert.IsTrue(current);
            Assert.AreEqual(@"D:\source", directory);
        });
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void Prompt_CancelDoesNotLaunch()
    {
        PluginPromptService.PromptFunc = (_, _, _) => null;
        CommandQuickNavigationProvider.Prompt(@"D:\source", (_, _, _, _, _) => Assert.Fail("Cancelled"));
    }

    [TestMethod]
    [DataRow("# pwsh", true)]
    [DataRow("$ Get-ChildItem", false)]
    public void GetInstantResults_CallbackRetainsQueryDirectoryAndConfiguredShell(string query, bool expectedAdmin)
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key switch
        {
            "CommandShell" => "pwsh", "CommandUseCurrentDirectory" => true, _ => fallback
        };
        (string Command, string Shell, bool Admin, bool Current, string? Directory)? called = null;
        IInstantResultProvider provider = new CommandInstantProvider((command, shell, admin, current, directory) =>
            called = (command, shell, admin, current, directory));
        var item = Assert.ContainsSingle(provider.GetInstantResults(query, @"D:\original"));
        PluginSettingsService.GetSettingFunc = (_, _, fallback) => fallback;
        Assert.IsNull(called);
        Assert.IsNotNull(item.OnExecute);
        item.OnExecute();
        Assert.IsNotNull(called);
        Assert.AreEqual(query[1..].Trim(), called.Value.Command);
        Assert.AreEqual("pwsh", called.Value.Shell);
        Assert.AreEqual(expectedAdmin, called.Value.Admin);
        Assert.IsTrue(called.Value.Current);
        Assert.AreEqual(@"D:\original", called.Value.Directory);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("$")]
    [DataRow("# ")]
    [DataRow("pwsh")]
    public void GetInstantResults_NoPrefixedCommand_ReturnsNoResult(string query) =>
        Assert.IsEmpty(new CommandInstantProvider().GetInstantResults(query));

    [TestMethod]
    public void QuickNavigation_CapturesSourceDirectoryBeforeMenuCloses()
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "CommandShowInQuickNav" ? true : fallback;
        string? launchedIn = null;
        var provider = new CommandQuickNavigationProvider(directory => launchedIn = directory, _ => true);
        var context = new Context { ContextDirectory = @"D:\original" };
        var item = Assert.ContainsSingle(provider.GetMenuItems(context, IntPtr.Zero));
        context.ContextDirectory = @"E:\later";
        provider.ClearSession();
        item.OnExecute!();
        Assert.AreEqual(@"D:\original", launchedIn);
    }

    [TestMethod]
    public void QuickNavigation_UnavailableDirectory_DisablesCommand()
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "CommandShowInQuickNav" ? true : fallback;
        var provider = new CommandQuickNavigationProvider(_ => Assert.Fail("Must not launch"), _ => false);
        var item = Assert.ContainsSingle(provider.GetMenuItems(new Context(), IntPtr.Zero));
        Assert.IsTrue(item.IsDisabled);
    }

    private sealed class Context : ISearchResult
    {
        public string Name => "folder";
        public string FullPath => @"C:\fallback";
        public string ContextDirectory { get; set; } = "";
        public bool IsDir => true;
        public bool IsApplication => false;
    }
}
