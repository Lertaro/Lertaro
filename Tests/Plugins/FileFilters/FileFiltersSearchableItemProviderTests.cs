using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters.Tests;

// Two shared static delegates drive this provider: PluginSettingsService.GetSettingFunc (read once,
// synchronously, in the constructor) and DirectoryIndexerService.EnumerateDirectoryFunc (the host's
// index-backed directory listing). Both are process-wide, hence [DoNotParallelize] plus a reset in
// TestInitialize AND TestCleanup -- a previous failed run must not leak state into the first test here.
//
// The provider itself no longer touches the filesystem: listing a folder, applying the file pattern,
// skipping hidden/system entries and falling back to a live walk for an unindexed or nonexistent path
// are all the host's job (see IndexedDirectoryEnumerator/DirectoryEnumerator, tested in Tests/Core).
// What is left to test here is what this class actually still decides: which folders get asked for,
// what it asks for them, and how an entry turns into a SearchableItem.
[TestClass]
[DoNotParallelize]
public sealed class FileFiltersSearchableItemProviderTests
{
    private const string PluginId = "Lertaro.Plugins.FileFilters";

    private readonly List<(string Path, bool Recursive, string Pattern, int Limit)> _calls = new();

    [TestInitialize]
    public void ResetBefore() => Reset();

    [TestCleanup]
    public void ResetAfter() => Reset();

