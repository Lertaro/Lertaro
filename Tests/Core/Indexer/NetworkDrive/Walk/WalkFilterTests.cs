using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class WalkFilterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "lertaro-walkfilter-root");

    private static WalkOptions Options(
        IReadOnlyList<string>? excludedPaths = null,
        int maxDepth = 0,
        bool useIgnoreFiles = false) => new(
            excludedPaths ?? [],
            IgnoredPathGlobs: [],
            IgnoredPathRegexes: [],
            maxDepth,
            WorkerCount: 1,
            useIgnoreFiles);

    [TestMethod]
    public void ShouldIndex_PathUnderExcludedRoot_ReturnsFalse()
    {
        var filter = WalkFilter.Create(Root, Options(excludedPaths: [Path.Combine(Root, "excluded")]));

        var result = filter.ShouldIndex(
            Path.Combine(Root, "excluded", "file.txt"), "file.txt", isDirectory: false,
            FileAttributes.Normal, NetworkIgnoreRuleSet.Empty);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ShouldIndex_PathNotExcluded_ReturnsTrue()
    {
        var filter = WalkFilter.Create(Root, Options(excludedPaths: [Path.Combine(Root, "excluded")]));

        var result = filter.ShouldIndex(
            Path.Combine(Root, "ok", "file.txt"), "file.txt", isDirectory: false,
            FileAttributes.Normal, NetworkIgnoreRuleSet.Empty);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldIndex_MatchedByIgnoreRuleSet_ReturnsFalse()
    {
        var filter = WalkFilter.Create(Root, Options());
        var basePath = PathHelpers.NormalizePath(Root, true);
        var rule = NetworkIgnoreRule.Parse(basePath, "*.tmp")!.Value;
        var ignoreRules = NetworkIgnoreRuleSet.Empty.Add(rule);

        var ignored = filter.ShouldIndex(Path.Combine(Root, "cache.tmp"), "cache.tmp", false, FileAttributes.Normal, ignoreRules);
        var kept = filter.ShouldIndex(Path.Combine(Root, "cache.txt"), "cache.txt", false, FileAttributes.Normal, ignoreRules);

        Assert.IsFalse(ignored);
        Assert.IsTrue(kept);
    }

    [TestMethod]
    public void ShouldDescend_MaxDepthZero_NeverBlocksOnDepth()
    {
        var filter = WalkFilter.Create(Root, Options(maxDepth: 0));

        var result = filter.ShouldDescend(Path.Combine(Root, "a", "b", "c"), FileAttributes.Directory, depth: 50, NetworkIgnoreRuleSet.Empty);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldDescend_DepthWithinMaxDepth_ReturnsTrue()
    {
        var filter = WalkFilter.Create(Root, Options(maxDepth: 3));

        var result = filter.ShouldDescend(Path.Combine(Root, "a"), FileAttributes.Directory, depth: 2, NetworkIgnoreRuleSet.Empty);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldDescend_DepthBeyondMaxDepth_ReturnsFalse()
    {
        var filter = WalkFilter.Create(Root, Options(maxDepth: 3));

        var result = filter.ShouldDescend(Path.Combine(Root, "a"), FileAttributes.Directory, depth: 4, NetworkIgnoreRuleSet.Empty);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ShouldDescend_DirectoryUnderExcludedRoot_ReturnsFalse()
    {
        var filter = WalkFilter.Create(Root, Options(excludedPaths: [Path.Combine(Root, "excluded")]));

        var result = filter.ShouldDescend(Path.Combine(Root, "excluded", "sub"), FileAttributes.Directory, depth: 1, NetworkIgnoreRuleSet.Empty);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void LoadIgnoreRules_UseIgnoreFilesDisabled_ReturnsInheritedUnchanged()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, ".gitignore"), "*.log");
        var filter = WalkFilter.Create(dir.Path, Options(useIgnoreFiles: false));

        var result = filter.LoadIgnoreRules(dir.Path, dir.Path, NetworkIgnoreRuleSet.Empty);

        Assert.IsFalse(result.IsIgnored(Path.Combine(dir.Path, "app.log"), "app.log", false));
    }

    [TestMethod]
    public void LoadIgnoreRules_GitignoreFile_RulesApplyToMatchingFiles()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, ".gitignore"), "*.log\n# a comment\n!keep.log");
        var filter = WalkFilter.Create(dir.Path, Options(useIgnoreFiles: true));

        var rules = filter.LoadIgnoreRules(dir.Path, dir.Path, NetworkIgnoreRuleSet.Empty);

        Assert.IsTrue(rules.IsIgnored(Path.Combine(dir.Path, "app.log"), "app.log", false));
        Assert.IsFalse(rules.IsIgnored(Path.Combine(dir.Path, "keep.log"), "keep.log", false));
        Assert.IsFalse(rules.IsIgnored(Path.Combine(dir.Path, "app.txt"), "app.txt", false));
    }

    [TestMethod]
    public void LoadIgnoreRules_NoIgnoreFilesPresent_ReturnsInheritedUnchanged()
    {
        using var dir = new TempDirectory();
        var filter = WalkFilter.Create(dir.Path, Options(useIgnoreFiles: true));

        var result = filter.LoadIgnoreRules(dir.Path, dir.Path, NetworkIgnoreRuleSet.Empty);

        Assert.IsFalse(result.IsIgnored(Path.Combine(dir.Path, "anything.txt"), "anything.txt", false));
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
