using System.Text.RegularExpressions;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// ParseExcludedPatterns logs invalid entries through the process-wide logger hook.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexConfigTests
{
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void CaptureLogs()
    {
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void ReleaseLogs() => PluginSdk.Logger.LogAction = null;

    [TestMethod]
    public void ParseExcludedPatterns_MixedValidAndInvalid_KeepsOnlyValid()
    {
        var patterns = ContentIndexConfig.ParseExcludedPatterns(@"backup\\;([unclosed;.*\.tmp$");

        Assert.HasCount(2, patterns);
        Assert.IsTrue(_logLines.Any(l => l.Contains("invalid exclusion pattern", StringComparison.Ordinal) && l.Contains("([unclosed", StringComparison.Ordinal)),
            $"Expected a warning for the broken pattern: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public void ParseExcludedPatterns_EmptyInput_ReturnsEmpty()
    {
        Assert.IsEmpty(ContentIndexConfig.ParseExcludedPatterns(null));
        Assert.IsEmpty(ContentIndexConfig.ParseExcludedPatterns(string.Empty));
        Assert.IsEmpty(ContentIndexConfig.ParseExcludedPatterns("   ;  ;"));
    }

    [TestMethod]
    public void IsExcluded_FilePathMatch_IsCaseInsensitive()
    {
        var config = new ContentIndexConfig
        {
            ExcludedPatterns = ContentIndexConfig.ParseExcludedPatterns(@"\\\\server\\private\\")
        };

        Assert.IsTrue(config.IsExcluded(@"\\SERVER\PRIVATE\report.txt"));
        Assert.IsFalse(config.IsExcluded(@"\\server\public\report.txt"));
    }

    [TestMethod]
    public void IsExcluded_FolderPatternMatch_ExcludesWholeSubtree()
    {
        var config = new ContentIndexConfig
        {
            ExcludedPatterns = ContentIndexConfig.ParseExcludedPatterns(@"\\Backup\\")
        };

        // The folder itself and everything below it, without any special subtree logic:
        // the pattern matches the ancestor's path embedded in every descendant path.
        Assert.IsTrue(config.IsExcluded(@"D:\Data\Backup\2024\old.pdf"));
        Assert.IsTrue(config.IsExcluded(@"D:\Data\Backup\notes.txt"));
        Assert.IsFalse(config.IsExcluded(@"D:\Data\BackupRestore\keep.pdf"));
    }

    [TestMethod]
    public void IsExcluded_NoPatterns_NeverExcludes()
    {
        var config = new ContentIndexConfig();
        Assert.IsFalse(config.IsExcluded(@"D:\anything\file.txt"));
    }

    [TestMethod]
    public void ParseExcludedPatterns_CompiledPatterns_UseTimeoutGuard()
    {
        var patterns = ContentIndexConfig.ParseExcludedPatterns("a+ b+");
        Assert.HasCount(1, patterns);
        Assert.IsTrue(patterns[0].MatchTimeout > TimeSpan.Zero, "User-supplied regexes must run under a match timeout");
        Assert.AreEqual(RegexOptions.IgnoreCase, patterns[0].Options & RegexOptions.IgnoreCase);
    }
}