    private void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        DirectoryIndexerService.EnumerateDirectoryFunc = null;
        _calls.Clear();
    }

    private sealed class Entry : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string ContextDirectory => string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication => false;
    }

    private static Entry File(string fullPath) => new() { Name = Path.GetFileName(fullPath), FullPath = fullPath };

    private static Entry Dir(string fullPath) => new() { Name = Path.GetFileName(fullPath), FullPath = fullPath, IsDir = true };

    private static void ConfigureFilters(List<FileFiltersSearchableItemProvider.FilterItem> filters) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Filters" ? filters : defaultValue;

    // Stands in for the host: records what was asked, answers with whatever `contents` holds for that
    // folder (nothing at all for a folder it doesn't know -- what the host returns for a path that is
    // neither in an index nor on disk).
    private void ConfigureIndex(Dictionary<string, Entry[]>? contents = null) =>
        DirectoryIndexerService.EnumerateDirectoryFunc = (path, recursive, pattern, limit, _) =>
        {
            _calls.Add((path, recursive, pattern, limit));
            return ToAsync(contents != null && contents.TryGetValue(path, out var entries) ? entries : Array.Empty<Entry>());
        };

    private static async IAsyncEnumerable<ISearchResult> ToAsync(IEnumerable<Entry> entries)
    {
        await Task.CompletedTask;
        foreach (var entry in entries)
            yield return entry;
    }

    [TestMethod]
    public void GetSearchableItems_NoConfiguredFilters_ReturnsEmpty()
    {
        ConfigureIndex();
        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
        Assert.IsEmpty(_calls);
    }

    [TestMethod]
    public void GetSearchableItems_DisabledFilter_IsNeitherListedNorAskedFor()
    {
        ConfigureIndex(new() { [@"C:\Movies"] = new[] { File(@"C:\Movies\a.mp4") } });
        ConfigureFilters(new() { new() { Enabled = false, Folders = { @"C:\Movies" }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
        Assert.IsEmpty(_calls, "a disabled filter must not cost an enumeration");
    }

    [TestMethod]
    public void GetSearchableItems_FilesAndFoldersFromTheHost_BecomeItemsWithTheMatchingResultKind()
    {
        ConfigureIndex(new()
        {
            [@"C:\Movies"] = new[] { File(@"C:\Movies\a.mp4"), Dir(@"C:\Movies\Season 1"), File(@"C:\Movies\Season 1\b.mp4") }
        });
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"C:\Movies" }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var items = provider.GetSearchableItems().ToList();

        Assert.HasCount(3, items);
        Assert.AreEqual("File", items.Single(i => i.Title == "a.mp4").ResultKind);
        Assert.AreEqual("Directory", items.Single(i => i.Title == "Season 1").ResultKind);
        Assert.AreEqual(@"C:\Movies\Season 1", items.Single(i => i.Title == "b.mp4").Description);
    }

    // The pattern is no longer matched here, so what this provider still owes the host is passing it
    // through verbatim -- along with recursive:true, which is what makes a filter cover a whole tree.
    [TestMethod]
    public void GetSearchableItems_AsksTheHostRecursivelyWithTheConfiguredPattern()
    {
        ConfigureIndex();
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"C:\Apps" }, FilterPattern = " *.exe; *.lnk " } });

        using var provider = new FileFiltersSearchableItemProvider();
        _ = provider.GetSearchableItems().ToList();

        Assert.HasCount(1, _calls);
        Assert.AreEqual(@"C:\Apps", _calls[0].Path);
        Assert.IsTrue(_calls[0].Recursive);
        Assert.AreEqual(" *.exe; *.lnk ", _calls[0].Pattern);
        Assert.AreEqual(0, _calls[0].Limit, "a filter wants everything it configured, not a truncated view");
    }

    [TestMethod]
    public void GetSearchableItems_EveryConfiguredFolder_IsAskedForOnce()
    {
        ConfigureIndex(new()
        {
            [@"C:\Movies"] = new[] { File(@"C:\Movies\a.mp4") },
            [@"D:\More Movies"] = new[] { File(@"D:\More Movies\b.mp4") }
        });
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"C:\Movies", @"D:\More Movies" }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var items = provider.GetSearchableItems().ToList();

        Assert.HasCount(2, items);
        CollectionAssert.AreEquivalent(new[] { @"C:\Movies", @"D:\More Movies" }, _calls.ConvertAll(c => c.Path));
    }

    // Whatever the user typed in: a path that is not indexed, not on disk, or simply misspelled. The
    // host answers with nothing (it falls back to a real walk itself and finds no such directory), so
    // this provider does no existence check of its own -- one that would touch the disk for every
    // configured folder, including sleeping and disconnected ones.
    [TestMethod]
    public void GetSearchableItems_FolderTheHostKnowsNothingAbout_YieldsNothingAndStillAsks()
    {
        ConfigureIndex();
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"Z:\definitely-not-a-real-lertaro-dir" }, FilterPattern = "*" } });

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
        Assert.HasCount(1, _calls);
    }

    [TestMethod]
    public void GetSearchableItems_BlankFolderEntries_AreSkipped()
    {
        ConfigureIndex();
        ConfigureFilters(new() { new() { Enabled = true, Folders = { "", "   " }, FilterPattern = "*" } });

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
        Assert.IsEmpty(_calls);
    }

    [TestMethod]
    public void GetSearchableItems_HostThrows_IsLoggedWithoutFailingTheWholeProvider()
    {
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"C:\Movies" }, FilterPattern = "*" } });
        DirectoryIndexerService.EnumerateDirectoryFunc = (_, _, _, _, _) => throw new UnauthorizedAccessException("nope");

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
    }

    [TestMethod]
    public void GetSearchableItems_FilterName_IsPrefixedInDescription()
    {
        ConfigureIndex(new() { [@"C:\Movies"] = new[] { File(@"C:\Movies\a.mp4") } });
        ConfigureFilters(new() { new() { Enabled = true, Name = "Movies", Folders = { @"C:\Movies" }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var item = provider.GetSearchableItems().Single(i => i.Title == "a.mp4");

        Assert.StartsWith("Movies · ", item.Description);
    }

    [TestMethod]
    public void GetSearchableItems_FilterKeyword_ProducesNamespacedResultKind()
    {
        ConfigureIndex(new() { [@"C:\Movies"] = new[] { File(@"C:\Movies\a.mp4") } });
        ConfigureFilters(new() { new() { Enabled = true, Keyword = "TF", Folders = { @"C:\Movies" }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var item = provider.GetSearchableItems().Single(i => i.Title == "a.mp4");

        Assert.AreEqual("FileFilter_tf", item.ResultKind);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var provider = new FileFiltersSearchableItemProvider();

        provider.Dispose();
    }
}
