using Lertaro.Plugins.CoreExtensions.Providers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers;

[TestClass]
public sealed class PanelResultItemTests
{
    [TestMethod]
    public void Constructor_RegularFilePath_DerivesNameFromFileName()
    {
        var item = new PanelResultItem(@"C:\Projects\readme.txt");

        Assert.AreEqual("readme.txt", item.Name);
        Assert.AreEqual(@"C:\Projects\readme.txt", item.FullPath);
        Assert.IsFalse(item.IsDir);
    }

    [TestMethod]
    public void Constructor_ExplicitDisplayName_OverridesDerivedName()
    {
        var item = new PanelResultItem(@"C:\Projects\readme.txt", displayName: "My Readme");

        Assert.AreEqual("My Readme", item.Name);
    }

    [TestMethod]
    public void Constructor_ApplicationLnkPath_StripsLnkExtension()
    {
        var item = new PanelResultItem(@"C:\Start Menu\MyApp.lnk", isApplication: true);

        Assert.AreEqual("MyApp", item.Name);
        Assert.IsTrue(item.IsApplication);
    }

    [TestMethod]
    public void Constructor_NonLnkApplicationPath_KeepsFullFileName()
    {
        var item = new PanelResultItem(@"C:\WindowsApps\App\App.exe", isApplication: true);

        Assert.AreEqual("App.exe", item.Name);
    }

    [TestMethod]
    public void Constructor_RealDirectory_SetsIsDirTrueAndContextDirectoryToSelf()
    {
        using var dir = new TempDirectory();

        var item = new PanelResultItem(dir.Path);

        Assert.IsTrue(item.IsDir);
        Assert.AreEqual(dir.Path, item.ContextDirectory);
    }

    [TestMethod]
    public void Constructor_FilePath_ContextDirectoryIsParent()
    {
        var item = new PanelResultItem(@"C:\Projects\readme.txt");

        Assert.AreEqual(@"C:\Projects", item.ContextDirectory);
    }

    [TestMethod]
    public void Constructor_ApplicationPathThatHappensToBeARealDirectory_IsNotTreatedAsADirectory()
    {
        // isApplication short-circuits the Directory.Exists check entirely -- an app's virtual path
        // (or a real exe path) is never itself "browsable" the way a folder result is.
        using var dir = new TempDirectory();

        var item = new PanelResultItem(dir.Path, isApplication: true);

        Assert.IsFalse(item.IsDir);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
