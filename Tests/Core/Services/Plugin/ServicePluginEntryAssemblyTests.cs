using Lertaro.Core.Services.Plugin.Loading;

namespace Lertaro.Core.Tests.Services.Plugin;

// The trust boundary for Plugins/. Everything under that directory sits next to the installation and is
// loaded into the service/hook process, so the naming rule below is the only thing separating a plugin
// entry assembly from a bundled dependency. These pin the rule against the DLL names that actually live
// there, because getting it wrong is silent: a dependency loaded as a plugin is probed for our contracts,
// and -- worse -- the service and the App would then register different provider sets, shifting every
// AliasProviderRegistry id assigned after the difference and misattributing already-baked alias data.
[TestClass]
public sealed class ServicePluginEntryAssemblyTests
{
    [TestMethod]
    [DataRow(@"C:\app\Plugins\PinyinAlias\Lertaro.Plugins.PinyinAlias.dll")]
    [DataRow(@"C:\app\Plugins\Lertaro.Plugins.CoreExtensions.dll")]
    [DataRow("Lertaro.Plugins.Files.dll")]
    [DataRow(@"Plugins\BrowserData\Lertaro.Plugins.BrowserData.dll")]
    public void PluginEntryAssembly_IsRecognised(string path)
        => Assert.IsTrue(ServicePluginLoader.IsPluginEntryAssembly(path));

    [TestMethod]
    [DataRow(@"C:\app\Plugins\lertaro.plugins.files.dll")]
    [DataRow(@"C:\app\Plugins\LERTARO.PLUGINS.FILES.DLL")]
    public void PluginEntryAssembly_CaseInsensitiveOnWindowsFilenames(string path)
        // Windows filenames are case-insensitive, so a differently-cased copy of the same entry assembly
        // is the same plugin -- treating it as a dependency would silently drop a provider.
        => Assert.IsTrue(ServicePluginLoader.IsPluginEntryAssembly(path));

    [TestMethod]
    [DataRow(@"C:\app\Plugins\BrowserData\Microsoft.Data.Sqlite.dll")]
    [DataRow(@"C:\app\Plugins\BrowserData\SQLitePCLRaw.core.dll")]
    [DataRow(@"C:\app\Plugins\BrowserData\e_sqlite3.dll")]
    [DataRow(@"C:\app\Plugins\ContentSearch\UglyToad.PdfPig.dll")]
    [DataRow(@"C:\app\Plugins\FlowLauncherBridge\Flow.Launcher.Plugin.dll")]
    [DataRow(@"C:\app\Plugins\Lertaro.PluginSdk.dll")]
    [DataRow(@"C:\app\Plugins\SomeOther.Plugins.Thing.dll")]
    public void BundledDependency_IsNotTreatedAsAPlugin(string path)
        => Assert.IsFalse(ServicePluginLoader.IsPluginEntryAssembly(path));

    [TestMethod]
    public void SelectPluginEntryAssemblies_KeepsOnlyEntriesInStableOrder()
    {
        // A real Plugins/ tree: every plugin's own directory holds its entry assembly next to its bundled
        // dependencies. Only the entries may be loaded and probed for providers.
        string[] tree =
        [
            @"C:\app\Plugins\PinyinAlias\Lertaro.Plugins.PinyinAlias.dll",
            @"C:\app\Plugins\BrowserData\Lertaro.Plugins.BrowserData.dll",
            @"C:\app\Plugins\BrowserData\Microsoft.Data.Sqlite.dll",
            @"C:\app\Plugins\BrowserData\e_sqlite3.dll",
            @"C:\app\Plugins\BrowserData\SQLitePCLRaw.core.dll",
            @"C:\app\Plugins\ContentSearch\Lertaro.Plugins.ContentSearch.dll",
            @"C:\app\Plugins\ContentSearch\UglyToad.PdfPig.dll",
            @"C:\app\Plugins\FlowLauncherBridge\Flow.Launcher.Plugin.dll",
        ];

        var selected = ServicePluginLoader.SelectPluginEntryAssemblies(tree);

        CollectionAssert.AreEqual(
            new[]
            {
                @"C:\app\Plugins\BrowserData\Lertaro.Plugins.BrowserData.dll",
                @"C:\app\Plugins\ContentSearch\Lertaro.Plugins.ContentSearch.dll",
                @"C:\app\Plugins\PinyinAlias\Lertaro.Plugins.PinyinAlias.dll",
            },
            selected);

        // Ordering is part of the decision, not a nicety: AliasProviderRegistry assigns ids by
        // registration order, so a shuffled enumeration would reassign ids between restarts.
        CollectionAssert.AreEqual(
            selected,
            ServicePluginLoader.SelectPluginEntryAssemblies(tree.Reverse()));
    }

    [TestMethod]
    public void SelectPluginEntryAssemblies_NoEntries_IsEmpty()
        => Assert.IsEmpty(ServicePluginLoader.SelectPluginEntryAssemblies(
            [@"C:\app\Plugins\BrowserData\Microsoft.Data.Sqlite.dll", @"C:\app\Plugins\BrowserData\e_sqlite3.dll"]));
}
