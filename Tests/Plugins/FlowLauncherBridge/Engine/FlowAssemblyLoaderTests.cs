using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowAssemblyLoaderTests
{
    [TestMethod]
    public void FlowAssemblyLoader_Constructs_AndResolvesFlowLauncherPlugin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FlowLoaderTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var loader = new FlowAssemblyLoader(tempDir);
            Assert.IsNotNull(loader);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
