namespace Lertaro.Core.Tests;

[TestClass]
public sealed class ExclusionRuleSetTests
{
    private static UserSettings EmptySettings() => new()
    {
        ExcludedPaths = new List<string>(),
        IgnoredPathGlobs = new List<string>(),
        IgnoredPathRegexes = new List<string>()
    };
    [TestMethod]
    public void Empty_ExcludesNothing() => Assert.IsFalse(ExclusionRuleSet.Empty.IsExcludedPath(@"c:\anything\at\all.txt", isDirectory: false));
    [TestMethod]
    public void IsExcludedPath_BlankPath_ReturnsFalse() => Assert.IsFalse(ExclusionRuleSet.Empty.IsExcludedPath("", isDirectory: false));
    [TestMethod]
    public void IsExcludedPath_PathUnderExcludedRoot_IsExcluded()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows.old");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\windows.old\system32\file.dll", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_PathOutsideExcludedRoot_IsNotExcluded()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows.old");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\file.txt", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_GlobMatchOnFileName_IsExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\app\node_modules", isDirectory: true));
    }
    [TestMethod]
    public void IsExcludedPath_GlobMatchOnAncestorDirectory_ExcludesDescendants()
    {
        // A file nested inside an ignored directory is excluded too -- IsExcludedPath walks up through
        // every parent directory, not just the path's own final segment.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\app\node_modules\lodash\index.js", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_NoMatchingGlobOrRoot_IsNotExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\app\src\index.js", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_RegexMatchOnFileName_IsExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathRegexes.Add(@"\.tmp$");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\cache.tmp", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_DotPrefixedGlob_MatchesHiddenStyleFolders()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add(".*");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\projects\.git", isDirectory: true));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\projects\src", isDirectory: true));
    }
    [TestMethod]
    public void IsExcludedPath_ExemptRoot_OverridesExcludedRoot()
    {
        // exemptRoot lets a caller explicitly re-include a path that would otherwise be excluded --
        // e.g. the user manually configured a folder index inside an excluded root.
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\data");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false, exemptRoot: @"c:\data"));
    }
    [TestMethod]
    public void IsExcludedPath_ExemptRootUnderExcludedRoot_DoesNotLeakToResultsOutsideIt()
    {
        // The exemption covers only the subtree the caller pointed at -- an exempt root sitting inside
        // an excluded one must not smuggle out sibling results that were never under it.
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\data");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\data\other.txt", isDirectory: false, exemptRoot: @"c:\data\sub"));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\data\sub\file.txt", isDirectory: false, exemptRoot: @"c:\data\sub"));
    }
    [TestMethod]
    public void IsExcludedPath_ExemptAncestorOfExcludedRoot_ReincludesOnlyTheRootRow()
    {
        // An exempt ancestor re-includes the excluded root's own row -- that row is the location the
        // caller pointed at, not "content under the excluded root" -- while everything still under the
        // root stays excluded.
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\data");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\data", isDirectory: true, exemptRoot: @"c:\"));
        Assert.IsTrue(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false, exemptRoot: @"c:\"));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\data\other", isDirectory: true, exemptRoot: @"c:\data"));
    }
    [TestMethod]
    public void IsExcluded_UsesCanonicalIndexedPathWithoutExpandingShortNames()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("~*");
        var rules = ExclusionRuleSet.From(settings, @"c:\");
        var result = new SearchResult { Path = @"c:\packages\~cache\file.txt", IsDir = false };

        Assert.IsTrue(rules.IsExcluded(result));
        Assert.AreEqual(@"c:\packages\~cache\file.txt", ExclusionRuleSet.NormalizeIndexedPath(result.Path, result.IsDir));
    }
    [TestMethod]
    public void NormalizeIndexedPath_AppendsOnlyMissingDirectorySeparator()
    {
        Assert.AreEqual(@"c:\data\", ExclusionRuleSet.NormalizeIndexedPath(@"c:\data", isDirectory: true));
        Assert.AreEqual(@"c:\data\", ExclusionRuleSet.NormalizeIndexedPath(@"c:\data\", isDirectory: true));
        Assert.AreEqual(@"c:\data.txt", ExclusionRuleSet.NormalizeIndexedPath(@"c:\data.txt", isDirectory: false));
    }
    [TestMethod]
    public void IsExcludedPath_WslPathUsesLexicalNormalization()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"\\wsl$\Ubuntu\home\testuser\cache");
        var rules = ExclusionRuleSet.From(settings);

        Assert.IsTrue(rules.IsExcludedPath(@"\\wsl$\Ubuntu/home/testuser/cache/file.txt", isDirectory: false));
    }
    [TestMethod]
    public void InvalidateCache_DoesNotThrow() => ExclusionRuleSet.InvalidateCache();
    // Ancestor verdicts are memoised per directory (see AncestorIsIgnored) -- an ignored directory's
    // answer is asked for once and reused by everything beneath it, which is what makes deciding this
    // for every match on a drive affordable. Everything below guards that the cache cannot answer for
    // a path it was never really about; a wrong verdict here silently drops results from a search,
    // which is the one failure mode nothing downstream would reveal.
    [TestMethod]
    public void IsExcludedPath_ManySiblingsUnderAnIgnoredDirectory_AllStayExcluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");
        for (var i = 0; i < 50; i++)
            Assert.IsTrue(rules.IsExcludedPath($@"c:\app\node_modules\pkg{i}\deep\index.js", isDirectory: false), $"sibling {i}");
    }

    [TestMethod]
    public void IsExcludedPath_ManySiblingsUnderAnIncludedDirectory_AllStayIncluded()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        for (var i = 0; i < 50; i++)
            Assert.IsFalse(rules.IsExcludedPath($@"c:\app\src\pkg{i}\deep\index.js", isDirectory: false), $"sibling {i}");
    }

    [TestMethod]
    public void IsExcludedPath_AnIgnoredSubtreeDoesNotContaminateItsSiblings()
    {
        // The memo is keyed per directory, so writing "excluded" back to a whole ancestor chain must
        // only cover the levels actually walked -- never a sibling that happens to share a parent.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules\x\y.js", isDirectory: false));

        Assert.IsFalse(rules.IsExcludedPath(@"c:\app\src\x\y.js", isDirectory: false));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\app\other.js", isDirectory: false));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\app", isDirectory: true));
    }

    [TestMethod]
    public void IsExcludedPath_AnIncludedSubtreeSeenFirstDoesNotMaskAnIgnoredOne()
    {
        // Order dependence would be the other way the memo could lie: caching "not excluded" for the
        // shared ancestors first, then trusting it for a path whose OWN segment is ignored.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\app\src\index.js", isDirectory: false));

        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules\index.js", isDirectory: false));
        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules", isDirectory: true));
    }

    [TestMethod]
    public void IsExcludedPath_TheSamePathRepeated_KeepsTheSameAnswer()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        for (var i = 0; i < 5; i++)
        {
            Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules\a.js", isDirectory: false));
            Assert.IsFalse(rules.IsExcludedPath(@"c:\app\src\a.js", isDirectory: false));
        }
    }

    [TestMethod]
    public void IsExcludedPath_DeepestFirstThenShallower_AgreesBothWays()
    {
        // Writing a verdict back to every level walked has to leave those levels individually correct,
        // not just the one that was asked about.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\a\b\node_modules\c\d\e\f.js", isDirectory: false));

        Assert.IsTrue(rules.IsExcludedPath(@"c:\a\b\node_modules\c\d", isDirectory: true));
        Assert.IsTrue(rules.IsExcludedPath(@"c:\a\b\node_modules\c", isDirectory: true));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\a\b", isDirectory: true));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\a", isDirectory: true));
    }

    [TestMethod]
    public void IsExcludedPath_ADriveRootPath_DoesNotWalkForever()
    {
        // The walk stops when the parent no longer strictly shortens the path. Comparing for inequality
        // instead is what let a malformed chain loop without end.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\", isDirectory: true));
        Assert.IsFalse(rules.IsExcludedPath(@"c:\a.txt", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_ADirectoryAndAFileAtTheSamePath_AgreeOnTheGlob()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules", isDirectory: true));
        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules", isDirectory: false));
    }

    [TestMethod]
    public void IsExcludedPath_ConcurrentCallers_AgreeWithTheSingleThreadedAnswer()
    {
        // Both of the streaming search's sources call in at once (SearchService's localTask and
        // networkTask share one rule set), so the memo is written from several threads.
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        var paths = Enumerable.Range(0, 400)
            .Select(i => i % 2 == 0
                ? $@"c:\app\node_modules\p{i % 20}\deep\f{i}.js"
                : $@"c:\app\src\p{i % 20}\deep\f{i}.js")
            .ToArray();
        var expected = paths.Select(p => p.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase)).ToArray();

        var actual = new bool[paths.Length];
        Parallel.For(0, paths.Length, i => actual[i] = rules.IsExcludedPath(paths[i], isDirectory: false));

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void IsExcludedPath_ExemptRoot_IsNotBakedIntoTheMemo()
    {
        // exemptRoot only ever affects the excluded-roots check, never the glob walk, which is why the
        // glob verdict can be cached without it. If that ever stops being true this fails rather than
        // quietly serving one caller's exemption to another.
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\data");
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false, exemptRoot: @"c:\data"));

        Assert.IsTrue(rules.IsExcludedPath(@"c:\data\file.txt", isDirectory: false));
        Assert.IsTrue(rules.IsExcludedPath(@"c:\app\node_modules\file.txt", isDirectory: false, exemptRoot: @"c:\data"));
    }
}
