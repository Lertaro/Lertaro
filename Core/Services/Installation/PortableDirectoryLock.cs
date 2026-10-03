using System.Security.AccessControl;
using System.Security.Principal;

using static Lertaro.Core.Services.Installation.InstallDirectoryLock;

namespace Lertaro.Core.Services.Installation;

/// <summary>
/// Locks a portable copy's folder before the LocalSystem service is pointed at it. It was unzipped wherever
/// its user chose, usually somewhere every user can write, and the service would otherwise load whatever
/// executable or plugin DLL anyone put there.
/// </summary>
/// <remarks>
/// Split out of <see cref="InstallDirectoryLock"/>, which holds the walk itself; this is only the layout of a
/// portable copy. Everything becomes read-only for Users except the portable per-user data. <c>Data\Machine</c>
/// is the service's own. Under <c>Data\Users</c> each existing <c>&lt;SID hash&gt;</c> folder is handed back to
/// the account whose SID it hashes (found through ProfileList) and closed to everyone else; a folder whose
/// account is not on this machine goes to Administrators. A folder is also created now for every account that
/// has a profile, so another user cannot create it first and own that account's settings and plugins.
/// ponytail: an account whose profile appears after this ran is still open to that; the upgrade is the App
/// refusing a data folder it does not own.
///
/// The per-user part only applies when the copy keeps its data beside itself (a Data folder exists, which
/// resolving the service's own data directory has created by now if that is where it lives). A copy that fell
/// back to %ProgramData% and %LocalAppData% is left to keep doing so.
/// </remarks>
public static class PortableDirectoryLock
{
    /// <summary>
    /// A portable copy's <c>Data\Users</c>: every user may create a folder here (only here, not below) and
    /// owns what it creates, through CREATOR OWNER. Nobody gets into anyone else's.
    /// </summary>
    internal static Zone UsersDirectory { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Administrators, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Users, FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories, AceFlags.None),
        Allow(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), FileSystemRights.FullControl,
            AceFlags.ObjectInherit | AceFlags.ContainerInherit | AceFlags.InheritOnly),
    ]);

    /// <summary>One user's own data folder: that user, SYSTEM and Administrators, owned by the user.</summary>
    internal static Zone OwnedBy(SecurityIdentifier user) => new(user,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Administrators, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(user, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
    ]);

    public static Report Lock(string appDirectory)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        var (zoneFor, userFolders) = Zones(appDirectory, UserProfiles.Read().Keys);

        var report = InstallDirectoryLock.Lock(appDirectory, ReadOnlyForUsers, zoneFor);
        if (!Directory.Exists(Path.Combine(appDirectory, "Data")))
            return report;

        // Created only now, inside a tree nobody else can write any more, so no link can be waiting on the
        // path. Each new folder is then given its own zone.
        foreach (var folder in userFolders.Prepend(Path.Combine(appDirectory, "Data", "Users")).Where(folder => !Directory.Exists(folder)))
        {
            Directory.CreateDirectory(folder);
            Merge(report, InstallDirectoryLock.Lock(folder, zoneFor(folder)!, zoneFor));
        }

        return report;
    }

    /// <summary>
    /// Which zone starts where in a portable copy at <paramref name="appDirectory"/> (null: inherit), and the
    /// per-user folders that belong to the accounts in <paramref name="profileSids"/>.
    /// </summary>
    internal static (Func<string, Zone?> ZoneFor, IReadOnlyList<string> UserFolders) Zones(string appDirectory, IEnumerable<string> profileSids)
    {
        var users = Path.Combine(appDirectory, "Data", "Users");
        var indexes = Path.Combine(appDirectory, "Data", "Machine", "indexes");
        var userFolders = profileSids
            .Where(UserProfiles.IsAccount)
            .ToDictionary(sid => Path.Combine(users, CurrentUserIdentity.Hash(sid)), sid => OwnedBy(new SecurityIdentifier(sid)),
                StringComparer.OrdinalIgnoreCase);

        Zone? ZoneFor(string path) =>
            string.Equals(path, users, StringComparison.OrdinalIgnoreCase) ? UsersDirectory
            : string.Equals(Path.GetDirectoryName(path), users, StringComparison.OrdinalIgnoreCase)
                ? userFolders.GetValueOrDefault(path) ?? PrivateToService
            : string.Equals(path, indexes, StringComparison.OrdinalIgnoreCase) ? PrivateToService
            : null;

        return (ZoneFor, userFolders.Keys.ToList());
    }
}
