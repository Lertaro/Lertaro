using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowNpmManagerTests
{
    [TestMethod]
    public void EnsureNpmAndPackagesBackground_WithoutPackageJson_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NpmTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            FlowNpmManager.EnsureNpmAndPackagesBackground(@"C:\dummy\node.exe", tempDir);
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, ".npm_installed")));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
