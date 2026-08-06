using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

// Whether the folder the user was last browsing is worth offering as a tab. The listing itself goes
// through the host's index and cannot be reached from a test; this is the decision in front of it.
[TestClass]
public sealed class LastDirectoryTabProviderTests
{
    [TestMethod]
    public void ARealFolderIsWorthShowing()
    {
        using var dir = new TempDirectory();

        Assert.IsTrue(LastDirectoryTabProvider.IsWorthShowing(dir.Path));
    }

    [TestMethod]
    public void NothingBrowsedYet_IsNot()
    {
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(null));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(string.Empty));
    }

    // The tracker records where the user went; the folder can have been deleted or unplugged since.
    [TestMethod]
    public void AFolderThatIsGone_IsNot()
        => Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(
            Path.Combine(Path.GetTempPath(), "lertaro-no-such-folder")));

    // Explorer and every file dialog land on the Desktop before anything more specific is browsed to,
    // so treating it as "last visited" would leave this tab permanently showing a place nobody chose.
    [TestMethod]
    public void TheDesktopIsNot_HoweverItIsSpelled()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop + "\\"));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop.ToUpperInvariant()));
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
