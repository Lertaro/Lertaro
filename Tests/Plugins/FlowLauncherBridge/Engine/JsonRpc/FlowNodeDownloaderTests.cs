using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowNodeDownloaderTests
{
    [TestMethod]
    public void FindNodeInDir_NonExistentDir_ReturnsNull()
    {
        var result = FlowNodeDownloader.FindNodeInDir(@"C:\non_existent_folder_12345");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void FindNodeInDir_WithNodeExeInRoot_ReturnsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NodeFindTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dummyExe = Path.Combine(tempDir, "node.exe");
            File.WriteAllText(dummyExe, "dummy");

            var found = FlowNodeDownloader.FindNodeInDir(tempDir);
            Assert.IsNotNull(found);
            Assert.AreEqual(dummyExe, found);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void FindNodeInDir_WithNodeExeInDir_ReturnsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NodeFindSubdirTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dummyExe = Path.Combine(tempDir, "node.exe");
            File.WriteAllText(dummyExe, "dummy");

            var found = FlowNodeDownloader.FindNodeInDir(tempDir);
            Assert.IsNotNull(found);
            Assert.AreEqual(dummyExe, found);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void GetDownloadUrl_ReturnsValidHttpsNodeUrl()
    {
        var url = FlowNodeDownloader.GetDownloadUrl();
        Assert.StartsWith("https://nodejs.org/dist/", url);
        Assert.EndsWith(".zip", url);
    }
}
