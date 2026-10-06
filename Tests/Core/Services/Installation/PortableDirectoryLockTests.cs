using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

[TestClass]
public sealed class PortableDirectoryLockTests
{
    private const string App = @"D:\Tools\Lertaro";
    private const string AliceSid = "S-1-5-21-1000000000-2000000000-3000000000-1001";

    private static readonly string[] ProfileSids = [AliceSid, "S-1-5-18", "S-1-5-19"];

    private static readonly string Users = Path.Combine(App, "Data", "Users");

    [TestMethod]
    public void UsersDirectory_LetsUsersCreateAFolderThereAndNothingElse()
    {
        var aces = InstallDirectoryLockTests.Aces(
            InstallDirectoryLock.Describe(PortableDirectoryLock.UsersDirectory, isZoneRoot: true, isDirectory: true));

        var users = aces.Single(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users);
        Assert.AreEqual(FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories | FileSystemRights.Synchronize,
            (FileSystemRights)users.AccessMask);
        Assert.AreEqual(AceFlags.None, users.AceFlags, "this folder only: no rights inside anyone's own folder");

        var creatorOwner = aces.Single(ace => ace.SecurityIdentifier.IsWellKnown(WellKnownSidType.CreatorOwnerSid));
        Assert.AreEqual(FileSystemRights.FullControl, (FileSystemRights)creatorOwner.AccessMask);
        Assert.AreEqual(AceFlags.ObjectInherit | AceFlags.ContainerInherit | AceFlags.InheritOnly, creatorOwner.AceFlags);
    }

    [TestMethod]
    public void OwnedBy_GivesTheFolderToThatUserAndNobodyElse()
    {
        var alice = new SecurityIdentifier(AliceSid);

        var descriptor = InstallDirectoryLock.Describe(PortableDirectoryLock.OwnedBy(alice), isZoneRoot: true, isDirectory: true);

        Assert.AreEqual(alice, descriptor.Owner);
        Assert.IsTrue(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
        CollectionAssert.AreEquivalent(new[] { InstallDirectoryLock.LocalSystem, InstallDirectoryLock.Administrators, alice },
            InstallDirectoryLockTests.Aces(descriptor).Select(ace => ace.SecurityIdentifier).ToArray());
    }

    [TestMethod]
    public void Zones_UserFoldersAreOnlyForRealAccounts()
    {
        var (_, userFolders) = PortableDirectoryLock.Zones(App, ProfileSids);

        CollectionAssert.AreEquivalent(new[]
        {
            Path.Combine(Users, CurrentUserIdentity.Hash(AliceSid)),
            DataDirectoryResolver.ResolveUser(InstallationMode.Portable, App, @"C:\Unused", CurrentUserIdentity.Hash(AliceSid),
                portableDataDirectoryExists: true, installedDataDirectoryExists: false)
        }, userFolders.ToArray());
    }

    [TestMethod]
    public void Zones_ActualAppDataDirectoryGrantsTheUserWriteAccessToSettingsAndIndex()
    {
        var folder = DataDirectoryResolver.ResolveUser(InstallationMode.Portable, App, @"C:\Unused",
            CurrentUserIdentity.Hash(AliceSid), portableDataDirectoryExists: true, installedDataDirectoryExists: false);
        var (zoneFor, _) = PortableDirectoryLock.Zones(App, ProfileSids);
        var zone = zoneFor(folder);
        Assert.IsNotNull(zone);
        Assert.AreEqual(new SecurityIdentifier(AliceSid), zone.Owner);

        foreach (var isDirectory in new[] { true, false })
        {
            var aces = InstallDirectoryLockTests.Aces(InstallDirectoryLock.Describe(zone, false, isDirectory));
            var user = aces.Single(ace => ace.SecurityIdentifier == new SecurityIdentifier(AliceSid));
            Assert.AreEqual(FileSystemRights.FullControl, (FileSystemRights)user.AccessMask);
            Assert.IsFalse(aces.Any(ace => ace.SecurityIdentifier == InstallDirectoryLock.Users));
        }
    }

    [TestMethod]
    public void IsCurrent_CorrectRootButStaleUserDataPermissions_RequiresRepair()
    {
        var actualFolder = DataDirectoryResolver.ResolveUser(InstallationMode.Portable, App, @"C:\Unused",
            CurrentUserIdentity.Hash(AliceSid), portableDataDirectoryExists: true, installedDataDirectoryExists: false);
        var checkedFolders = new List<string>();
        var current = PortableDirectoryLock.IsCurrent(App, true, ProfileSids, (folder, zone) =>
        {
            checkedFolders.Add(folder);
            return folder != actualFolder;
        });

        Assert.IsFalse(current);
        Assert.Contains(App, checkedFolders);
        Assert.Contains(actualFolder, checkedFolders);
    }

    [TestMethod]
    public void IsCurrent_AllZonesRepaired_AcceptsCurrentPermissions()
    {
        var checkedFolders = new List<string>();
        Assert.IsTrue(PortableDirectoryLock.IsCurrent(App, true, ProfileSids, (folder, _) =>
        {
            checkedFolders.Add(folder);
            return true;
        }));
        Assert.HasCount(4, checkedFolders); // binaries, Users, single-hash and double-hash account folders
    }

    [TestMethod]
    public void IsCurrent_ExternalData_OnlyChecksBinaries()
    {
        var checkedFolders = new List<string>();
        Assert.IsTrue(PortableDirectoryLock.IsCurrent(App, false, ProfileSids, (folder, _) =>
        {
            checkedFolders.Add(folder);
            return true;
        }));
        Assert.AreEqual(App, Assert.ContainsSingle(checkedFolders));
    }

    [TestMethod]
    public void Lock_CorrectedUserZone_RestoresWritesWithoutLosingExistingData()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var root = Path.Combine(Path.GetTempPath(), $"LertaroUserLock_{Guid.NewGuid():N}");
        var folder = DataDirectoryResolver.ResolveUser(InstallationMode.Portable, root, @"C:\Unused",
            CurrentUserIdentity.Hash(user.Value), portableDataDirectoryExists: true, installedDataDirectoryExists: false);
        var indexFolder = Directory.CreateDirectory(Path.Combine(folder, "ContentIndex")).FullName;
        var settings = Path.Combine(folder, "user-settings.json");
        var index = Path.Combine(indexFolder, "content_index.db");
        File.WriteAllText(settings, "existing settings");
        File.WriteAllText(index, "existing index");
        var (zoneFor, _) = PortableDirectoryLock.Zones(root, [user.Value]);
        var zone = zoneFor(folder);
        Assert.IsNotNull(zone);

        try
        {
            // Keep ACL-management rights so the repair runs without elevation, but deny data writes.
            var stale = new InstallDirectoryLock.Zone(user,
                [InstallDirectoryLock.Allow(user, FileSystemRights.ReadAndExecute | FileSystemRights.ChangePermissions |
                    FileSystemRights.TakeOwnership | FileSystemRights.Delete,
                    AceFlags.ObjectInherit | AceFlags.ContainerInherit)]);
            InstallDirectoryLock.Lock(folder, stale, _ => null);
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(folder, "history.tmp"), "history"));

