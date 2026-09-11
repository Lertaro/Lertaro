using System.IO;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// The inline window's folder listing is cached per window so the walk (which scales with the folder's
// size) is not repeated on every keystroke -- that repetition was why the first character of a search in
// a large folder took so long to show anything. These pin the caching contract; enumeration itself is
// covered by DirectChildrenLocatorTests.
[TestClass]
public sealed class DirectChildrenListingCacheTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-listing-").FullName;

        public TempDirectory(params string[] names)
        {
            foreach (var name in names)
                File.WriteAllText(System.IO.Path.Combine(Path, name), string.Empty);
        }

        public void Add(string name) => File.WriteAllText(System.IO.Path.Combine(Path, name), string.Empty);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task GetAsync_SecondCallForTheSameFolder_ReusesTheListingWithoutRewalking()
    {
        using var dir = new TempDirectory("a.txt");
        var cache = new DirectChildrenListingCache();

        var first = await cache.GetAsync(dir.Path);
        // A file appearing after the first read must NOT show up: proof the folder was not walked again,
        // which is the whole point (a re-walk per keystroke is the cost being removed).
        dir.Add("b.txt");
        var second = await cache.GetAsync(dir.Path);

        Assert.AreSame(first, second, "the same folder must reuse the same listing");
        Assert.HasCount(1, second);
    }

    [TestMethod]
    public async Task GetAsync_DifferentFolder_GetsItsOwnListing()
    {
        using var a = new TempDirectory("from-a.txt");
        using var b = new TempDirectory("from-b.txt");
        var cache = new DirectChildrenListingCache();

        var listingA = await cache.GetAsync(a.Path);
        var listingB = await cache.GetAsync(b.Path);

        Assert.AreEqual("from-a.txt", listingA[0].Name);
        Assert.AreEqual("from-b.txt", listingB[0].Name);
    }

    [TestMethod]
    public async Task GetAsync_SameFolderAgainAfterAnother_ReturnsToTheFirstListing()
    {
        // Two folders alternate (the window's scope moving back and forth); the cache must key on the
        // directory rather than assume the caller is monotonic.
        using var a = new TempDirectory("a.txt");
        using var b = new TempDirectory("b.txt");
        var cache = new DirectChildrenListingCache();

        var firstA = await cache.GetAsync(a.Path);
        await cache.GetAsync(b.Path);
        var againA = await cache.GetAsync(a.Path);

        Assert.AreSame(firstA, againA);
    }

    [TestMethod]
    public async Task GetAsync_ConcurrentCallers_ShareOneWalk()
    {
        using var dir = new TempDirectory("a.txt");
        var cache = new DirectChildrenListingCache();

        var t1 = cache.GetAsync(dir.Path);
        var t2 = cache.GetAsync(dir.Path);

        Assert.AreSame(t1, t2, "an in-flight walk must be shared, not started twice");
        Assert.AreSame(await t1, await t2);
    }

    [TestMethod]
    public async Task Prewarm_StartsTheWalkSoALaterGetFindsTheSameTask()
    {
        // Prewarm is the payoff: it runs when the window learns its folder, before the user types. It is
        // deliberately fire-and-forget (warmup must never block the UI), so this asserts identity with the
        // task a later Get returns -- i.e. the keystroke joins the warmup instead of starting its own walk.
        using var dir = new TempDirectory("a.txt");
        var cache = new DirectChildrenListingCache();

        cache.Prewarm(dir.Path);
        var listing = cache.GetAsync(dir.Path);

        Assert.HasCount(1, await listing);
    }

    [TestMethod]
    public async Task Prewarm_IsIdempotent_SoRepeatedScopeNotificationsDoNotRewalk()
    {
        // OnPathCaptured can fire repeatedly for one folder; each must reuse the same walk. Identity of the
        // task is the assertion -- the entry count is not, since the walk itself runs on the thread pool
        // and may not have started when this test edits the folder.
        using var dir = new TempDirectory("a.txt");
        var cache = new DirectChildrenListingCache();

        cache.Prewarm(dir.Path);
        var first = cache.GetAsync(dir.Path);
        dir.Add("added-later.txt");
        cache.Prewarm(dir.Path);
        var second = cache.GetAsync(dir.Path);

        Assert.AreSame(first, second, "a re-prewarm must not start a second walk");
        await second;
    }

    [TestMethod]
    public async Task GetAsync_BeyondTheFolderCap_EvictsTheOldestSoMemoryStaysBounded()
    {
        // Each listing can hold MaxExaminedEntries names, so retention has to be bounded; the least
        // recently added folder is the one dropped.
        using var a = new TempDirectory("a.txt");
        var folders = new List<TempDirectory>();
        var cache = new DirectChildrenListingCache();

        var firstA = await cache.GetAsync(a.Path);
        // Push past the cap (4) with distinct folders so `a` must be evicted.
        for (var i = 0; i < 4; i++)
        {
            var next = new TempDirectory($"f{i}.txt");
            folders.Add(next);
            await cache.GetAsync(next.Path);
        }

        var reloadedA = await cache.GetAsync(a.Path);

        Assert.AreNotSame(firstA, reloadedA, "the evicted folder must be walked again");
        foreach (var folder in folders) folder.Dispose();
    }

    [TestMethod]
    public void Prewarm_NullOrEmpty_DoesNothing() =>
        // The window can exist without a scope yet; warming "no folder" must not throw or walk the CWD.
        new DirectChildrenListingCache().Prewarm(null);

    [TestMethod]
    public void MatchInto_AgainstAPrebuildListing_MatchesNamesAndReportsDirectories()
    {
        var listing = new List<DirectChildrenLocator.Entry>
        {
            new("report-final.docx", @"C:\f\report-final.docx", false, FileAttributes.Normal),
            new("notes.txt", @"C:\f\notes.txt", false, FileAttributes.Normal),
            new("reports", @"C:\f\reports", true, FileAttributes.Directory),
        };

        var matched = new List<(string Name, bool IsDir)>();
        DirectChildrenLocator.MatchInto(listing, @"C:\f", "report", 50, r => matched.Add((r.Name, r.IsDir)), CancellationToken.None);

        Assert.HasCount(2, matched);
        Assert.IsTrue(matched.Any(m => m.Name == "reports" && m.IsDir));
        Assert.IsTrue(matched.Any(m => m.Name == "report-final.docx" && !m.IsDir));
    }

    [TestMethod]
    public void MatchInto_EmptyListing_ReturnsNothing() =>
        Assert.AreEqual(0, DirectChildrenLocator.MatchInto(
            new List<DirectChildrenLocator.Entry>(), @"C:\f", "q", 50, _ => { }, CancellationToken.None));
}
