using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.Core.Tests.Services.Plugin.DirectoryIndex;

// The rule that decides whether a change is worth waking a subscriber for. It now runs on the service's
// side, against the watch list the subscriber sent -- which is the whole point: changes arrive there at
// roughly 3000 batches a second on an ordinary working C:, so the small thing travels to meet the large
// one rather than the other way round.
//
// Getting it wrong is invisible either way: too narrow and a plugin silently never refreshes, too wide
// and every write on the volume costs a full re-listing. The second is what shipped before this.
[TestClass]
public sealed class WatchedDirectoryMatcherTests
{
    private static readonly List<string> Watched = new()
    {
        @"C:\ProgramData\Microsoft\Windows\Start Menu",
        @"C:\Movies",
    };

    // The bug this exists for: a temp file on the same volume is not the Start Menu changing.
    [TestMethod]
    public void AChangeElsewhereOnTheVolumeMatchesNothing()
        => Assert.IsEmpty(WatchedDirectoryMatcher.Match(Watched, new[] { @"C:\Users\me\AppData\Local\Temp" }));

    [TestMethod]
    public void AChangeInsideAWatchedDirectoryMatchesIt()
        => CollectionAssert.AreEqual(
            new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu" },
            WatchedDirectoryMatcher.Match(Watched, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" }));

    // A change to a parent directory (e.g. C:\ or C:\Users\me) is outside the watched directory and must not match it.
    [TestMethod]
    public void AChangeToAParentOfAWatchedDirectoryDoesNotMatchIt()
        => Assert.IsEmpty(WatchedDirectoryMatcher.Match(new List<string> { @"C:\Movies" }, new[] { @"C:\" }));

    // Whole segments only, or a change in one folder would wake somebody watching an unrelated sibling
    // that happens to share its opening characters.
    [TestMethod]
    public void ASiblingWithTheSamePrefixDoesNotMatch()
    {
        Assert.IsFalse(WatchedDirectoryMatcher.Touches(@"D:\Projects\ProjectAB", @"D:\Projects\ProjectA"));
        Assert.IsFalse(WatchedDirectoryMatcher.Touches(@"D:\Projects\ProjectA", @"D:\Projects\ProjectAB"));
    }

    [TestMethod]
    public void EachWatchedDirectoryIsReportedOnce()
    {
        var hits = WatchedDirectoryMatcher.Match(Watched, new[]
        {
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Tools",
        });

        CollectionAssert.AreEqual(new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu" }, hits);
    }

    // Null means the batch could not be pinned to directories -- too wide to enumerate, or a whole tree
    // replaced. Everything watched is returned: a needless re-listing is recoverable, a missed change is
    // not, since nothing retries until the next one.
    [TestMethod]
    public void AChangeThatCannotBePinnedDownMatchesEverythingWatched()
        => CollectionAssert.AreEqual(Watched, WatchedDirectoryMatcher.Match(Watched, null));

    // Including when it cannot be pinned down: a subscriber watching nothing still hears nothing.
    [TestMethod]
    public void WatchingNothingMatchesNothing()
    {
        Assert.IsEmpty(WatchedDirectoryMatcher.Match(new List<string>(), new[] { @"C:\Movies" }));
        Assert.IsEmpty(WatchedDirectoryMatcher.Match(new List<string>(), null));
    }

    [TestMethod]
    public void EmptyPathsNeverMatch()
    {
        Assert.IsFalse(WatchedDirectoryMatcher.Touches("", @"C:\Movies"));
        Assert.IsFalse(WatchedDirectoryMatcher.Touches(@"C:\Movies", ""));
    }

    // A UNC root behaves exactly as a drive-letter path does -- one rule for every index behind it.
    [TestMethod]
    public void ANetworkPathFollowsTheSameRule()
    {
        Assert.IsTrue(WatchedDirectoryMatcher.Touches(@"\\nas\media\music\albums", @"\\nas\media\music"));
        Assert.IsFalse(WatchedDirectoryMatcher.Touches(@"\\nas\media\video", @"\\nas\media\music"));
    }

    [TestMethod]
    public void ForwardSlashesAreNormalized()
    {
        Assert.IsTrue(WatchedDirectoryMatcher.Touches(@"//nas/media/music/albums", @"\\nas\media\music"));
        Assert.IsTrue(WatchedDirectoryMatcher.Touches(@"C:/Movies/Sub", @"C:\Movies"));
    }

    [TestMethod]
    public void PreciseMatchingKeepsChangedSubdirectories()
        => CollectionAssert.AreEqual(
            new[] { @"C:\Movies\Action" },
            WatchedDirectoryMatcher.MatchChangedDirectories(
                new[] { @"C:\Movies" },
                new[] { @"C:\Movies\Action", @"C:\Other" }));

    [TestMethod]
    public void PreciseMatchingFallsBackToWatchedRootsWhenUnknown()
        => CollectionAssert.AreEqual(
            Watched,
            WatchedDirectoryMatcher.MatchChangedDirectories(Watched, null));
}
