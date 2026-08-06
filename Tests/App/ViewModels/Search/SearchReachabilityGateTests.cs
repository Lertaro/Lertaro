using System.IO;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// Only the pure classification helpers -- BeginSession/ProbeAsync touch a live pipe connection, the WSL
// registry key, and Directory.Exists on real system state, the same non-injectable-real-path hazard
// noted elsewhere in this suite (see NetworkIndexerHelperTests in Core.Tests).
[TestClass]
public sealed class SearchReachabilityGateTests
{
    [TestMethod]
    public void IsResultReachable_EmptyDrive_ReturnsTrue()
    {
        var unreachable = new HashSet<string> { "C" };

        Assert.IsTrue(SearchReachabilityGate.IsResultReachable("", unreachable));
    }

    [TestMethod]
    public void IsResultReachable_NullDrive_ReturnsTrue()
    {
        var unreachable = new HashSet<string> { "C" };

        Assert.IsTrue(SearchReachabilityGate.IsResultReachable(null, unreachable));
    }

    [TestMethod]
    public void IsResultReachable_DriveNotInUnreachableSet_ReturnsTrue()
    {
        var unreachable = new HashSet<string> { "D" };

        Assert.IsTrue(SearchReachabilityGate.IsResultReachable("C", unreachable));
    }

    [TestMethod]
    public void IsResultReachable_DriveInUnreachableSet_ReturnsFalse()
    {
        var unreachable = new HashSet<string> { "D" };

        Assert.IsFalse(SearchReachabilityGate.IsResultReachable("D", unreachable));
    }

    [TestMethod]
    public void IsResultReachable_UnreachableSetEmpty_ReturnsTrue() => Assert.IsTrue(SearchReachabilityGate.IsResultReachable("Z", new HashSet<string>()));

    [TestMethod]
    public void IsNetworkSourceReachable_DriveLetterResolved_ReturnsTrue()
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Z" };

        Assert.IsTrue(SearchReachabilityGate.IsNetworkSourceReachable("Z", resolved, new List<string>()));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_DriveLetterNotResolved_ReturnsFalse()
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Y" };

        Assert.IsFalse(SearchReachabilityGate.IsNetworkSourceReachable("Z", resolved, new List<string>()));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_DriveLetterCaseInsensitive_StillMatches()
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "z" };

        Assert.IsTrue(SearchReachabilityGate.IsNetworkSourceReachable("Z", resolved, new List<string>()));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_WslDistroRunning_ReturnsTrue()
    {
        var distros = new List<string> { "Ubuntu" };

        Assert.IsTrue(SearchReachabilityGate.IsNetworkSourceReachable(@"\\wsl$\Ubuntu", new HashSet<string>(), distros));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_WslDistroNoLongerListed_ReturnsFalse()
    {
        var distros = new List<string> { "Debian" };

        Assert.IsFalse(SearchReachabilityGate.IsNetworkSourceReachable(@"\\wsl$\Ubuntu", new HashSet<string>(), distros));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_WslLocalhostPrefix_AlsoRecognizedAsWsl()
    {
        var distros = new List<string> { "Ubuntu" };

        Assert.IsTrue(SearchReachabilityGate.IsNetworkSourceReachable(@"\\wsl.localhost\Ubuntu", new HashSet<string>(), distros));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_FolderIndexPathExists_ReturnsTrue()
    {
        using var dir = new TempDirectory();

        Assert.IsTrue(SearchReachabilityGate.IsNetworkSourceReachable(dir.Path, new HashSet<string>(), new List<string>()));
    }

    [TestMethod]
    public void IsNetworkSourceReachable_FolderIndexPathDeleted_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "lertaro-tests-nonexistent-folder-index");

        Assert.IsFalse(SearchReachabilityGate.IsNetworkSourceReachable(path, new HashSet<string>(), new List<string>()));
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
