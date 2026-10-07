using Lertaro.Core.Services.Plugin.Loading;

namespace Lertaro.Core.Tests.Services.Plugin.Loading;

[TestClass]
public sealed class ServicePluginLoaderTests
{
    [TestMethod]
    public void Discovery_ExcludesDisabledDllsAndDependenciesWithoutOpeningThem()
    {
        var directory = Directory.CreateTempSubdirectory("Lertaro-plugin-discovery-");
        try
        {
            var nested = directory.CreateSubdirectory("nested");
            var disabled = Path.Combine(nested.FullName, "Lertaro.Plugins.Disabled.dll");
            var enabled = Path.Combine(nested.FullName, "Lertaro.Plugins.Enabled.dll");
            File.WriteAllText(enabled, "not an assembly: discovery must not load it");
            File.WriteAllText(Path.Combine(directory.FullName, "Dependency.dll"), "not a plugin");
            File.WriteAllText(Path.Combine(directory.FullName, "Lertaro.Plugins.Other.json"), "not a DLL");
            using (var locked = new FileStream(disabled, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var files = ServicePluginLoader.EnabledAssemblyFiles(directory.FullName, ["lertaro.plugins.disabled.DLL"]).ToArray();
                CollectionAssert.AreEqual(new[] { enabled }, files);
            }
            Assert.HasCount(2, ServicePluginLoader.EnabledAssemblyFiles(directory.FullName, []).ToArray());
        }
        finally { directory.Delete(recursive: true); }
    }
}
