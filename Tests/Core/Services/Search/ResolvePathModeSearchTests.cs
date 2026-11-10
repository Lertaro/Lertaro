using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

// Resolving a path-mode query ("C:\Windows\System...", typed by the user) to the directory a live scan
// should walk and the leftover text that filters it. Split from LiveDirectorySearcherTests solely to keep
// that file within the repository's per-file line limit.
[TestClass]
public sealed class ResolvePathModeSearchTests
{
    [TestMethod]
    public void ResolvePathModeSearch_EmptyInput_ReturnsEmptyTuple()
    {
        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch("");

        Assert.AreEqual(string.Empty, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_WslPath_DoesNotProbeForLiveFallback()
    {
        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch(@"\\wsl$\Ubuntu\home\testuser\file.txt");

        Assert.AreEqual(string.Empty, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_ExistingDirectory_ReturnsItselfWithNoFilter()
    {
        using var tempDir = new TempDirectory();

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch(tempDir.Path);

        Assert.AreEqual(tempDir.Path, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_NonExistentSubPath_ReturnsNearestExistingAncestorAndFilter()
    {
        using var tempDir = new TempDirectory();
        var target = Path.Combine(tempDir.Path, "missing-sub", "file.txt");

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch(target);

        Assert.AreEqual(tempDir.Path, dir);
        Assert.AreEqual(Path.Combine("missing-sub", "file.txt"), filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_NoAncestorExists_ReturnsEmptyTuple()
    {
        var usedLetters = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var freeLetter = Enumerable.Range('A', 26).Select(c => (char)c).First(c => !usedLetters.Contains(c));

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch($@"{freeLetter}:\definitely-not-real\deeper\file.txt");

        Assert.AreEqual(string.Empty, dir);
        Assert.AreEqual(string.Empty, filter);
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