            var report = InstallDirectoryLock.Lock(folder, zone, _ => null);

            Assert.IsEmpty(report.Failed);
            Assert.IsEmpty(report.Removed);
            Assert.AreEqual("existing settings", File.ReadAllText(settings));
            Assert.AreEqual("existing index", File.ReadAllText(index));
            File.WriteAllText(Path.Combine(folder, "history.tmp"), "history");
            File.AppendAllText(index, " updated");
            Assert.AreEqual("existing index updated", File.ReadAllText(index));
        }
        finally
        {
            var restore = new DirectorySecurity();
            restore.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(folder).SetAccessControl(restore);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Zones_AnAccountsFolderGoesToThatAccount()
    {
        var (zoneFor, _) = PortableDirectoryLock.Zones(App, ProfileSids);

        var zone = zoneFor(Path.Combine(Users, CurrentUserIdentity.Hash(AliceSid)));

        Assert.AreEqual(new SecurityIdentifier(AliceSid), zone?.Owner);
    }

    [TestMethod]
    public void Zones_AFolderForNoKnownAccount_GoesToAdministrators()
    {
        var (zoneFor, _) = PortableDirectoryLock.Zones(App, ProfileSids);

        Assert.AreSame(InstallDirectoryLock.PrivateToService, zoneFor(Path.Combine(Users, "0123456789abcdef")));
    }

    [TestMethod]
    public void Zones_TheRestOfTheLayout()
    {
        var (zoneFor, _) = PortableDirectoryLock.Zones(App, ProfileSids);

        Assert.AreSame(PortableDirectoryLock.UsersDirectory, zoneFor(Users));
        Assert.AreSame(InstallDirectoryLock.PrivateToService, zoneFor(Path.Combine(App, "Data", "Machine", "indexes")));
        // Everything else inherits the read-only top: the binaries, the plugins, Data\Machine itself, and
        // whatever sits inside a user's own folder.
        Assert.IsNull(zoneFor(Path.Combine(App, "Lertaro.Service.exe")));
        Assert.IsNull(zoneFor(Path.Combine(App, "Plugins")));
        Assert.IsNull(zoneFor(Path.Combine(App, "Data", "Machine")));
        Assert.IsNull(zoneFor(Path.Combine(Users, CurrentUserIdentity.Hash(AliceSid), "user-settings.json")));
    }
}
