using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomCommands.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CustomCommandsQuickNavProviderTests
{
    private readonly Func<string, string, object?, object?>? _oldSettings = PluginSettingsService.GetSettingFunc;

    [TestCleanup]
    public void Cleanup()
    {
        PluginSettingsService.GetSettingFunc = _oldSettings;
        QuickNavIcon.Invalidate();
    }

    [STATestMethod]
    public void MenuCallback_RetainsSourceDirectoryAfterSessionEnds()
    {
        var command = Configure(true);
        string? launchedIn = null;
        var provider = new CustomCommandsQuickNavProvider((item, directory) =>
        {
            Assert.AreSame(command, item);
            launchedIn = directory;
        }, _ => true);
        var context = new Context { ContextDirectory = @"D:\original" };
        var menuItem = Assert.ContainsSingle(provider.GetMenuItems(context, IntPtr.Zero));
        Assert.IsFalse(menuItem.IsDisabled);
        context.ContextDirectory = @"E:\later";
        provider.ClearSession();
        menuItem.OnExecute!();
        Assert.AreEqual(@"D:\original", launchedIn);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MissingDirectory_DisablesOnlyCommandsThatRequireIt(bool current)
    {
        Configure(current);
        var provider = new CustomCommandsQuickNavProvider((_, _) => Assert.Fail("Must not launch while rendering"), _ => false);
        var item = Assert.ContainsSingle(provider.GetMenuItems(new Context(), IntPtr.Zero));
        Assert.AreEqual(current, item.IsDisabled);
    }

    private static CustomCommandsInstantProvider.CommandItem Configure(bool current)
    {
        var command = new CustomCommandsInstantProvider.CommandItem { Path = "tool.exe", ShowInQuickNav = true, UseCurrentDirectory = current };
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "Commands"
            ? new List<CustomCommandsInstantProvider.CommandItem> { command } : fallback;
        return command;
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
