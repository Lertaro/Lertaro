using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QueryTokens;

[TestClass]
public sealed class SortFilterQueryTokenProviderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    [DoNotParallelize]
    [DataRow(4000)]
    [DataRow(100_000)]
    public async Task SortMetadata_RecordsSynchronousWorkAndAllocation(int count)
    {
        var results = Enumerable.Range(0, count).Select(i => (ISearchResult)new FakeResult
        {
            Name = $"report{i:D6}", FullPath = $@"Z:\report{i:D6}.txt",
            Metadata = new((i * 7919L) % count, default, default, default)
        }).ToArray();
        for (var i = 0; i < 3; i++) await Provider.ApplyAsync("S-", results);
        var elapsed = new List<double>();
        var bytes = GC.GetTotalAllocatedBytes(true);
        var completedInline = 0;
        for (var i = 0; i < 20; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var work = Provider.ApplyAsync("S-", results);
            if (work.IsCompletedSuccessfully) completedInline++;
            var sorted = await work;
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            Assert.AreEqual(count - 1L, sorted[0].Metadata.Size);
        }
        TestContext.WriteLine("CHECKLIST_PERF " + System.Text.Json.JsonSerializer.Serialize(new
        {
            scenario = "token-sort", rows = count, iterations = 20, elapsedMs = elapsed,
            allocatedBytes = GC.GetTotalAllocatedBytes(true) - bytes, completedInline
        }));
    }

    [TestMethod]
    [DataRow("S")]
    [DataRow("S-")]
    [DataRow("C")]
    [DataRow("C-")]
    [DataRow("M")]
    [DataRow("M-")]
    [DataRow("A")]
    [DataRow("A-")]
    public async Task MetadataSort_PreservesStableTiesAndExtremeValues(string token)
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "first", Metadata = new(0, default, default, default) },
            new FakeResult { Name = "large", Metadata = new(long.MaxValue, DateTime.MaxValue, DateTime.MaxValue, DateTime.MaxValue) },
            new FakeResult { Name = "second", Metadata = new(0, default, default, default) },
        };
        var result = await Provider.ApplyAsync(token, results);
        var expected = token.EndsWith('-') ? new[] { "large", "first", "second" } : new[] { "first", "second", "large" };
        CollectionAssert.AreEqual(expected, result.Select(r => r.Name).ToArray());
    }

    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
        public FileMetadata Metadata { get; init; }
    }

    private static readonly SortFilterQueryTokenProvider Provider = new();

    [TestMethod]
    [DataRow("s")]
    [DataRow("S")]
    [DataRow("-s")]
    [DataRow("s-")]
    [DataRow("c")]
    [DataRow("m")]
    [DataRow("a")]
    [DataRow("f")]
    [DataRow("F")]
    [DataRow("-f")]
    public void CanHandle_SortTokens_ReturnsTrue(string token) => Assert.IsTrue(Provider.CanHandle(token));

    [TestMethod]
    public void CanHandle_ExtensionFilterToken_ReturnsTrue() => Assert.IsTrue(Provider.CanHandle(".txt.doc"));

    [TestMethod]
    public void CanHandle_UnrelatedToken_ReturnsFalse() => Assert.IsFalse(Provider.CanHandle("xyz"));

    [TestMethod]
    public void CanHandle_JustDot_ReturnsFalse() => Assert.IsFalse(Provider.CanHandle("."));

    [TestMethod]
    public async Task ApplyAsync_ExtensionFilter_KeepsOnlyMatchingExtensions()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { FullPath = @"C:\a.txt" },
            new FakeResult { FullPath = @"C:\b.doc" },
            new FakeResult { FullPath = @"C:\c.png" },
        };

        var filtered = await Provider.ApplyAsync(".txt.doc", results);

        CollectionAssert.AreEquivalent(new[] { @"C:\a.txt", @"C:\b.doc" }, filtered.Select(r => r.FullPath).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_ExtensionFilter_ExcludesDirectoriesEvenIfNameMatches()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt", IsDir = true } };

        var filtered = await Provider.ApplyAsync(".txt", results);

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_ExtensionFilterIsCaseInsensitive()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\a.TXT" } };

        var filtered = await Provider.ApplyAsync(".txt", results);

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_SortBySizeAscending_OrdersSmallestFirst()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "big", Metadata = new FileMetadata(300, default, default, default) },
            new FakeResult { Name = "small", Metadata = new FileMetadata(100, default, default, default) },
        };

        var sorted = await Provider.ApplyAsync("s", results);

        CollectionAssert.AreEqual(new[] { "small", "big" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_SortBySizeDescendingWithLeadingDash_OrdersLargestFirst()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "small", Metadata = new FileMetadata(100, default, default, default) },
            new FakeResult { Name = "big", Metadata = new FileMetadata(300, default, default, default) },
        };

        var sorted = await Provider.ApplyAsync("-s", results);

        CollectionAssert.AreEqual(new[] { "big", "small" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_SortByModifiedDate_OrdersOldestFirst()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "newer", Metadata = new FileMetadata(0, default, new DateTime(2024, 6, 1), default) },
            new FakeResult { Name = "older", Metadata = new FileMetadata(0, default, new DateTime(2024, 1, 1), default) },
        };

        var sorted = await Provider.ApplyAsync("m", results);

        CollectionAssert.AreEqual(new[] { "older", "newer" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_TrailingDashAlsoMeansDescending()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "small", Metadata = new FileMetadata(100, default, default, default) },
            new FakeResult { Name = "big", Metadata = new FileMetadata(300, default, default, default) },
        };

        var sorted = await Provider.ApplyAsync("s-", results);

        CollectionAssert.AreEqual(new[] { "big", "small" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_FilterDirectories_KeepsOnlyDirectories()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "folder1", IsDir = true },
            new FakeResult { Name = "file1", IsDir = false },
            new FakeResult { Name = "folder2", IsDir = true },
        };

        var filtered = await Provider.ApplyAsync("f", results);

        CollectionAssert.AreEqual(new[] { "folder1", "folder2" }, filtered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_FilterNonDirectories_KeepsOnlyFiles()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "folder1", IsDir = true },
            new FakeResult { Name = "file1", IsDir = false },
        };

        var filtered = await Provider.ApplyAsync("-f", results);

        CollectionAssert.AreEqual(new[] { "file1" }, filtered.Select(r => r.Name).ToList());
    }
}
