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
    // Bump only when a release requires a full walk, including protected descendants whose root
    // already has the right ACL. Lives in the administrator-owned binary directory, never user data.
    private const string PermissionsRevision = "2";
    private const string RevisionFile = ".permissions-version";
    /// <summary>
    /// Locks <paramref name="directory"/> to <paramref name="user"/> alone, and checks that the owner it claims
    /// to have set is the owner that is actually there.
    /// </summary>
    public static Report RepairUserDirectory(string directory, SecurityIdentifier user)
    {
        Directory.CreateDirectory(directory);
        var report = InstallDirectoryLock.Lock(directory, OwnedBy(user), _ => null);

        // A repair that returns without having changed the owner is the failure issue #342 could not see: the
        // walk reported success, every later start refused again, and the user had nothing to go on. The owner
        // is read back here, while the walk's own result is still in hand, and a mismatch is reported rather
        // than assumed away -- the App turns a non-empty Failed list into the reason it shows.
        var owner = UserDataAccess.TryGetOwner(directory);
        if (owner != null && !string.Equals(owner, user.Value, StringComparison.OrdinalIgnoreCase))
            report.Failed.Add($"{directory}: owner is still {owner}, expected {user.Value}");
        return report;
    }

    /// <summary>
    /// The data roots an elevated repair has to cover, in the order it should try them: the one this copy
    /// actually uses, then the profile default.
    /// </summary>
    /// <remarks>
    /// Only the profile default used to be repaired, which is where an installed copy keeps its data and not
    /// where a portable one does: a portable copy's user data sits beside its executable, so its repair walked
    /// a folder the App never reads and left the one it does read exactly as it was. Both roots are derived
    /// here from this process's own location and the profile map -- never from a path the caller handed in --
    /// so no low-integrity process can steer elevation at a directory of its choosing.
    /// </remarks>
    public static IReadOnlyList<string> RepairTargets(InstallationMode mode, string applicationDirectory,
        string localApplicationDataDirectory, string profileDirectory, string userSid)
    {
        var profileDefault = Path.Combine(profileDirectory, "AppData", "Local", "Lertaro");
        var used = DataDirectoryResolver.ResolveUser(mode, applicationDirectory, localApplicationDataDirectory, userSid);
        return SamePath(used, profileDefault) ? [profileDefault] : [used, profileDefault];
    }

    /// <summary>
    /// Repairs one of <see cref="RepairTargets"/>: the root the App is about to use, wherever this copy keeps
    /// it. The profile default is the one of them whose ancestors have to be pinned first, because that is the
    /// user-writable chain a planted junction could redirect elevation through.
    /// </summary>
    public static Report RepairUserDataDirectory(string directory, string profile, SecurityIdentifier user) =>
        SamePath(directory, Path.Combine(profile, "AppData", "Local", "Lertaro"))
            ? RepairProfileUserDirectory(profile, user)
            : RepairUserDirectory(directory, user);

    /// <summary>
    /// What an elevated repair has to report as a failure, or null when it may be treated as done.
    /// </summary>
    /// <remarks>
    /// The trust boundary is <see cref="Report.Failed"/>: a directory or file whose ACL or owner the walk could
    /// not set. A personal file that was in use is reported under <see cref="Report.UserDataFailed"/> instead,
    /// and deliberately does not fail the repair -- elevation cannot open a file another process is holding, and
    /// one busy settings file must not be the reason an App cannot start. The App re-checks the directory itself
    /// right after the repair, which is the verdict that actually decides whether it can run.
    /// </remarks>
    public static string? FatalReason(Report report) =>
        report.Failed.Count == 0 ? null : string.Join(Environment.NewLine, report.Failed);

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    public static Report RepairProfileUserDirectory(string profile, SecurityIdentifier user)
    {
        // ProfileList supplies the trusted root. Pin each user-writable ancestor and refuse junctions
        // before resolving the final Lertaro folder; elevation must not repair an arbitrary link target.
        // Pinned, not migrated: these are live directories the shell already holds open without sharing
        // delete, and nothing here is deleted or re-ACLed through these handles.
        using var root = DirectoryLockNativeMethods.OpenWithoutFollowing(profile, pin: true);
        RejectLink(root);
        using var appData = DirectoryLockNativeMethods.OpenWithoutFollowing("AppData", root, pin: true);
        RejectLink(appData);
        using var local = DirectoryLockNativeMethods.OpenWithoutFollowing("Local", appData, pin: true);
        RejectLink(local);
        var directory = Path.Combine(profile, "AppData", "Local", "Lertaro");
        if (!Directory.Exists(directory)) return new Report();
        return RepairUserDirectory(directory, user);

        static void RejectLink(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            if (((FileAttributes)DirectoryLockNativeMethods.GetInfo(handle).dwFileAttributes).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("The user profile data path contains a link; automatic elevated repair is refused.");
        }
    }

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
    ], PreserveLinks: true);

    public static Report Lock(string appDirectory)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        var (zoneFor, userFolders) = Zones(appDirectory, UserProfiles.Read().Keys);

        // A failed walk must not leave a previous success marker suppressing the next repair.
        File.Delete(Path.Combine(appDirectory, RevisionFile));
        var report = InstallDirectoryLock.Lock(appDirectory, ReadOnlyForUsers, zoneFor);
        if (!Directory.Exists(Path.Combine(appDirectory, "Data")))
        {
            if (report.Failed.Count == 0 && report.UserDataFailed.Count == 0) AtomicFileStore.Write(Path.Combine(appDirectory, RevisionFile), PermissionsRevision);
            return report;
        }

        // Created only now, inside a tree nobody else can write any more, so no link can be waiting on the
        // path. Each new folder is then given its own zone.
        foreach (var folder in userFolders.Prepend(Path.Combine(appDirectory, "Data", "Users")).Where(folder => !Directory.Exists(folder)))
        {
            Directory.CreateDirectory(folder);
            Merge(report, InstallDirectoryLock.Lock(folder, zoneFor(folder)!, zoneFor));
        }

        if (report.Failed.Count == 0 && report.UserDataFailed.Count == 0) AtomicFileStore.Write(Path.Combine(appDirectory, RevisionFile), PermissionsRevision);
        return report;
    }

    /// <summary>
    /// Whether the copy at <paramref name="appDirectory"/> is still locked the way <see cref="Lock"/> does it
    /// now. False for a copy installed before the lock existed, or locked by a build whose zones were wrong
    /// (5.8.2 granted Users read without SYNCHRONIZE, which refused even starting the App: issue #316). The
    /// service applies updates in place, without running --install again, so it checks this on every start.
    /// </summary>
    public static bool IsCurrent(string appDirectory)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (SettingsFileReader.ReadIfPresent(Path.Combine(appDirectory, RevisionFile)) != PermissionsRevision) return false;
        return IsCurrent(appDirectory, Directory.Exists(Path.Combine(appDirectory, "Data")),
            UserProfiles.Read().Keys, (folder, zone) => Directory.Exists(folder) && GrantsAtLeast(folder, zone));
    }

    internal static bool IsCurrent(string appDirectory, bool hasPortableData, IEnumerable<string> profileSids,
        Func<string, Zone, bool> grantsAtLeast)
    {
        if (!grantsAtLeast(appDirectory, ReadOnlyForUsers))
            return false;
        if (!hasPortableData)
            return true;

        // #327: a correct binary-directory ACL says nothing about the separately protected user zones.
        var (zoneFor, userFolders) = Zones(appDirectory, profileSids);
        return userFolders.Prepend(Path.Combine(appDirectory, "Data", "Users"))
            .All(folder => grantsAtLeast(folder, zoneFor(folder)!));
    }

    /// <summary>
    /// Which zone starts where in a portable copy at <paramref name="appDirectory"/> (null: inherit), and the
    /// per-user folders that belong to the accounts in <paramref name="profileSids"/>.
    /// </summary>
    internal static (Func<string, Zone?> ZoneFor, IReadOnlyList<string> UserFolders) Zones(string appDirectory, IEnumerable<string> profileSids)
    {
        var users = Path.Combine(appDirectory, "Data", "Users");
        var indexes = Path.Combine(appDirectory, "Data", "Machine", "indexes");
        var logs = Path.Combine(appDirectory, "Data", "Machine", "logs");
        var legacyLog = Path.Combine(appDirectory, "service.log");
        var userFolders = profileSids
            .Where(UserProfiles.IsAccount)
            // Logger passes SidHash to ResolveUser, which hashes it again. Preserve existing data paths
            // and recognize both names; otherwise the live folder is locked as an unknown account (#327).
            .SelectMany(sid => new[] { CurrentUserIdentity.Hash(sid), CurrentUserIdentity.Hash(CurrentUserIdentity.Hash(sid)) }
                .Select(hash => (Path: Path.Combine(users, hash), Sid: sid)))
            .ToDictionary(user => user.Path, user => OwnedBy(new SecurityIdentifier(user.Sid)),
                StringComparer.OrdinalIgnoreCase);

        Zone? ZoneFor(string path) =>
            string.Equals(path, users, StringComparison.OrdinalIgnoreCase) ? UsersDirectory
            : string.Equals(Path.GetDirectoryName(path), users, StringComparison.OrdinalIgnoreCase)
                ? userFolders.GetValueOrDefault(path) ?? PrivateToService
            : string.Equals(path, indexes, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, logs, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, legacyLog, StringComparison.OrdinalIgnoreCase) ? PrivateToService
            : null;

        return (ZoneFor, userFolders.Keys.ToList());
    }
}
