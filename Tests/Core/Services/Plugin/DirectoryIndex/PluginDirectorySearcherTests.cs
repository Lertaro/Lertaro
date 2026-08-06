using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.Core.Tests.Services.Plugin.DirectoryIndex;

// Only the match decision is covered: the rest of the searcher is one call per registered directory
// into IndexedDirectoryEnumerator, which talks to the service pipe or the filesystem and has no
// injectable seam. The pattern and recursion flags are no longer this class's business at all -- it
// passes each registration's own values straight down (see DirectoryEnumeratorTests for what they do).
[TestClass]
public sealed class PluginDirectorySearcherTests
{
    [TestMethod]
    public void MatchesQuery_EmptyQuery_KeepsEverything()
    {
        Assert.IsTrue(PluginDirectorySearcher.MatchesQuery("anything.txt", ""));
        Assert.IsTrue(PluginDirectorySearcher.MatchesQuery("anything.txt", "   "));
    }

    [TestMethod]
    public void MatchesQuery_MatchingName_IsKept()
    {
        Assert.IsTrue(PluginDirectorySearcher.MatchesQuery("report.txt", "report"));
        Assert.IsTrue(PluginDirectorySearcher.MatchesQuery("Report.TXT", "report"));
    }

    // Fuzzy, not a substring test: the same query would find this file anywhere else in the app, and a
    // plugin searching its own directories should not get a stricter answer than the search box does.
    [TestMethod]
    public void MatchesQuery_NonContiguousCharacters_StillMatch()
        => Assert.IsTrue(PluginDirectorySearcher.MatchesQuery("annual-report-2024.txt", "arep"));

    [TestMethod]
    public void MatchesQuery_UnrelatedName_IsDropped()
        => Assert.IsFalse(PluginDirectorySearcher.MatchesQuery("notes.txt", "zzz"));
}
