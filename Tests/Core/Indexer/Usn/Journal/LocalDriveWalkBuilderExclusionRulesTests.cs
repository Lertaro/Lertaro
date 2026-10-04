using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.Indexer.Usn.Journal;

namespace Lertaro.Core.Tests.Indexer.Usn.Journal;

// The local (FAT32/exFAT) drive walk runs in the --service process, where UserSettings.Load() resolves to
// a per-user directory that does not exist for LocalSystem -- it returned defaults, so the user's own
// ExcludedPaths never reached the walk. The rules now travel user settings -> SetMachineSettings request
// -> the indexer's in-memory WalkOptions -> the walk's real filter. This is that chain end to end,
// through the actual walk rather than a hand-built options object.
[TestClass]
public sealed class LocalDriveWalkBuilderExclusionRulesTests
{
    [TestMethod]
    public void Build_RulesFromTheRequest_ExcludeTheListedDirectoryAndKeepItsSibling()
    {
        using var dir = new TempDirectory();
        var excluded = Path.Combine(dir.Path, "excluded");
        Directory.CreateDirectory(Path.Combine(excluded, "deeper"));
        File.WriteAllText(Path.Combine(excluded, "skipped.txt"), "x");
        File.WriteAllText(Path.Combine(excluded, "deeper", "skipped-too.txt"), "x");
        var sibling = Path.Combine(dir.Path, "sibling");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "kept.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            WalkOptions.From([excluded], [], []));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("excluded", names);
        Assert.DoesNotContain("deeper", names);
        Assert.DoesNotContain("skipped.txt", names);
        Assert.DoesNotContain("skipped-too.txt", names);
        CollectionAssert.Contains(names, "sibling");
        CollectionAssert.Contains(names, "kept.txt");
    }

    // The other half of the same chain, and the reason a request carries all three lists rather than just
    // the excluded paths: a glob/regex pair set by the user has to filter the walk the same way an
    // exclusion path does, since all three travel together.
    [TestMethod]
    public void Build_RulesFromTheRequest_HonourGlobsAndRegexesToo()
    {
        using var dir = new TempDirectory();
        var modules = Path.Combine(dir.Path, "node_modules");
        Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "pkg.js"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "secret-plan.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "kept.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            WalkOptions.From([], ["node_modules"], ["^secret-"]));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("node_modules", names);
        Assert.DoesNotContain("pkg.js", names);
        Assert.DoesNotContain("secret-plan.txt", names);
        CollectionAssert.Contains(names, "kept.txt");
    }

    // The default state of a service that has never been sent a rule, and the reason it is safe: an empty
    // rule set filters nothing, which is the upstream behavior. Asserted through a real walk so the empty
    // default is pinned where it actually matters -- a default that quietly excluded something would be
    // invisible in any assertion about the rule objects themselves.
    [TestMethod]
    public void Build_NoRulesSent_FiltersNothing()
    {
        using var dir = new TempDirectory();
        var sub = Path.Combine(dir.Path, "node_modules");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "pkg.js"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "kept.txt"), "x");

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            WalkOptions.Empty);

        var names = store.Records.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "node_modules");
        CollectionAssert.Contains(names, "pkg.js");
        CollectionAssert.Contains(names, "kept.txt");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
