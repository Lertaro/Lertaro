using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QueryTokens;

[TestClass]
public sealed class SortFilterQueryTokenProviderTests
{
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
    [DataRow("<s")]
    [DataRow(">s")]
    [DataRow("<c")]
    [DataRow(">c")]
    [DataRow("<m")]
    [DataRow("<a")]
    [DataRow("<f")]
    [DataRow(">f")]
    [DataRow("<s>20m")]
    [DataRow("<c>2008.8.3")]
    public void CanHandle_SortTokens_ReturnsTrue(string token) => Assert.IsTrue(Provider.CanHandle(token));

    [TestMethod]
    [DataRow("s")]
    [DataRow(":s")]
    [DataRow("<")]
    [DataRow("<x")]
    [DataRow("<s>")]
    public void CanHandle_NoLongerClaimedTokens_ReturnsFalse(string token) => Assert.IsFalse(Provider.CanHandle(token));

    [TestMethod]
    public async Task ApplyAsync_AscendingSort_OrdersSmallestFirst()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "big", Metadata = new FileMetadata(300, default, default, default) },
            new FakeResult { Name = "small", Metadata = new FileMetadata(100, default, default, default) },
        };

        var sorted = await Provider.ApplyAsync("<s", results);

        CollectionAssert.AreEqual(new[] { "small", "big" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_DescendingSort_OrdersLargestFirst()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "small", Metadata = new FileMetadata(100, default, default, default) },
            new FakeResult { Name = "big", Metadata = new FileMetadata(300, default, default, default) },
        };

        var sorted = await Provider.ApplyAsync(">s", results);

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

        var sorted = await Provider.ApplyAsync("<m", results);

        CollectionAssert.AreEqual(new[] { "older", "newer" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_SizeThresholdAscending_KeepsFilesLargerThanTheThreshold()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "small", Metadata = new FileMetadata(10 * 1024, default, default, default) },
            new FakeResult { Name = "big", Metadata = new FileMetadata(30 * 1024 * 1024, default, default, default) },
        };

        var filtered = await Provider.ApplyAsync("<s>20m", results);

        CollectionAssert.AreEqual(new[] { "big" }, filtered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_UpperBoundSizeThreshold_KeepsFilesSmallerThanTheThreshold()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "small", Metadata = new FileMetadata(10 * 1024, default, default, default) },
            new FakeResult { Name = "big", Metadata = new FileMetadata(30 * 1024 * 1024, default, default, default) },
        };

        var filtered = await Provider.ApplyAsync("<s<20m", results);

        CollectionAssert.AreEqual(new[] { "small" }, filtered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_UpperBoundDateThreshold_KeepsItemsBeforeTheDate()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "old", Metadata = new FileMetadata(0, new DateTime(2001, 1, 1), default, default) },
            new FakeResult { Name = "new", Metadata = new FileMetadata(0, new DateTime(2010, 1, 1), default, default) },
        };

        var filtered = await Provider.ApplyAsync("<c<2008.8.3", results);

        CollectionAssert.AreEqual(new[] { "old" }, filtered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_LowerBoundDateThreshold_KeepsItemsAfterTheDate()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "old", Metadata = new FileMetadata(0, new DateTime(2001, 1, 1), default, default) },
            new FakeResult { Name = "new", Metadata = new FileMetadata(0, new DateTime(2010, 1, 1), default, default) },
        };

        var filtered = await Provider.ApplyAsync("<c>2008.8.3", results);

        CollectionAssert.AreEqual(new[] { "new" }, filtered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task ApplyAsync_DirectoryFilter_KeepsOnlyDirectories()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "folder1", IsDir = true },
            new FakeResult { Name = "file1", IsDir = false },
        };

        var filtered = await Provider.ApplyAsync("<f>f", results);

        CollectionAssert.AreEqual(new[] { "folder1" }, filtered.Select(r => r.Name).ToList());
    }
    [TestMethod]
    public async Task ApplyAsync_UnparseableThreshold_NarrowsNothing()
    {
        var results = new ISearchResult[]
        {
            new FakeResult { Name = "a", Metadata = new FileMetadata(1, default, default, default) },
            new FakeResult { Name = "b", Metadata = new FileMetadata(2, default, default, default) },
        };

        var filtered = await Provider.ApplyAsync("<s>notasize", results);

        Assert.HasCount(2, filtered);
    }

    [TestMethod]
    [DataRow("2003-08-03")]
    [DataRow("2003-8-3")]
    [DataRow("03-08-03")]
    [DataRow("03-8-3")]
    [DataRow("2003.08.03")]
    [DataRow("2003.8.3")]
    [DataRow("03.08.03")]
    [DataRow("03.8.3")]
    [DataRow("2003/08/03")]
    [DataRow("2003/8/3")]
    [DataRow("03/08/03")]
    [DataRow("03/8/3")]
    [DataRow("20030803")]
    public void TryParseDate_AllThirteenFormats_ParseToTheSameDay(string text)
    {
        Assert.IsTrue(SortFilterQueryTokenProvider.TryParseDate(text, out var value));
        Assert.AreEqual(new DateTime(2003, 8, 3), value);
    }

    [TestMethod]
    [DataRow("2003-08.03", DisplayName = "dash mixed with dot")]
    [DataRow("2003-08/03", DisplayName = "dash mixed with slash")]
    [DataRow("2003.8/3", DisplayName = "dot mixed with slash")]
    public void TryParseDate_MixedSeparators_AreRejected(string text) => Assert.IsFalse(SortFilterQueryTokenProvider.TryParseDate(text, out _));

    [TestMethod]
    public void TryParseDate_AmbiguousNumericForm_IsNotAFallDate() =>
        // 03/08/2003 would be March 8 in one culture and August 3 in another -- rejected outright, so
        // the same query can never mean two different things on two machines.
        Assert.IsFalse(SortFilterQueryTokenProvider.TryParseDate("03/08/2003", out _));

    [TestMethod]
    [DataRow("2003-13-03", DisplayName = "month 13")]
    [DataRow("2003-12-32", DisplayName = "day 32")]
    [DataRow("2003-00-03", DisplayName = "month 0")]
    [DataRow("2003-01-00", DisplayName = "day 0")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "whitespace only")]
    public void TryParseDate_ImpossibleOrEmptyValues_AreRejected(string text) => Assert.IsFalse(SortFilterQueryTokenProvider.TryParseDate(text, out _));

    [TestMethod]
    public void TryParseDate_TwoDigitYearLeadingThirteen_ReadsAsAYearNotAMonth()
    {
        // "13-01-03" is year 2013, month 1, day 3 -- the leading field is always the year, so a value
        // above 12 there is a year, not an impossible month. Pinned so the field order cannot drift.
        Assert.IsTrue(SortFilterQueryTokenProvider.TryParseDate("13-01-03", out var value));
        Assert.AreEqual(new DateTime(2013, 1, 3), value);
    }

    [TestMethod]
    [DataRow("20m", 20L * 1024 * 1024)]
    [DataRow("1.5k", 1536)]
    [DataRow("2g", 2L * 1024 * 1024 * 1024)]
    [DataRow("512", 512)]
    public void TryParseSize_AcceptedForms(string text, long expected)
    {
        Assert.IsTrue(SortFilterQueryTokenProvider.TryParseSize(text, out var bytes));
        Assert.AreEqual(expected, bytes);
    }

    [TestMethod]
    [DataRow("1e300", DisplayName = "far outside long's range")]
    [DataRow("NaN", DisplayName = "not a number")]
    [DataRow("Infinity", DisplayName = "infinite")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("abc", DisplayName = "not a number at all")]
    public void TryParseSize_UnrepresentableValues_AreRejectedRatherThanSilentlyBounded(string text)
    {
        // NumberStyles.Float parses "NaN", "Infinity" and "1e300", and converting such a double to long is
        // UNSPECIFIED in an unchecked context -- so an absurd threshold used to become a nonsense bound
        // (in practice long.MinValue, which makes "greater than" keep everything) instead of being rejected
        // as unparseable, which the caller already handles by narrowing nothing.
        Assert.IsFalse(SortFilterQueryTokenProvider.TryParseSize(text, out _));
    }

    [TestMethod]
    public async Task ApplyAsync_FolderThreshold_AcceptsTheDocumentedSpellings()
    {
        // The search-syntax documentation offers "f", "folder" and "dir" for the folder/file key, so all
        // three have to mean directories -- only the bare letter used to work, and "dir" silently meant
        // "files only".
        foreach (var threshold in new[] { "f", "folder", "dir", "F", "DIR" })
        {
            var results = new List<ISearchResult>
            {
                new FakeResult { Name = "folder1", IsDir = true },
                new FakeResult { Name = "file1", IsDir = false },
            };

            var filtered = await Provider.ApplyAsync($"<f>{threshold}", results);

            CollectionAssert.AreEqual(new[] { "folder1" }, filtered.Select(r => r.Name).ToList(), threshold);
        }
    }
}
