using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Helpers;

// The cache exists so opening an action menu asks the filesystem about each selected path once instead
// of once per action that gates on it -- eight of them do, over the same selection, on the UI thread.
// Its whole job is to change the cost and not one verdict, so what follows is mostly about the second
// half of that.
[TestClass]
[DoNotParallelize]
public sealed class PathExistenceCacheTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string ContextDirectory { get; init; } = string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lertaro-existence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string NewFile(string name = "a.txt")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [TestMethod]
    public void Exists_OutsideAScope_AnswersFromTheFilesystem()
    {
        var file = NewFile();

        Assert.IsTrue(PathExistenceCache.Exists(file));
        Assert.IsFalse(PathExistenceCache.Exists(file + ".gone"));
    }

    [TestMethod]
    public void Exists_TypedResult_OnlyChecksItsDeclaredKind()
    {
        var file = NewFile();

        Assert.IsFalse(PathExistenceCache.ExistsResult(new FakeResult { FullPath = _dir, IsDir = false }));
        Assert.IsFalse(PathExistenceCache.ExistsResult(new FakeResult { FullPath = file, IsDir = true }));
        Assert.IsTrue(PathExistenceCache.ExistsResult(new FakeResult { FullPath = _dir, IsDir = true }));
        Assert.IsTrue(PathExistenceCache.ExistsResult(new FakeResult { FullPath = file, IsDir = false }));
    }

    [TestMethod]
    public void Exists_TypedAndUntypedAnswers_DoNotShareTheWrongCachedVerdict()
    {
        using var scope = PathExistenceCache.BeginScope();

        Assert.IsTrue(PathExistenceCache.Exists(_dir));
        Assert.IsFalse(PathExistenceCache.ExistsResult(new FakeResult { FullPath = _dir, IsDir = false }));
    }

    [TestMethod]
    public void Exists_OutsideAScope_DoesNotRemember()
    {
        // A caller that hasn't opted in has to behave exactly as a direct File.Exists call did, which
        // means noticing a deletion immediately.
        var file = NewFile();
        Assert.IsTrue(PathExistenceCache.Exists(file));

        File.Delete(file);

        Assert.IsFalse(PathExistenceCache.Exists(file));
    }

    [TestMethod]
    public void Exists_InsideAScope_GivesTheSameAnswersAsWithout()
    {
        var file = NewFile();
        using var scope = PathExistenceCache.BeginScope();

        Assert.IsTrue(PathExistenceCache.Exists(file));
        Assert.IsTrue(PathExistenceCache.Exists(_dir));
        Assert.IsFalse(PathExistenceCache.Exists(file + ".gone"));
    }

    [TestMethod]
    public void Exists_InsideAScope_RemembersForTheRestOfIt()
    {
        // The staleness this deliberately accepts: one menu build sees one consistent view. Without it
        // the eight gates could disagree with each other, enabling one action and disabling the next
        // over a file that vanished between two of their passes.
        var file = NewFile();
        using (PathExistenceCache.BeginScope())
        {
            Assert.IsTrue(PathExistenceCache.Exists(file));
            File.Delete(file);
            Assert.IsTrue(PathExistenceCache.Exists(file), "within one build the verdict must not change");
        }

        Assert.IsFalse(PathExistenceCache.Exists(file), "and must not survive the build");
    }

    [TestMethod]
    public void Exists_ANewScope_DoesNotInheritTheOldOne()
    {
        var file = NewFile();
        using (PathExistenceCache.BeginScope())
            Assert.IsTrue(PathExistenceCache.Exists(file));

        File.Delete(file);

        using (PathExistenceCache.BeginScope())
            Assert.IsFalse(PathExistenceCache.Exists(file));
    }

    [TestMethod]
    public void Exists_PathsDifferingOnlyInCase_ShareOneAnswer()
    {
        var file = NewFile("Mixed.TXT");
        using var scope = PathExistenceCache.BeginScope();

        Assert.IsTrue(PathExistenceCache.Exists(file));
        Assert.IsTrue(PathExistenceCache.Exists(file.ToLowerInvariant()));
        Assert.IsTrue(PathExistenceCache.Exists(file.ToUpperInvariant()));
    }

    [TestMethod]
    public void Exists_BlankPath_IsFalseEitherWay()
    {
        Assert.IsFalse(PathExistenceCache.Exists(null));
        Assert.IsFalse(PathExistenceCache.Exists(""));
        Assert.IsFalse(PathExistenceCache.Exists("   "));

        using var scope = PathExistenceCache.BeginScope();
        Assert.IsFalse(PathExistenceCache.Exists(null));
        Assert.IsFalse(PathExistenceCache.Exists(""));
    }

    [TestMethod]
    public void Exists_AMalformedPath_IsFalseRatherThanThrowing()
    {
        using var scope = PathExistenceCache.BeginScope();

        Assert.IsFalse(PathExistenceCache.Exists("|not<a>path?"));
        Assert.IsFalse(PathExistenceCache.Exists(new string('x', 4000)));
    }

    [TestMethod]
    public void Prime_StopsAtTheFirstMissingPath()
    {
        // The property that keeps priming from ever costing more than not priming. Every caller reads
        // the result through All(), which stops at the first false as well, so priming past that point
        // would probe paths nobody was going to ask about -- on a selection of hundreds of thousands
        // whose first entry is stale, that is the difference between one probe and all of them.
        var present = NewFile("present.txt");
        var missing = Path.Combine(_dir, "missing.txt");
        var laterFile = NewFile("later.txt");

        using var scope = PathExistenceCache.BeginScope();
        PathExistenceCache.Prime(new[] { present, missing, laterFile });

        // Deleting present.txt now proves it was cached; laterFile not being cached is proved by it
        // noticing ITS deletion.
        File.Delete(present);
        File.Delete(laterFile);

        Assert.IsTrue(PathExistenceCache.Exists(present), "should have been primed");
        Assert.IsFalse(PathExistenceCache.Exists(laterFile), "should not have been primed past the gap");
    }

    [TestMethod]
    public void Prime_AllPresent_CachesEveryOne()
    {
        var files = Enumerable.Range(0, 5).Select(i => NewFile($"f{i}.txt")).ToArray();

        using var scope = PathExistenceCache.BeginScope();
        PathExistenceCache.Prime(files);
        foreach (var f in files) File.Delete(f);

        foreach (var f in files)
            Assert.IsTrue(PathExistenceCache.Exists(f), f);
    }

    [TestMethod]
    public void Prime_OutsideAScope_DoesNothingAndDoesNotThrow()
    {
        var file = NewFile();

        PathExistenceCache.Prime(new[] { file });

        File.Delete(file);
        Assert.IsFalse(PathExistenceCache.Exists(file));
    }

    [TestMethod]
    public void Dispose_OfASupersededScope_LeavesTheNewerOneWorking()
    {
        // A menu build can be replaced while it is still running (ActionFlyout.Show calls Close first and
        // guards on a generation counter). The older one finishing must not pull the cache out from under
        // the newer one.
        var file = NewFile();
        var older = PathExistenceCache.BeginScope();
        var newer = PathExistenceCache.BeginScope();

        Assert.IsTrue(PathExistenceCache.Exists(file));
        older.Dispose();
        File.Delete(file);

        Assert.IsTrue(PathExistenceCache.Exists(file), "the newer scope's cache should still be in effect");

        newer.Dispose();
        Assert.IsFalse(PathExistenceCache.Exists(file));
    }

    [TestMethod]
    public void Exists_FromSeveralThreads_AgreesWithTheSingleThreadedAnswer()
    {
        // Primed on a background thread and read on the UI thread, so the cache is genuinely shared
        // across threads rather than ambient to one.
        var present = Enumerable.Range(0, 40).Select(i => NewFile($"p{i}.txt")).ToArray();
        var absent = present.Select(p => p + ".gone").ToArray();
        var all = present.Concat(absent).ToArray();

        using var scope = PathExistenceCache.BeginScope();
        var answers = new bool[all.Length];
        Parallel.For(0, all.Length, i => answers[i] = PathExistenceCache.Exists(all[i]));

        for (var i = 0; i < present.Length; i++)
            Assert.IsTrue(answers[i], present[i]);
        for (var i = present.Length; i < all.Length; i++)
            Assert.IsFalse(answers[i], all[i]);
    }
}
