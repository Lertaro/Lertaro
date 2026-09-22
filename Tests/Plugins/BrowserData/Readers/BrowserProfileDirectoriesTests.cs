using Lertaro.Plugins.BrowserData.Readers;

using System.IO;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class BrowserProfileDirectoriesTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static string MakeProfile(string parent, string name, string markerFileName)
    {
        var dir = Directory.CreateDirectory(Path.Combine(parent, name)).FullName;
        File.WriteAllText(Path.Combine(dir, markerFileName), "{}");
        return dir;
    }

    [TestMethod]
    public void Discover_FolderIsItselfAProfile_ReturnsOnlyThatFolder()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "Bookmarks"), "{}");

        var found = BrowserProfileDirectories.Discover(dir.Path);

        Assert.HasCount(1, found);
        Assert.AreEqual(dir.Path, found[0]);
    }

    [TestMethod]
    public void Discover_ParentOfSeveralProfiles_ReturnsEachOne()
    {
        using var dir = new TempDirectory();
        MakeProfile(dir.Path, "Profile 1", "Bookmarks");
        MakeProfile(dir.Path, "Default", "History");
        // What else sits in a real "User Data" folder: caches, crash reports, component data.
        Directory.CreateDirectory(Path.Combine(dir.Path, "GrShaderCache"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "Crashpad"));

        var found = BrowserProfileDirectories.Discover(dir.Path);

        Assert.HasCount(2, found);
        Assert.AreEqual(Path.Combine(dir.Path, "Default"), found[0]);
        Assert.AreEqual(Path.Combine(dir.Path, "Profile 1"), found[1]);
    }

    [TestMethod]
    public void Discover_RandomlyNamedFirefoxProfileFolders_AreAllFound()
    {
        using var dir = new TempDirectory();
        MakeProfile(dir.Path, "abc12345.default-release", "places.sqlite");
        MakeProfile(dir.Path, "zyx98765.default", "places.sqlite");
        // Firefox keeps a profile-less stub folder next to the real ones.
        Directory.CreateDirectory(Path.Combine(dir.Path, "k7cm2jgl.profiles"));
        File.WriteAllText(Path.Combine(dir.Path, "k7cm2jgl.profiles", "times.json"), "{}");

        var found = BrowserProfileDirectories.Discover(dir.Path);

        Assert.HasCount(2, found);
    }

    [TestMethod]
    public void Discover_FolderHoldingNoProfiles_ReturnsNothing()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));

        Assert.IsEmpty(BrowserProfileDirectories.Discover(dir.Path));
    }
}
