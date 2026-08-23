using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowPythonDownloaderTests
{
    [TestMethod]
    public void FindPythonInDir_NonExistentDir_ReturnsNull()
    {
        var result = FlowPythonDownloader.FindPythonInDir(@"C:\non_existent_folder_12345");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void FindPythonInDir_WithPythonExe_ReturnsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PyFindTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dummyExe = Path.Combine(tempDir, "pythonw.exe");
            File.WriteAllText(dummyExe, "dummy");

            var found = FlowPythonDownloader.FindPythonInDir(tempDir);
            Assert.IsNotNull(found);
            Assert.AreEqual(dummyExe, found);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
