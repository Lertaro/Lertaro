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

    [TestMethod]
    public void Lock_ReadOnlyForUsers_StillLetsAUserOpenAndReadAFile()
    {
        // Issue #316: the App, not elevated, could no longer read machine-settings.json once the service had
        // locked the shared data dir. Granted to the current user here, since OwnedByMe's full control would
        // hide it; the owner's implicit rights include neither reading nor listing.
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        var settings = Path.Combine(root, "machine-settings.json");
        File.WriteAllText(settings, "{}");
        var users = InstallDirectoryLock.ReadOnlyForUsers.Aces.Single(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users);
        var readOnly = new InstallDirectoryLock.Zone(CurrentUser,
            [new CommonAce(users.AceFlags, users.AceQualifier, users.AccessMask, CurrentUser, false, null)]);

        try
        {
            InstallDirectoryLock.Lock(root, readOnly, _ => null);

            Assert.AreEqual("{}", File.ReadAllText(settings));
            Assert.HasCount(1, Directory.GetFiles(root));
        }
        finally
        {
            // The owner may always rewrite the DACL; inheritance carries it down so Cleanup can delete.
            var restore = new DirectorySecurity();
            restore.AddAccessRule(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(restore);
        }
    }

    [TestMethod]
    public void Allow_GrantsSynchronize()
    {
        // FileSystemAccessRule adds it to every allow rule; a CommonAce built by hand has to, or a FileStream
        // (which asks for GENERIC_READ or GENERIC_WRITE, both including SYNCHRONIZE) is refused outright.
        var ace = InstallDirectoryLock.Allow(InstallDirectoryLock.Users, FileSystemRights.ReadAndExecute, AceFlags.None);

        Assert.AreEqual(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, (FileSystemRights)ace.AccessMask);
    }

    [TestMethod]
    public void GrantsAtLeast_OnlyForATreeLockedWithThatZone()
    {
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        var current = OwnedByMe();
        // The zone as 5.8.2 wrote it: the Users ACE without SYNCHRONIZE.
        var stale = current with
        {
            Aces = [.. current.Aces.Select(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users
                ? new CommonAce(ace.AceFlags, ace.AceQualifier, ace.AccessMask & ~(int)FileSystemRights.Synchronize, ace.SecurityIdentifier, false, null)
                : ace)],
        };

        Assert.IsFalse(InstallDirectoryLock.GrantsAtLeast(root, current), "never locked");

        InstallDirectoryLock.Lock(root, stale, _ => null);
        Assert.IsFalse(InstallDirectoryLock.GrantsAtLeast(root, current), "locked by an older build");

        InstallDirectoryLock.Lock(root, current, _ => null);
        Assert.IsTrue(InstallDirectoryLock.GrantsAtLeast(root, current));
    }

    [TestMethod]
    public void GrantsAtLeast_AGrantNobodyAskedFor_IsCurrentRatherThanStale()
    {
        // An ACE an administrator added by hand -- a backup tool's grant, say -- is not a build that locked the
        // tree differently. Treating it as one made every service start re-walk the tree and wipe it.
        var root = Directory.CreateDirectory(Path.Combine(_temp, "root")).FullName;
        var asked = new InstallDirectoryLock.Zone(CurrentUser, [.. InstallDirectoryLock.ReadOnlyForUsers.Aces]);
        var carrying = asked with
        {
            Aces = [.. asked.Aces, InstallDirectoryLock.Allow(CurrentUser, FileSystemRights.FullControl,
                AceFlags.ObjectInherit | AceFlags.ContainerInherit)],
        };

        InstallDirectoryLock.Lock(root, carrying, _ => null);

        Assert.IsTrue(InstallDirectoryLock.GrantsAtLeast(root, asked), "more rights than asked for is still current");
        Assert.IsTrue(InstallDirectoryLock.GrantsAtLeast(root, carrying));

        InstallDirectoryLock.Lock(root, asked, _ => null);

        Assert.IsFalse(InstallDirectoryLock.GrantsAtLeast(root, carrying), "a right nobody granted has to be walked in");
        Assert.IsTrue(InstallDirectoryLock.GrantsAtLeast(root, asked));
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
