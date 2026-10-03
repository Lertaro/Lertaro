using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

// WalkOptions.From is the single construction of a walk's exclusion rules, shared by every walk that
// honours them: the network/WSL/folder-index path (DriveRefreshRunner) builds it from the user's settings,
// and the non-journal local-drive path (LocalDriveWalkBuilder, driven from the service) from the machine
// copy of those settings. Both sources are pinned here, so a local drive is never filtered by a different
// rule set than a network drive -- and these cover the mapping itself, the values the network path used
// to build inline.
[TestClass]
public sealed class WalkOptionsTests
{
    [TestMethod]
    public void FromUserSettings_CarriesAllThreeExclusionLists()
    {
        var settings = new UserSettings
        {
            ExcludedPaths = [@"C:\excluded", @"D:\also"],
            IgnoredPathGlobs = ["node_modules", "*.tmp"],
            IgnoredPathRegexes = ["^secret-"],
        };

        var options = WalkOptions.FromUserSettings(settings);

        CollectionAssert.AreEqual(new[] { @"C:\excluded", @"D:\also" }, options.ExcludedPaths.ToArray());
        CollectionAssert.AreEqual(new[] { "node_modules", "*.tmp" }, options.IgnoredPathGlobs.ToArray());
        CollectionAssert.AreEqual(new[] { "^secret-" }, options.IgnoredPathRegexes.ToArray());
    }

    // What the network path has always walked with, and therefore what a local drive's walk must get too:
    // no depth cap, an automatic worker count, and ignore files honoured.
    [TestMethod]
    public void FromUserSettings_UsesTheSameFlagsAsTheNetworkPath()
    {
        var options = WalkOptions.FromUserSettings(new UserSettings());

        Assert.AreEqual(0, options.MaxDepth);
        Assert.AreEqual(0, options.WorkerCount);
        Assert.IsTrue(options.UseIgnoreFiles);
    }

    // The other half of "the same construction": neither walk may grow its own inline WalkOptions that
    // reads settings a different way. Builders go through From() above, and this scan keeps a third one
    // from quietly appearing. Checked as source text rather than behaviour because a duplicate
    // construction reads settings at a place no unit test can reach -- the same reason
    // RepositoryHygieneTests scans the tree.
    [TestMethod]
    public void WalkOptions_HasNoSecondProductionConstructionSite()
    {
        var coreDirectory = Path.Combine(RepoRoot(), "Core");
        var offenders = Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(path => Path.GetFileName(path) != "WalkOptions.cs")
            .Where(path => File.ReadAllText(path).Contains("new WalkOptions(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(coreDirectory, path))
            .ToList();

        Assert.IsEmpty(offenders,
            "WalkOptions must only be built by WalkOptions.From; found: " + string.Join(", ", offenders));
    }

    // The other source, for the walk that cannot use the first one: a local drive's walk runs in the
    // --service process, whose UserSettings path does not exist, so its rules come off the machine copy
    // the App mirrors them into. Same three lists through the same From() field list, and still the flags
    // the network path walks with -- a FAT32/exFAT drive must not be filtered differently from a UNC one.
    [TestMethod]
    public void FromMachineSettings_CarriesAllThreeExclusionListsAndTheNetworkFlags()
    {
        var machine = new MachineSettings
        {
            ExcludedPaths = [@"C:\excluded", @"D:\also"],
            IgnoredPathGlobs = ["node_modules", "*.tmp"],
            IgnoredPathRegexes = ["^secret-"],
        };

        var options = WalkOptions.FromMachineSettings(machine);

        CollectionAssert.AreEqual(new[] { @"C:\excluded", @"D:\also" }, options.ExcludedPaths.ToArray());
        CollectionAssert.AreEqual(new[] { "node_modules", "*.tmp" }, options.IgnoredPathGlobs.ToArray());
        CollectionAssert.AreEqual(new[] { "^secret-" }, options.IgnoredPathRegexes.ToArray());
        Assert.AreEqual(0, options.MaxDepth);
        Assert.AreEqual(0, options.WorkerCount);
        Assert.IsTrue(options.UseIgnoreFiles);
    }

    // The regression itself: reading the rules off UserSettings in the service silently produced defaults
    // (the per-user directory does not exist for LocalSystem), so the walk filtered by nothing and the
    // user's ExcludedPaths were a no-op. Nothing on the local walk path -- builder or its service-side
    // caller -- may reach for UserSettings again; the rules travel in as an argument instead. Scanned as
    // source text for the reason the scan above gives: which settings store a process resolves to is
    // invisible to a unit test, and that silent difference was the whole bug.
    [TestMethod]
    public void LocalDriveWalkPath_NeverReadsUserSettings()
    {
        var usnDirectory = Path.Combine(RepoRoot(), "Core", "Indexer", "Usn");
        var offenders = Directory.EnumerateFiles(usnDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(path => File.ReadAllLines(path).Any(line => IsCode(line) && line.Contains("UserSettings", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(usnDirectory, path))
            .ToList();

        Assert.IsEmpty(offenders,
            "the local drive walk runs in the service, where UserSettings does not exist; it must take its rules from its caller (WalkOptions.FromMachineSettings). Found: "
            + string.Join(", ", offenders));
    }

    // Comment-only mentions are skipped: these files are supposed to explain *why* UserSettings is the
    // wrong store for the service, which means naming it. Only executable text is the dependency this
    // guards against.
    private static bool IsCode(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static bool IsGeneratedPath(string path) =>
        path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
