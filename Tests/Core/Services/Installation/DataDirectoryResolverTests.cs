using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

// [DoNotParallelize]: three of the tests below write DataDirectoryResolver.VolumeIsSupported, which is a
// process-wide seam, and a resolver call from any other test running beside them would read that override.
[TestClass]
[DoNotParallelize]
public sealed class DataDirectoryResolverTests
{
    [TestMethod]
    public void ResolveShared_PortableCopyPrefersItsOwnDataFolder() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Machine",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: true,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveUser_PortableCopyPrefersItsOwnDataFolder() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Users\20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: true,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveShared_PortableCopyWithoutDataUsesExistingInstalledData() => Assert.AreEqual(
            @"C:\ProgramData\Lertaro",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveUser_PortableCopyWithoutDataUsesExistingInstalledData() => Assert.AreEqual(
            @"C:\Users\testuser\AppData\Local\Lertaro",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveShared_PortableCopyWithoutAnyExistingDataUsesNewPortablePath() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Machine",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: false));

    [TestMethod]
    public void ResolveUser_PortableCopyWithoutAnyExistingDataUsesNewPortablePath() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Users\20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: false));

    [TestMethod]
    public void ResolveShared_InstalledCopyUsesProgramData() => Assert.AreEqual(
            @"C:\ProgramData\Lertaro",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Installed,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData"));

    [TestMethod]
    public void ResolveUser_InstalledCopyUsesLocalAppData() => Assert.AreEqual(
            @"C:\Users\testuser\AppData\Local\Lertaro",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Installed,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100"));

    // The reported deployment: the folder that holds the user's data is a link to another drive, and every
    // open below the root refuses links by design, so start-up must be handed the ordinary directory the
    // link names rather than the link. Targets are deliberately inside the test's own temp tree -- never a
    // real profile folder -- so cleaning up cannot reach anything a user owns.
    [TestMethod]
    public void ResolveFinalTarget_LinkedAncestor_ReturnsTheTargetDirectory()
    {
        if (!TempRootIsPlain) Assert.Inconclusive("This machine's temp path is itself relocated; see TempRootIsPlain.");

        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real", "Users")).FullName;
        var link = Path.Combine(tree.Root, "Data", "Users");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        tree.Link(link, real);

        // A leaf below the link is resolved with the link, not instead of it.
        Assert.AreEqual(Path.Combine(real, "20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2"),
            DataDirectoryResolver.ResolveFinalTarget(Path.Combine(link, "20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2")));
    }

    [TestMethod]
    public void ResolveFinalTarget_ComponentThatDoesNotExistYet_KeepsTheTailVerbatim()
    {
        if (!TempRootIsPlain) Assert.Inconclusive("This machine's temp path is itself relocated; see TempRootIsPlain.");

        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real")).FullName;
        var link = Path.Combine(tree.Root, "Data");
        tree.Link(link, real);

        Assert.AreEqual(Path.Combine(real, "Users", "fresh"),
            DataDirectoryResolver.ResolveFinalTarget(Path.Combine(link, "Users", "fresh")));
    }

    [TestMethod]
    public void ResolveFinalTarget_OrdinaryPath_IsUnchanged()
    {
        using var tree = new TempTree();
        var plain = Directory.CreateDirectory(Path.Combine(tree.Root, "Data", "Users")).FullName;
        Assert.AreEqual(Path.TrimEndingDirectorySeparator(plain), DataDirectoryResolver.ResolveFinalTarget(plain));
    }

    [TestMethod]
    public void ResolveFinalTarget_EmptyOrUnreachableInput_IsReturnedAsGiven()
    {
        Assert.AreEqual(string.Empty, DataDirectoryResolver.ResolveFinalTarget(string.Empty));
        var missing = Path.Combine(Path.GetTempPath(), "Lertaro-does-not-exist-" + Guid.NewGuid().ToString("N"));
        Assert.AreEqual(Path.TrimEndingDirectorySeparator(missing), DataDirectoryResolver.ResolveFinalTarget(missing));
    }

    // A relocated data folder is supported only on a local fixed disk, and that refusal has to name itself: the
    // layer below refuses links, which is how a relocation onto a network share was reported to the user as
    // another instance transferring settings.
    [TestMethod]
    public void DescribeRelocationRefusal_LinkToAnUnsupportedVolume_NamesBothPaths()
    {
        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real", "Users")).FullName;
        var link = Path.Combine(tree.Root, "Data", "Users");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        tree.Link(link, real);

        var previous = DataDirectoryResolver.VolumeIsSupported;
        try
        {
            DataDirectoryResolver.VolumeIsSupported = _ => false;
            var refusal = DataDirectoryResolver.DescribeRelocationRefusal(link);

            Assert.IsNotNull(refusal);
            StringAssert.Contains(refusal, "not a local fixed disk");
            StringAssert.Contains(refusal, link);
            StringAssert.Contains(refusal, real);
        }
        finally { DataDirectoryResolver.VolumeIsSupported = previous; }
    }

    [TestMethod]
    public void DescribeRelocationRefusal_LinkToALocalDisk_IsNotARefusal()
    {
        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real")).FullName;
        var link = Path.Combine(tree.Root, "Data");
        tree.Link(link, real);

        Assert.IsNull(DataDirectoryResolver.DescribeRelocationRefusal(link));
    }

    // A path that is simply unusual -- no link anywhere in its chain -- has always been allowed to try, and this
    // check has no business changing that.
    [TestMethod]
    public void DescribeRelocationRefusal_NoLinkAtAll_IsNotARefusal()
    {
        using var tree = new TempTree();
        var plain = Directory.CreateDirectory(Path.Combine(tree.Root, "Data", "Users")).FullName;

        var previous = DataDirectoryResolver.VolumeIsSupported;
        try
        {
            DataDirectoryResolver.VolumeIsSupported = _ => false;
            Assert.IsNull(DataDirectoryResolver.DescribeRelocationRefusal(plain));
            Assert.IsNull(DataDirectoryResolver.DescribeRelocationRefusal(string.Empty));
        }
        finally { DataDirectoryResolver.VolumeIsSupported = previous; }
    }

    // The same seam decides whether a resolution may be adopted at all: a volume this build will not vouch for
    // leaves the path exactly as it was, refusal and all.
    [TestMethod]
    public void ResolveFinalTarget_UnsupportedTargetVolume_LeavesTheLinkInPlace()
    {
        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real", "Users")).FullName;
        var link = Path.Combine(tree.Root, "Data", "Users");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        tree.Link(link, real);

        var previous = DataDirectoryResolver.VolumeIsSupported;
        try
        {
            DataDirectoryResolver.VolumeIsSupported = _ => false;
            Assert.AreEqual(link, DataDirectoryResolver.ResolveFinalTarget(link));
        }
        finally { DataDirectoryResolver.VolumeIsSupported = previous; }
    }

    // The pairing that makes the fix mean something: the settings backup layer refuses the link itself, and
    // accepts the directory the resolver hands it for the same folder.
    [TestMethod]
    public void ResolvedDataRoot_PassesTheLinkRefusalTheUnresolvedOneHits()
    {
        using var tree = new TempTree();
        var realUsers = Directory.CreateDirectory(Path.Combine(tree.Root, "real", "Users")).FullName;
        Directory.CreateDirectory(Path.Combine(realUsers, "abc"));
        var link = Path.Combine(tree.Root, "Data", "Users");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        tree.Link(link, realUsers);

        var unresolved = Path.Combine(link, "abc");
        Assert.ThrowsExactly<IOException>(() => SettingsBackup.RecoverInterruptedImports(unresolved));
        SettingsBackup.RecoverInterruptedImports(DataDirectoryResolver.ResolveFinalTarget(unresolved));
    }

    // End to end through the same call Logger makes, so the wiring is covered and not only the walk.
    [TestMethod]
    public void ResolveUser_PortableDataUnderALink_ReturnsTheLinkTarget()
    {
        if (!TempRootIsPlain) Assert.Inconclusive("This machine's temp path is itself relocated; see TempRootIsPlain.");

        using var tree = new TempTree();
        var real = Directory.CreateDirectory(Path.Combine(tree.Root, "real", "Users")).FullName;
        Directory.CreateDirectory(Path.Combine(tree.Root, "Data"));
        tree.Link(Path.Combine(tree.Root, "Data", "Users"), real);

        Assert.AreEqual(
            Path.Combine(real, "20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2"),
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable, tree.Root, @"C:\nonexistent-local-appdata", "S-1-5-21-100"));
    }

    // A machine whose own profile was relocated to another drive puts a link in %TEMP% as well, and then no
    // literal expectation below is what a correct resolver returns. Saying so beats weakening the assertion.
    private static bool TempRootIsPlain => string.Equals(
        DataDirectoryResolver.ResolveFinalTarget(Path.GetTempPath()),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
        StringComparison.OrdinalIgnoreCase);

    private sealed class TempTree : IDisposable
    {
        private readonly List<string> _links = [];

        internal TempTree() => Root = Directory.CreateTempSubdirectory("LertaroDataRoot-").FullName;

        internal string Root { get; }

        internal void Link(string link, string target)
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "/d", "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, output);
            _links.Add(link);
        }

        public void Dispose()
        {
            // The links first, one at a time: a recursive delete of the root would walk through them.
            foreach (var link in _links)
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
