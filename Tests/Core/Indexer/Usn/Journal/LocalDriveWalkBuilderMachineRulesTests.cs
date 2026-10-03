using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.Indexer.Usn.Journal;

namespace Lertaro.Core.Tests.Indexer.Usn.Journal;

// The local (FAT32/exFAT) drive walk runs in the --service process, where UserSettings.Load() resolves to
// a per-user directory that does not exist for LocalSystem -- it returned defaults, so the user's own
// ExcludedPaths never reached the walk. The rules now travel user settings -> machine mirror
// (MachineSettings, the one settings file the service can read) -> WalkOptions -> the walk's real filter.
// This is that chain end to end, through the actual file rather than a hand-built options object.
[TestClass]
public sealed class LocalDriveWalkBuilderMachineRulesTests
{
    [TestMethod]
    public void Build_OptionsFromTheMachineMirror_ExcludeTheListedDirectoryAndKeepItsSibling()
    {
        using var dir = new TempDirectory();
        var excluded = Path.Combine(dir.Path, "excluded");
        Directory.CreateDirectory(Path.Combine(excluded, "deeper"));
        File.WriteAllText(Path.Combine(excluded, "skipped.txt"), "x");
        File.WriteAllText(Path.Combine(excluded, "deeper", "skipped-too.txt"), "x");
        var sibling = Path.Combine(dir.Path, "sibling");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "kept.txt"), "x");

        var machineSettingsPath = Path.Combine(dir.Path, "machine-settings.json");
        MachineSettings.MirrorExclusionRules(
            new UserSettings { ExcludedPaths = [excluded], IgnoredPathGlobs = [], IgnoredPathRegexes = [] },
            machineSettingsPath);
        var mirror = MachineSettings.TryLoadFromFile(machineSettingsPath);
        Assert.IsNotNull(mirror);

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            WalkOptions.FromMachineSettings(mirror));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("excluded", names);
        Assert.DoesNotContain("deeper", names);
        Assert.DoesNotContain("skipped.txt", names);
        Assert.DoesNotContain("skipped-too.txt", names);
        CollectionAssert.Contains(names, "sibling");
        CollectionAssert.Contains(names, "kept.txt");
    }

    // The other half of the same chain, and the reason a machine file is written even when the user never
    // touches the drive selection: a glob/regex pair set by the user has to filter the walk the same way
    // the exclusion path above does, since all three lists travel in one field list.
    [TestMethod]
    public void Build_OptionsFromTheMachineMirror_HonourGlobsAndRegexesToo()
    {
        using var dir = new TempDirectory();
        var modules = Path.Combine(dir.Path, "node_modules");
        Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "pkg.js"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "secret-plan.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "kept.txt"), "x");

        var machineSettingsPath = Path.Combine(dir.Path, "machine-settings.json");
        MachineSettings.MirrorExclusionRules(
            new UserSettings { ExcludedPaths = [], IgnoredPathGlobs = ["node_modules"], IgnoredPathRegexes = ["^secret-"] },
            machineSettingsPath);
        var mirror = MachineSettings.TryLoadFromFile(machineSettingsPath);
        Assert.IsNotNull(mirror);

        var store = LocalDriveWalkBuilder.Build("Z", dir.Path, previousStore: null, (_, _) => { }, CancellationToken.None,
            WalkOptions.FromMachineSettings(mirror));

        var names = store.Records.Select(r => r.Name).ToList();
        Assert.DoesNotContain("node_modules", names);
        Assert.DoesNotContain("pkg.js", names);
        Assert.DoesNotContain("secret-plan.txt", names);
        CollectionAssert.Contains(names, "kept.txt");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
