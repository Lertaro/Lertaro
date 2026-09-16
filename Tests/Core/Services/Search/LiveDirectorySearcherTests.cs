using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class LiveDirectorySearcherTests
{
    [TestMethod]
    public void ScanDirectory_EmptyPath_ReturnsEmpty()
    {
        var results = LiveDirectorySearcher.ScanDirectory("", 100, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void ScanDirectory_NonExistentPath_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "lertaro-tests-nonexistent-dir-marker");

        var results = LiveDirectorySearcher.ScanDirectory(path, 100, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void ScanDirectory_FileAndSubdirectory_ReturnsBothWithCorrectMetadata()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None);

        Assert.HasCount(2, results);
        var file = results.Single(r => r.Name == "a.txt");
        Assert.IsFalse(file.IsDir);
        var subdir = results.Single(r => r.Name == "sub");
        Assert.IsTrue(subdir.IsDir);
    }

    [TestMethod]
    public void ScanDirectory_HiddenFile_PopulatesAttributesWithHiddenFlag()
    {
        // Regression test: attrs was computed to decide IsDir but never assigned onto the SearchResult
        // itself, so FileSystemItemFilter.IsHiddenOrSystem always saw the zero default for every
        // live-scanned entry regardless of its real on-disk attributes.
        using var dir = new TempDirectory();
        var hiddenPath = Path.Combine(dir.Path, "hidden.txt");
        File.WriteAllText(hiddenPath, "x");
        File.SetAttributes(hiddenPath, FileAttributes.Hidden);

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None);

        var hidden = results.Single(r => r.Name == "hidden.txt");
        Assert.IsTrue(hidden.Attributes.HasFlag(FileAttributes.Hidden));
    }

    [TestMethod]
    public void ScanDirectory_RecursesIntoSubdirectories()
    {
        using var dir = new TempDirectory();
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "x");

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None);

        Assert.IsTrue(results.Any(r => r.Name == "nested.txt"));
    }

    [TestMethod]
    public void ScanDirectory_MaxProcessedLimitsResultCount()
    {
        using var dir = new TempDirectory();
        for (var i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(dir.Path, $"file{i}.txt"), "x");

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 3, CancellationToken.None);

        Assert.IsLessThanOrEqualTo(3, results.Count);
    }

    [TestMethod]
    public void ScanDirectory_WithLiveQuery_StreamsOnlyMatchingEntriesAsTheyAreDiscovered()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "readme.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "other.log"), "x");
        var streamed = new List<SearchResult>();

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None,
            liveQuery: "read", onLiveMatch: streamed.Add);

        Assert.HasCount(2, results);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.txt", streamed[0].Name);
    }

    [TestMethod]
    public void ScanDirectory_LiveQueryEmpty_StreamsEveryEntry()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "x");
        var streamed = new List<SearchResult>();

        LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None,
            liveQuery: "", onLiveMatch: streamed.Add);

        Assert.HasCount(2, streamed);
    }

    [TestMethod]
    public void ScanDirectory_LiveQueryWithOnlyDirectChildren_ExcludesGrandchildrenFromLiveStream()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "child.txt"), "x");
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "grandchild.txt"), "x");
        var streamed = new List<SearchResult>();

        LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None,
            liveQuery: "", onLiveMatch: streamed.Add, onlyDirectChildren: true, parentPath: dir.Path);

        // "sub" itself is a direct child (streamed), but "grandchild.txt" (one level deeper) is not.
        Assert.HasCount(2, streamed);
        Assert.IsFalse(streamed.Any(r => r.Name == "grandchild.txt"));
    }

    [TestMethod]
    public void ScanDirectory_CancelledLiveMatchToken_StopsDeliveryButNotTheScan()
    {
        // The scan is shared between keystrokes and outlives any one of them, so its own token cannot gate
        // delivery to whoever is listening: cancelling it means "stop walking". Delivery is gated by the
        // listener's own token instead -- without that, the callback captured at scan start keeps firing
        // into a request that has already been superseded and moved on.
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "readme.txt"), "x");
        var streamed = new List<SearchResult>();
        using var liveCts = new CancellationTokenSource();
        liveCts.Cancel();

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None,
            liveQuery: "read", onLiveMatch: streamed.Add, liveMatchToken: liveCts.Token);

        Assert.IsEmpty(streamed);
        // The walk still completed, so the finished list is there for the next keystroke to reuse.
        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void ScanDirectory_LiveMatchTokenNotCancelled_StillStreams()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "readme.txt"), "x");
        var streamed = new List<SearchResult>();

        LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None,
            liveQuery: "read", onLiveMatch: streamed.Add, liveMatchToken: CancellationToken.None);

        Assert.HasCount(1, streamed);
    }

    [TestMethod]
    public void MatchAndStream_EmptyEntries_ReturnsFalse()
    {
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(new List<SearchResult>(), "query", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_NoQuery_StreamsEveryEntry()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "alpha.txt", Path = @"C:\alpha.txt" },
            new() { Name = "beta.txt", Path = @"C:\beta.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(2, streamed);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesSubsequence_StreamsOnlyMatchingEntries()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "readme.txt", Path = @"C:\readme.txt" },
            new() { Name = "other.log", Path = @"C:\other.log" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "read", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.txt", streamed[0].Name);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesNothing_ReturnsFalse()
    {
        var entries = new List<SearchResult> { new() { Name = "readme.txt", Path = @"C:\readme.txt" } };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "zzz", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_OnlyDirectChildren_FiltersOutGrandchildren()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "child.txt", Path = @"C:\root\child.txt" },
            new() { Name = "grandchild.txt", Path = @"C:\root\sub\grandchild.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None,
            onlyDirectChildren: true, parentPath: @"C:\root");

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("child.txt", streamed[0].Name);
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
