using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class QuickNavigationPathResolverTests
{
    // Mirrors the private field names (`_nodeMap`/`_commandMap`) QuickNavigationPathResolver looks for
    // via reflection on any real IQuickNavigationProvider implementation.
    private sealed class FakeProvider : IQuickNavigationProvider
    {
        public string GroupName => "Fake";
        public bool CanProvide(ISearchResult result) => true;
        public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu) => Array.Empty<DynamicMenuItem>();
        public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd) { }
        public void ClearSession() { }

        private readonly Dictionary<IntPtr, string> _nodeMap = new();
        private readonly Dictionary<uint, string> _commandMap = new();

        public void SeedNode(IntPtr handle, string path) => _nodeMap[handle] = path;
        public void SeedCommand(uint commandId, string path) => _commandMap[commandId] = path;
    }

    private sealed class NoFieldsProvider : IQuickNavigationProvider
    {
        public string GroupName => "NoFields";
        public bool CanProvide(ISearchResult result) => true;
        public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu) => Array.Empty<DynamicMenuItem>();
        public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd) { }
        public void ClearSession() { }
    }

    [TestMethod]
    public void TryResolveSubMenuPath_HandleInNodeMapAsString_ReturnsPath()
    {
        var provider = new FakeProvider();
        var handle = new IntPtr(42);
        provider.SeedNode(handle, @"C:\folder");

        Assert.AreEqual(@"C:\folder", QuickNavigationPathResolver.TryResolveSubMenuPath(provider, handle));
    }

    [TestMethod]
    public void TryResolveSubMenuPath_HandleNotInNodeMap_ReturnsNull()
    {
        var provider = new FakeProvider();

        Assert.IsNull(QuickNavigationPathResolver.TryResolveSubMenuPath(provider, new IntPtr(999)));
    }

    [TestMethod]
    public void TryResolveSubMenuPath_ProviderWithNoNodeMapField_ReturnsNullInsteadOfThrowing() =>
        Assert.IsNull(QuickNavigationPathResolver.TryResolveSubMenuPath(new NoFieldsProvider(), new IntPtr(1)));

    [TestMethod]
    public void TryResolveCommandPath_CommandInCommandMap_ReturnsPath()
    {
        var provider = new FakeProvider();
        provider.SeedCommand(7, @"C:\other");

        Assert.AreEqual(@"C:\other", QuickNavigationPathResolver.TryResolveCommandPath(provider, 7));
    }

    [TestMethod]
    public void TryResolveCommandPath_CommandNotInCommandMap_ReturnsNull()
    {
        var provider = new FakeProvider();

        Assert.IsNull(QuickNavigationPathResolver.TryResolveCommandPath(provider, 123));
    }

    [TestMethod]
    public void TryResolveCommandPath_ProviderWithNoCommandMapField_ReturnsNullInsteadOfThrowing() =>
        Assert.IsNull(QuickNavigationPathResolver.TryResolveCommandPath(new NoFieldsProvider(), 1));
}
