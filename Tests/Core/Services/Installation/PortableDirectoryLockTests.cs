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
        Assert.AreEqual(FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories, (FileSystemRights)users.AccessMask);
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

        CollectionAssert.AreEqual(new[] { Path.Combine(Users, CurrentUserIdentity.Hash(AliceSid)) }, userFolders.ToArray());
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
