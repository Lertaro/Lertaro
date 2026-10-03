using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

[TestClass]
public sealed class InstallDirectoryLockTests
{
    // Every right that lets a holder change what a SYSTEM process later reads, runs or writes through.
    private const FileSystemRights AnyWrite = FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static readonly SecurityIdentifier CurrentUser = WindowsIdentity.GetCurrent().User!;

    private string _temp = null!;

    [TestInitialize]
    public void Setup()
    {
        _temp = Path.Combine(Path.GetTempPath(), "LertaroLockTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_temp, true); } catch { }
    }

    [TestMethod]
    public void ReadOnlyForUsers_AtTheTop_IsProtectedOwnedByAdministratorsAndGrantsNoOneElseWrite()
    {
        var descriptor = InstallDirectoryLock.Describe(InstallDirectoryLock.ReadOnlyForUsers, isZoneRoot: true, isDirectory: true);

        Assert.IsTrue(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
        Assert.AreEqual(InstallDirectoryLock.Administrators, descriptor.Owner);
        AssertOnlyServiceAccountsCanWrite(descriptor);
        Assert.IsTrue(Aces(descriptor).Any(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users &&
            ((FileSystemRights)ace.AccessMask).HasFlag(FileSystemRights.ReadAndExecute)));
    }

    [TestMethod]
    public void PrivateToService_GrantsUsersNothing()
    {
        var descriptor = InstallDirectoryLock.Describe(InstallDirectoryLock.PrivateToService, isZoneRoot: true, isDirectory: true);

        Assert.IsFalse(Aces(descriptor).Any(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users));
        AssertOnlyServiceAccountsCanWrite(descriptor);
    }

    [TestMethod]
    public void Describe_BelowTheTop_HandsDownInheritedAces()
    {
        var zone = InstallDirectoryLock.ReadOnlyForUsers;

        var directory = InstallDirectoryLock.Describe(zone, isZoneRoot: false, isDirectory: true);
        var file = InstallDirectoryLock.Describe(zone, isZoneRoot: false, isDirectory: false);

        Assert.IsFalse(directory.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
        Assert.IsTrue(Aces(directory).All(ace => ace.AceFlags == (AceFlags.ObjectInherit | AceFlags.ContainerInherit | AceFlags.Inherited)));
        Assert.IsTrue(Aces(file).All(ace => ace.AceFlags == AceFlags.Inherited));
        Assert.HasCount(zone.Aces.Count, Aces(file));
        AssertOnlyServiceAccountsCanWrite(file);
    }

    [TestMethod]
    public void Describe_BelowTheTop_TurnsCreatorOwnerIntoTheOwnerAndDropsThisFolderOnlyAces()
    {
        var zone = new InstallDirectoryLock.Zone(CurrentUser,
        [
            InstallDirectoryLock.Allow(InstallDirectoryLock.Users, FileSystemRights.CreateDirectories, AceFlags.None),
            InstallDirectoryLock.Allow(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), FileSystemRights.FullControl,
                AceFlags.ObjectInherit | AceFlags.ContainerInherit | AceFlags.InheritOnly),
        ]);

        var file = Aces(InstallDirectoryLock.Describe(zone, isZoneRoot: false, isDirectory: false));

        Assert.HasCount(1, file);
        Assert.AreEqual(CurrentUser, file[0].SecurityIdentifier);
    }

    [TestMethod]
    public void Lock_RemovesPlantedLinksWithoutTouchingWhatTheyPointAt()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_temp, "outside")).FullName;
        var outsideFile = Path.Combine(outside, "victim.txt");
        File.WriteAllText(outsideFile, "keep me");
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, "logs", "service.log"), "log");
        Mklink("/J", Path.Combine(root, "junction"), outside);
        Mklink("/H", Path.Combine(root, "hardlink.txt"), outsideFile);

        var report = InstallDirectoryLock.Lock(root, OwnedByMe(), _ => null);

        Assert.IsFalse(Directory.Exists(Path.Combine(root, "junction")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "hardlink.txt")));
        Assert.AreEqual("keep me", File.ReadAllText(outsideFile));
        Assert.HasCount(2, report.Removed);
        Assert.IsEmpty(report.Failed);
        // The walk never reached through the junction to the directory it pointed at.
        Assert.IsFalse(new DirectoryInfo(outside).GetAccessControl().AreAccessRulesProtected);
    }

    [TestMethod]
    public void Lock_ProtectsTheTopAndResetsEverythingBelowToInherit()
    {
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        var log = Path.Combine(logs, "service.log");
        File.WriteAllText(log, "log");
        // An explicit grant a planting user might have left on its own file.
        var planted = new FileInfo(log).GetAccessControl();
        planted.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(log).SetAccessControl(planted);

        InstallDirectoryLock.Lock(root, OwnedByMe(), _ => null);

        Assert.IsTrue(new DirectoryInfo(root).GetAccessControl().AreAccessRulesProtected);
        var logRules = new FileInfo(log).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        Assert.IsTrue(logRules.Cast<FileSystemAccessRule>().All(rule => rule.IsInherited));
        Assert.IsFalse(logRules.Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference.Value == "S-1-1-0"));
    }

    [TestMethod]
    public void Lock_StartsANewZoneWhereAsked()
    {
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        var indexes = Directory.CreateDirectory(Path.Combine(root, "indexes")).FullName;
        var privateZone = new InstallDirectoryLock.Zone(CurrentUser,
            [InstallDirectoryLock.Allow(CurrentUser, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit)]);

        InstallDirectoryLock.Lock(root, OwnedByMe(), path => path == indexes ? privateZone : null);

        var security = new DirectoryInfo(indexes).GetAccessControl();
        Assert.IsTrue(security.AreAccessRulesProtected);
        Assert.HasCount(1, security.GetAccessRules(true, true, typeof(SecurityIdentifier)));
    }

    [TestMethod]
    public void Lock_TopThatIsALink_IsRefusedAndLeftInPlace()
    {
        var target = Directory.CreateDirectory(Path.Combine(_temp, "target")).FullName;
        var link = Path.Combine(_temp, "link");
        Mklink("/J", link, target);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => InstallDirectoryLock.Lock(link, OwnedByMe(), _ => null));
        Assert.IsTrue(Directory.Exists(link));
        Assert.IsFalse(new DirectoryInfo(target).GetAccessControl().AreAccessRulesProtected);
    }

    // The production zones make Administrators the owner, which a non-elevated test cannot assign; the walk
    // is the same whoever owns the result.
    private static InstallDirectoryLock.Zone OwnedByMe() => new(CurrentUser,
        [.. InstallDirectoryLock.ReadOnlyForUsers.Aces, InstallDirectoryLock.Allow(CurrentUser, FileSystemRights.FullControl,
            AceFlags.ObjectInherit | AceFlags.ContainerInherit)]);

    internal static List<CommonAce> Aces(RawSecurityDescriptor descriptor) =>
        descriptor.DiscretionaryAcl!.Cast<CommonAce>().ToList();

    private static void AssertOnlyServiceAccountsCanWrite(RawSecurityDescriptor descriptor)
    {
        foreach (var ace in Aces(descriptor).Where(ace => ((FileSystemRights)ace.AccessMask & AnyWrite) != 0))
        {
            Assert.IsTrue(ace.SecurityIdentifier == InstallDirectoryLock.LocalSystem ||
                ace.SecurityIdentifier == InstallDirectoryLock.Administrators,
                $"{ace.SecurityIdentifier} can write");
        }
    }

    // Junctions and hard links need no privilege, so a test can plant them the way a standard user would.
    private static void Mklink(string kind, string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink {kind} \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"mklink {kind} failed");
    }
}
