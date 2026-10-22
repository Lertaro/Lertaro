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

    // Every root a repair has to cover: the installed copy keeps its data under the profile, a portable one
    // keeps it beside the executable, and a portable one without a Data folder still reuses the installed data.
    [TestMethod]
    [DataRow(InstallationMode.Installed, false, 1)]
    [DataRow(InstallationMode.Portable, true, 2)]
    [DataRow(InstallationMode.Portable, false, 1)]
    public void RepairTargets_CoverTheRootThisCopyUsesAndTheProfileDefault(InstallationMode mode, bool ownData, int expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroRepairTargets_{Guid.NewGuid():N}");
        var app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var local = Directory.CreateDirectory(Path.Combine(profile, "AppData", "Local")).FullName;
        Directory.CreateDirectory(Path.Combine(local, "Lertaro"));
        if (ownData) Directory.CreateDirectory(Path.Combine(app, "Data"));
        try
        {
            var targets = PortableDirectoryLock.RepairTargets(mode, app, local, profile, AliceSid);

            // The root the App actually reads comes first whenever it is not the profile default already: a
            // portable copy keeps its user data beside its executable, so a repair that only knew the profile
            // default walked a folder nothing reads and left the one that matters untouched. The profile default
            // is always last, listed once, never the same tree twice under two names.
            Assert.HasCount(expected, targets);
            Assert.AreEqual(Path.Combine(profile, "AppData", "Local", "Lertaro"), targets[^1]);
            if (expected == 2)
            {
                Assert.AreEqual(DataDirectoryResolver.ResolveUser(InstallationMode.Portable, app, local, AliceSid), targets[0]);
                StringAssert.Contains(targets[0], Path.Combine("Data", "Users"));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Which of the two roots is repaired, and which branch each one takes: the profile default has to pin the
    // profile's own chain first, so it is never treated as a plain root, while the portable root is repaired
    // where it sits because it is not under the profile at all.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RepairUserDataDirectory_RepairsOnlyARootThatIsThere(bool profileDefault)
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroRepairBranch_{Guid.NewGuid():N}");
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        Directory.CreateDirectory(Path.Combine(profile, "AppData", "Local"));
        var portable = Directory.CreateDirectory(
            Path.Combine(root, "app", "Data", "Users", CurrentUserIdentity.Hash(AliceSid))).FullName;
        var directory = profileDefault ? Path.Combine(profile, "AppData", "Local", "Lertaro") : portable;
        try
        {
            var report = PortableDirectoryLock.RepairUserDataDirectory(
                directory, profile, WindowsIdentity.GetCurrent().User!);

            Assert.IsEmpty(report.Failed);
            if (profileDefault)
                Assert.IsFalse(Directory.Exists(directory), "nothing to repair means nothing is created either");
            else
                Assert.AreEqual(WindowsIdentity.GetCurrent().User!.Value, UserDataAccess.TryGetOwner(directory));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The trust boundary is the directory's ACL and owner. A personal file another process is holding open is
    // not: elevation cannot open it either, and treating it as a failed repair is what turned one busy settings
    // file into an App that could not start at all.
    // The marker a lock run leaves behind is the only record of what happened to a walk that never finished: the
    // SCM kills a start at its timeout, so "started and was cut short" has to look different from "nobody ever
    // locked this copy", and neither may count as current.
    [TestMethod]
    public void IsInterrupted_APendingMarkerIsReportedAndNeverCountsAsCurrent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroRevision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, ".permissions-version");
        try
        {
            Assert.IsFalse(PortableDirectoryLock.IsInterrupted(root), "no marker is a copy that was never locked");

            File.WriteAllText(marker, "pending");
            Assert.IsTrue(PortableDirectoryLock.IsInterrupted(root));
            Assert.IsFalse(PortableDirectoryLock.IsCurrent(root), "an interrupted walk must be walked again");

            File.WriteAllText(marker, "2");
            Assert.IsFalse(PortableDirectoryLock.IsInterrupted(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A completed walk is what makes the next start skip the tree entirely, so the marker it writes has to be the
    // revision IsCurrent accepts -- not the pending one Lock starts with.
    [TestMethod]
    public void Lock_CompletingTheWalk_LeavesTheCurrentRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroLockRevision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // The shipped root zone gives the folder to SYSTEM and Administrators, and assigning that owner
            // needs elevation. Locking it to the account running the test exercises the same walk.
            var report = PortableDirectoryLock.Lock(root,
                PortableDirectoryLock.OwnedBy(WindowsIdentity.GetCurrent().User!));

            Assert.IsEmpty(report.Failed);
            Assert.AreEqual("2", File.ReadAllText(Path.Combine(root, ".permissions-version")).Trim());
            Assert.IsFalse(PortableDirectoryLock.IsInterrupted(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FatalReason_ABusyPersonalFileIsNotARepairFailure()
    {
        var report = new InstallDirectoryLock.Report();
        report.UserDataFailed.Add(@"C:\Data\Users\0123456789abcdef\plugin.json: the file is in use");
        Assert.IsNull(PortableDirectoryLock.FatalReason(report));

        report.Failed.Add(@"C:\Data\Users\0123456789abcdef: the ACL could not be set");
        Assert.AreEqual(@"C:\Data\Users\0123456789abcdef: the ACL could not be set", PortableDirectoryLock.FatalReason(report));
    }

    [TestMethod]
    public void RepairProfileUserDirectory_ReachesAProfileTheShellHoldsOpenWithoutSharingDelete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LertaroProfileRepair_{Guid.NewGuid():N}");
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        Directory.CreateDirectory(Path.Combine(profile, "AppData", "Local", "Lertaro"));
        var live = DirectoryLockNativeMethodsTests.HoldOpenWithoutSharingDelete(profile);
        try
        {
            // The repair used to ask for DELETE on this profile, which the held handle above refuses; the
            // elevated repair is also the only thing that can unblock a start, so the App never started.
            Assert.IsFalse(live.IsInvalid, "the test needs the live-profile handle to reproduce the refusal");
            var report = PortableDirectoryLock.RepairProfileUserDirectory(profile, WindowsIdentity.GetCurrent().User!);

            Assert.IsEmpty(report.Failed);
            Assert.IsEmpty(report.UserDataFailed);
        }
        finally
        {
            live.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
