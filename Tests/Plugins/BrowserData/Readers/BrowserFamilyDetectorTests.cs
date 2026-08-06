using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class BrowserFamilyDetectorTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Detect_PlacesSqlitePresent_ReturnsFirefox()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "places.sqlite"), "");

        Assert.AreEqual(BrowserFamily.Firefox, BrowserFamilyDetector.Detect(dir.Path));
    }

    [TestMethod]
    public void Detect_BookmarksFilePresent_ReturnsChromium()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "Bookmarks"), "{}");

        Assert.AreEqual(BrowserFamily.Chromium, BrowserFamilyDetector.Detect(dir.Path));
    }

    [TestMethod]
    public void Detect_HistoryFilePresent_ReturnsChromium()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "History"), "");

        Assert.AreEqual(BrowserFamily.Chromium, BrowserFamilyDetector.Detect(dir.Path));
    }

    [TestMethod]
    public void Detect_NoMarkerFiles_ReturnsUnknown()
    {
        using var dir = new TempDirectory();

        Assert.AreEqual(BrowserFamily.Unknown, BrowserFamilyDetector.Detect(dir.Path));
    }

    [TestMethod]
    public void Detect_BothPlacesAndBookmarksPresent_FirefoxTakesPriority()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "places.sqlite"), "");
        File.WriteAllText(Path.Combine(dir.Path, "Bookmarks"), "{}");

        Assert.AreEqual(BrowserFamily.Firefox, BrowserFamilyDetector.Detect(dir.Path));
    }
}
