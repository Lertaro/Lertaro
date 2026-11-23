using System.Reflection;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowAssemblyLoaderTests
{
    [TestMethod]
    [DataRow("5.3.1.0")]
    [DataRow("5.3.2.0")]
    public void Load_ExistingAndUpdatedSdkReferences_ResolveToCompatibleHostAssembly(string version)
    {
        var directory = Directory.CreateTempSubdirectory("flow-loader-");
        var loader = new FlowAssemblyLoader(directory.FullName);
        try
        {
            var assembly = loader.LoadFromAssemblyName(new AssemblyName($"Flow.Launcher.Plugin, Version={version}, Culture=neutral, PublicKeyToken=null"));
            Assert.AreSame(typeof(Result).Assembly, assembly);
            Assert.IsGreaterThanOrEqualTo(Version.Parse(version), assembly.GetName().Version!);
        }
        finally { loader.Unload(); directory.Delete(); }
    }
}
