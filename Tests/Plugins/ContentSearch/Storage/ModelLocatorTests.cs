using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
[DoNotParallelize]
public sealed class ModelLocatorTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LertaroModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        ModelLocator.ResetCacheForTesting();
        UserDataService.GetSharedDataDirectoryFunc = () => _tempDir;
    }

    [TestCleanup]
    public void TearDown()
    {
        UserDataService.GetSharedDataDirectoryFunc = null;
        ModelLocator.ResetCacheForTesting();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void GetModelsDirectory_ReturnsSharedSubdirectory()
    {
        var modelsDir = ModelLocator.GetModelsDirectory();

        Assert.IsNotNull(modelsDir);
        Assert.IsTrue(modelsDir.StartsWith(_tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(modelsDir.EndsWith(Path.Combine("Models", "ContentSearch"), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FindModelFile_ReturnsNullWhenNotExist_AndPathWhenExists()
    {
        var missing = ModelLocator.FindModelFile("test_model.onnx");
        Assert.IsNull(missing);

        var modelsDir = ModelLocator.GetModelsDirectory();
        Directory.CreateDirectory(modelsDir);
        var testFile = Path.Combine(modelsDir, "test_model.onnx");
        File.WriteAllText(testFile, "dummy");

        var found = ModelLocator.FindModelFile("test_model.onnx");
        Assert.IsNotNull(found);
        Assert.AreEqual(testFile, found);
    }
}
