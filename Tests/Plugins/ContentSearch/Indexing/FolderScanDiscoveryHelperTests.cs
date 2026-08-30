using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// The host enumeration hook (DirectoryIndexerService.EnumerateDirectoryFunc) is process-wide
// static state, so these tests must not run concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class FolderScanDiscoveryHelperTests
{
    [TestCleanup]
    public void ResetHostEnumeration() => DirectoryIndexerService.EnumerateDirectoryFunc = null;

    [TestMethod]
    public async Task DiscoverFilesAsync_EmptyFolders_ReturnsEmpty()
    {
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string>()
        };
        var existingMeta = new Dictionary<string, (long, long)>();
        var enqueued = new List<string>();

        var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
            config,
            existingMeta,
            enqueued.Add,
            CancellationToken.None);

        Assert.IsEmpty(discovered);
        Assert.IsEmpty(enqueued);
    }

    [TestMethod]
    public async Task DiscoverFilesAsync_StaleHostSnapshot_MergesFilesystemWalk()
    {
        // Regression: the service index can answer from a stale snapshot that misses recently
        // created files; a non-empty answer must not stop the filesystem walk from finding them.
        var dir = CreateTempDirectory();

        try
        {
            foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
                await File.WriteAllTextAsync(Path.Combine(dir, name), "content of " + name);

            var hostFiles = new[] { "a.txt" }; // stale snapshot: only one of three files
            DirectoryIndexerService.EnumerateDirectoryFunc = (folder, recursive, pattern, limit, token) =>
                EnumerateFake(Path.Combine(dir, hostFiles[0]), (long)new FileInfo(Path.Combine(dir, hostFiles[0])).Length, DateTime.Now);

            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { dir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" }
            };
            var enqueued = new List<string>();

            var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                config,
                new Dictionary<string, (long LastModified, long FileSize)>(),
                enqueued.Add,
                CancellationToken.None);

            var expected = new[] { "a.txt", "b.txt", "c.txt" }.Select(n => Path.Combine(dir, n)).ToList();
            CollectionAssert.AreEquivalent(expected, discovered.ToList());
            Assert.HasCount(3, enqueued);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task DiscoverFilesAsync_HostEnumerationThrows_StillDiscoversFromDisk()
    {
        var dir = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "only.txt"), "content");

            DirectoryIndexerService.EnumerateDirectoryFunc = (_, _, _, _, _) => throw new IOException("service unreachable");

            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { dir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" }
            };
            var enqueued = new List<string>();

            var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                config,
                new Dictionary<string, (long LastModified, long FileSize)>(),
                enqueued.Add,
                CancellationToken.None);

            CollectionAssert.AreEquivalent(new[] { Path.Combine(dir, "only.txt") }, discovered.ToList());
            Assert.HasCount(1, enqueued);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task DiscoverFilesAsync_UnchangedFilesInHostIndex_AreDiscoveredButNotEnqueued()
    {
        var dir = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(dir, "known.txt");
            await File.WriteAllTextAsync(filePath, "content");

            DirectoryIndexerService.EnumerateDirectoryFunc = (folder, recursive, pattern, limit, token) =>
                EnumerateFake(filePath, new FileInfo(filePath).Length, FileInfoMetaModified(filePath));

            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { dir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" }
            };
            var info = new FileInfo(filePath);
            var existingMeta = new Dictionary<string, (long LastModified, long FileSize)>
            {
                [filePath] = (new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(), info.Length)
            };
            var enqueued = new List<string>();

            var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                config,
                existingMeta,
                enqueued.Add,
                CancellationToken.None);

            CollectionAssert.AreEquivalent(new[] { filePath }, discovered.ToList());
            Assert.IsEmpty(enqueued);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task DiscoverFilesAsync_ExcludedFolder_SubtreeSkippedByFilesystemWalk()
    {
        var dir = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "keep.txt"), "indexed content");
            var backupDir = Path.Combine(dir, "Backup");
            Directory.CreateDirectory(backupDir);
            await File.WriteAllTextAsync(Path.Combine(backupDir, "old.txt"), "must not be indexed");

            // Host enumeration unavailable: everything below comes from the raw walk.
            DirectoryIndexerService.EnumerateDirectoryFunc = (_, _, _, _, _) => throw new IOException("service unreachable");

            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { dir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
                ExcludedPatterns = ContentIndexConfig.ParseExcludedPatterns(@"\\Backup\\")
            };
            var enqueued = new List<string>();

            var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                config,
                new Dictionary<string, (long LastModified, long FileSize)>(),
                enqueued.Add,
                CancellationToken.None);

            CollectionAssert.AreEquivalent(new[] { Path.Combine(dir, "keep.txt") }, discovered.ToList());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task DiscoverFilesAsync_HostReportsExcludedFile_NotDiscovered()
    {
        var dir = CreateTempDirectory();

        try
        {
            var secretPath = Path.Combine(dir, "secret.txt");
            await File.WriteAllTextAsync(secretPath, "sensitive");

            DirectoryIndexerService.EnumerateDirectoryFunc = (folder, recursive, pattern, limit, token) =>
                EnumerateFake(secretPath, new FileInfo(secretPath).Length, FileInfoMetaModified(secretPath));

            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { dir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
                ExcludedPatterns = ContentIndexConfig.ParseExcludedPatterns("secret")
            };
            var enqueued = new List<string>();

            var discovered = await FolderScanDiscoveryHelper.DiscoverFilesAsync(
                config,
                new Dictionary<string, (long LastModified, long FileSize)>(),
                enqueued.Add,
                CancellationToken.None);

            Assert.IsEmpty(discovered);
            Assert.IsEmpty(enqueued);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"scan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DateTime FileInfoMetaModified(string filePath) =>
        // FileMetadata.Modified is local time; mirror the metadata the host would report.
        new FileInfo(filePath).LastWriteTime;

    private static async IAsyncEnumerable<ISearchResult> EnumerateFake(
        string fullPath, long size, DateTime modified)
    {
        await Task.CompletedTask;
        yield return new FakeSearchResult(fullPath, size, modified);
    }

    private sealed class FakeSearchResult(string fullPath, long size, DateTime modified) : ISearchResult
    {
        public string Name => Path.GetFileName(fullPath);
        public string FullPath => fullPath;
        public string ContextDirectory => Path.GetDirectoryName(fullPath) ?? string.Empty;
        public bool IsDir => false;
        public bool IsApplication => false;
        public FileMetadata Metadata => new(size, modified, modified, modified);
    }
}
