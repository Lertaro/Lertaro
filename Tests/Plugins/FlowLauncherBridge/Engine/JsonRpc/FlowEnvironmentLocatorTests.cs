using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
[DoNotParallelize]
public sealed class FlowEnvironmentLocatorTests
{
    private string? _tempShared;

    [TestInitialize]
    public void Setup()
    {
        FlowEnvironmentLocator.ResetCache();
        _tempShared = Path.Combine(Path.GetTempPath(), "LertaroTestShared_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempShared);

        UserDataService.GetSharedDataDirectoryFunc = () => _tempShared;
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowEnvironmentLocator.ResetCache();
        UserDataService.GetSharedDataDirectoryFunc = null;

        try { if (_tempShared != null && Directory.Exists(_tempShared)) Directory.Delete(_tempShared, true); } catch { }
    }

    [TestMethod]
    public void GetEmbeddedPythonDirectory_ResolvesUnderSharedDataDirectory()
    {
        var dir = FlowEnvironmentLocator.GetEmbeddedPythonDirectory();
        Assert.IsTrue(dir.StartsWith(_tempShared!, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("PythonEmbeded-", dir);
    }

    [TestMethod]
    public void GetEmbeddedNodeDirectory_ResolvesUnderSharedDataDirectory()
    {
        var dir = FlowEnvironmentLocator.GetEmbeddedNodeDirectory();
        Assert.IsTrue(dir.StartsWith(_tempShared!, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("NodeEmbeded-", dir);
    }

    [TestMethod]
    public void FindPythonExecutable_WhenInSharedDirectory_ReturnsSharedPath()
    {
        var sharedEmbed = FlowEnvironmentLocator.GetEmbeddedPythonDirectory();
        Directory.CreateDirectory(sharedEmbed);
        var dummyExe = Path.Combine(sharedEmbed, "python.exe");
        File.WriteAllText(dummyExe, "dummy");

        var found = FlowEnvironmentLocator.FindPythonExecutable();
        Assert.AreEqual(dummyExe, found);
    }

    [TestMethod]
    public void FindNodeExecutable_WhenInSharedDirectory_ReturnsSharedPath()
    {
        var sharedEmbed = FlowEnvironmentLocator.GetEmbeddedNodeDirectory();
        Directory.CreateDirectory(sharedEmbed);
        var dummyExe = Path.Combine(sharedEmbed, "node.exe");
        File.WriteAllText(dummyExe, "dummy");

        var found = FlowEnvironmentLocator.FindNodeExecutable();
        Assert.AreEqual(dummyExe, found);
    }
}
