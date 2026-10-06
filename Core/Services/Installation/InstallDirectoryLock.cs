using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.Services.Installation;

/// <summary>
/// Takes a directory tree that a privileged process writes or runs from and makes it writable by SYSTEM and
/// Administrators only: owner Administrators, a protected DACL at the top, and every entry below reset to
/// what it inherits from there.
/// </summary>
/// <remarks>
/// The directories this is pointed at start out writable by ordinary users (%ProgramData% lets anyone create
/// files in a subfolder; a portable copy sits wherever it was unzipped), so anything already inside may have
/// been planted by one of them: a junction that sends a SYSTEM write somewhere else, a hard link to a file
/// elsewhere, an entry whose owner can re-grant itself access after the reset.
///
/// So the walk never trusts a path twice. Each entry is opened once, without following reparse points, and
/// everything after that goes through the handle: what the entry is, its new owner and DACL, or its removal.
/// Links in privileged zones are removed without touching their targets. Personal data keeps legitimate
/// links without traversing them. Enumeration uses the open directory handle, and each child is opened
/// relative to that same handle, so even a pre-opened directory handle cannot redirect the walk by renaming
/// a parent. SetKernelObjectSecurity avoids the automatic path-based child traversal of SetNamedSecurityInfo.
/// </remarks>
public static class InstallDirectoryLock
{
    internal static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    internal static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    internal static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private const AceFlags Inheritable = AceFlags.ObjectInherit | AceFlags.ContainerInherit;

    /// <summary>
    /// The protected DACL at the top of a locked tree, and the owner of every entry that inherits it.
    /// </summary>
    internal sealed record Zone(SecurityIdentifier Owner, IReadOnlyList<CommonAce> Aces, bool PreserveLinks = false);

    /// <summary>SYSTEM and Administrators full control, Users read and execute, applied to everything below.</summary>
    internal static Zone ReadOnlyForUsers { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, Inheritable),
        Allow(Administrators, FileSystemRights.FullControl, Inheritable),
        Allow(Users, FileSystemRights.ReadAndExecute, Inheritable),
    ]);

    /// <summary>
    /// SYSTEM and Administrators only. For the drive indexes: they list every file on the disk, including
    /// other users' profiles, which the service filters before answering a caller.
    /// </summary>
    internal static Zone PrivateToService { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, Inheritable),
        Allow(Administrators, FileSystemRights.FullControl, Inheritable),
    ]);

    /// <summary>What a lock removed (planted links) and what it could not reset; the caller logs both.</summary>
    public sealed class Report
    {
        public List<string> Removed { get; } = [];
        public List<string> Failed { get; } = [];
        public List<string> UserDataFailed { get; } = [];
    }

    /// <summary>
    /// Locks the machine-wide data directory the service writes as LocalSystem (logs, the relaunch note,
    /// machine settings, indexes). Users keep read access to all of it but the indexes. The report is for
    /// the caller to log: this runs before the service's log is opened, because opening it is one of the
    /// writes a planted link would redirect.
    /// </summary>
    public static Report LockSharedDataDirectory(string directory) =>
        LockSharedDataDirectory(directory, ReadOnlyForUsers, PrivateToService);

    public static Report PrepareSharedDataDirectory(string directory)
    {
        var report = LockSharedDataDirectory(directory);
        if (report.Failed.Count != 0) return report;
        SettingsDataDirectoryMigrator.Migrate(directory, updateUserSettings: false);
        Merge(report, LockSharedDataDirectory(directory));
        return report;
    }

    internal static Report LockSharedDataDirectory(string directory, Zone sharedZone, Zone indexZone)
    {
        // A pre-planted junction in place of the directory itself: remove the link, not what it points at
        // (Directory.Delete without recursion on a reparse point deletes only the link).
        if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            Directory.Delete(directory);
        Directory.CreateDirectory(directory);

        // LocalDriveCacheLocator.DefaultCacheDir, relative to the directory being locked.
        var indexes = Path.Combine(directory, "indexes");
        var logs = Path.Combine(directory, "logs");
        var report = Lock(directory, sharedZone,
            path => string.Equals(path, indexes, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, logs, StringComparison.OrdinalIgnoreCase) ? indexZone : null);
        // The first snapshot must inherit the private ACL too. Only create this after the parent has
        // been secured and any planted link removed; no index bytes are written before it is locked.
        foreach (var privateDirectory in new[] { indexes, logs }.Where(path => !Directory.Exists(path)))
        {
            Directory.CreateDirectory(privateDirectory);
            Merge(report, Lock(privateDirectory, indexZone, _ => null));
        }
        return report;
    }

    /// <summary>
    /// Whether <paramref name="directory"/> is locked at least as tightly as <see cref="Lock"/> would lock it
    /// for <paramref name="zone"/> now: the zone's owner, a protected DACL, and every ACE the zone grants
    /// present with those rights. For a tree locked once and re-checked on every start, so the walk runs again
    /// only when a build with different zones wrote it (5.8.2 denied Users the Synchronize bit every open asks
    /// for, which refused even starting the App: issue #316).
    /// </summary>
    /// <remarks>
    /// Rights beyond what the zone asks for count as current rather than stale, deliberately: comparing the
    /// DACL byte for byte made an ACE an administrator added by hand -- a backup tool's grant -- disagree with
    /// every build forever, so each service start re-walked the tree and quietly wiped it.
    ///
    /// ponytail: this is a drift probe, not tamper detection. It reads the one entry and never the ones below,
    /// which is sound only because a locked tree is writable by nobody but SYSTEM and Administrators, and
    /// <c>--install</c> still walks whatever it finds.
    /// </remarks>
    internal static bool GrantsAtLeast(string directory, Zone zone)
    {
        var expected = Describe(zone, isZoneRoot: true, isDirectory: true);
        var actual = new RawSecurityDescriptor(new DirectoryInfo(directory)
            .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access).GetSecurityDescriptorBinaryForm(), 0);

        if (actual.Owner != expected.Owner || !actual.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) ||
            actual.DiscretionaryAcl is not { } acl)
            return false;

        var aces = acl.OfType<CommonAce>().ToArray();
        if (aces.Any(ace => ace.AceQualifier == AceQualifier.AccessDenied))
            return false;

        const FileSystemRights writes = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (aces.Any(ace => ace.AceQualifier == AceQualifier.AccessAllowed &&
            ace.SecurityIdentifier != LocalSystem && ace.SecurityIdentifier != Administrators && ace.SecurityIdentifier != zone.Owner &&
            (ace.AccessMask & (int)writes) != 0 && !zone.Aces.Any(wanted => wanted.SecurityIdentifier == ace.SecurityIdentifier &&
                (wanted.AccessMask & ace.AccessMask) == ace.AccessMask && wanted.AceFlags == ace.AceFlags)))
            return false;

        return expected.DiscretionaryAcl!.Cast<CommonAce>().All(wanted => aces.Any(has =>
            has.AceQualifier == wanted.AceQualifier && has.SecurityIdentifier == wanted.SecurityIdentifier &&
            (has.AccessMask & wanted.AccessMask) == wanted.AccessMask &&
            (has.AceFlags & ~AceFlags.Inherited) == wanted.AceFlags));
    }

    internal static void Merge(Report into, Report from)
    {
        into.Removed.AddRange(from.Removed);
        into.Failed.AddRange(from.Failed);
        into.UserDataFailed.AddRange(from.UserDataFailed);
    }

    /// <summary>
    /// Applies <paramref name="zone"/> to <paramref name="root"/> and resets everything below it.
    /// <paramref name="zoneFor"/> may start a different zone at any descendant (it gets the full path and
    /// returns null to keep inheriting). Throws when the root itself is a link or the process lacks the
    /// rights; a descendant that cannot be fixed is reported and skipped so one bad entry does not leave the
    /// rest of the tree open.
    /// </summary>
    internal static Report Lock(string root, Zone zone, Func<string, Zone?> zoneFor)
    {
        var report = new Report();
        using var privileges = BackupRestorePrivileges.Enable();
        if (!Apply(Path.GetFullPath(root), zone, isZoneRoot: true, zoneFor, report, isTop: true))
            throw new UnauthorizedAccessException($"'{root}' is a link, not a directory; refusing to lock it.");
        return report;
    }

    /// <summary>
    /// Returns false when the entry was a link or an extra hard-link name: removed, except at the top of the
    /// walk, where the caller named it and gets to decide.
    /// </summary>
    private static bool Apply(string path, Zone zone, bool isZoneRoot, Func<string, Zone?> zoneFor, Report report,
        bool isTop = false, SafeFileHandle? parent = null)
    {
        using var handle = DirectoryLockNativeMethods.OpenWithoutFollowing(parent == null ? path : Path.GetFileName(path), parent);
        var info = DirectoryLockNativeMethods.GetInfo(handle);
        var attributes = (FileAttributes)info.dwFileAttributes;
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);

        if (attributes.HasFlag(FileAttributes.ReparsePoint) || (!isDirectory && info.nNumberOfLinks > 1))
        {
            // Personal plugin data may deliberately use links. Neither change the target's ACL nor
            // remove its name. The service never loads executable code from these user-owned zones.
            if (zone.PreserveLinks && !isZoneRoot)
                return true;
            if (isTop)
                return false;
            DirectoryLockNativeMethods.Delete(handle);
            report.Removed.Add(path);
            return false;
        }

        DirectoryLockNativeMethods.SetSecurity(handle, Describe(zone, isZoneRoot, isDirectory), isZoneRoot);
        if (!isDirectory)
            return true;

        foreach (var name in DirectoryLockNativeMethods.EnumerateNames(handle))
        {
            var child = Path.Combine(path, name);
            Zone? childZone = null;
            try
            {
                childZone = zoneFor(child);
                Apply(child, childZone ?? zone, childZone is not null, zoneFor, report, parent: handle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var failures = (childZone ?? zone).PreserveLinks ? report.UserDataFailed : report.Failed;
                failures.Add($"{child}: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// The security descriptor an entry gets: the zone's own ACEs, protected, at the top of the zone;
    /// below it, the ACEs that inheritance would hand down to a directory or a file, marked inherited.
    /// </summary>
    internal static RawSecurityDescriptor Describe(Zone zone, bool isZoneRoot, bool isDirectory)
    {
        var control = ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative | ControlFlags.DiscretionaryAclAutoInherited;
        if (isZoneRoot)
            control |= ControlFlags.DiscretionaryAclProtected;

        var acl = new RawAcl(GenericAcl.AclRevision, zone.Aces.Count);
        foreach (var ace in isZoneRoot ? zone.Aces : zone.Aces.Select(ace => Inherit(ace, zone.Owner, isDirectory)).OfType<CommonAce>())
            acl.InsertAce(acl.Count, ace);

        return new RawSecurityDescriptor(control, zone.Owner, null, null, acl);
    }

    /// <summary>
    /// What <paramref name="ace"/> becomes on a child, following the Windows inheritance rules this needs:
    /// a directory takes container-inherit ACEs (keeping their inheritance, minus inherit-only), a file takes
    /// object-inherit ones as plain effective ACEs, and CREATOR OWNER turns into the child's owner.
    /// </summary>
    private static CommonAce? Inherit(CommonAce ace, SecurityIdentifier owner, bool isDirectory)
    {
        var flags = ace.AceFlags;
        AceFlags childFlags;
        if (isDirectory && flags.HasFlag(AceFlags.ContainerInherit))
            childFlags = (flags & Inheritable) | AceFlags.Inherited;
        else if (isDirectory && flags.HasFlag(AceFlags.ObjectInherit))
            childFlags = AceFlags.ObjectInherit | AceFlags.InheritOnly | AceFlags.Inherited;
        else if (!isDirectory && flags.HasFlag(AceFlags.ObjectInherit))
            childFlags = AceFlags.Inherited;
        else
            return null;

        var sid = ace.SecurityIdentifier.IsWellKnown(WellKnownSidType.CreatorOwnerSid) ? owner : ace.SecurityIdentifier;
        return new CommonAce(childFlags, ace.AceQualifier, ace.AccessMask, sid, false, null);
    }

    /// <summary>
    /// An allow ACE for <paramref name="rights"/>, plus Synchronize. FileSystemAccessRule adds that to every
    /// allow rule by itself; a raw ACE does not, and without it the grant is useless: FileStream and directory
    /// enumeration ask for GENERIC_READ, which includes SYNCHRONIZE, so ReadAndExecute alone is refused outright
    /// (issue #316: the App could no longer read machine-settings.json).
    /// </summary>
    internal static CommonAce Allow(SecurityIdentifier sid, FileSystemRights rights, AceFlags flags) =>
        new(flags, AceQualifier.AccessAllowed, (int)(rights | FileSystemRights.Synchronize), sid, false, null);

    /// <summary>
    /// Enables SeBackup/SeRestore for the walk: they let an elevated admin or SYSTEM open an entry whose
    /// planted DACL shuts Administrators out, and set an owner other than itself. Restored on dispose.
    /// A process without them (a non-elevated test run on its own temp directory) simply goes on without.
    /// </summary>
    private sealed class BackupRestorePrivileges : IDisposable
    {
        private readonly SafeAccessTokenHandle? _token;
        private readonly byte[]? _previous;

        private BackupRestorePrivileges(SafeAccessTokenHandle? token, byte[]? previous) => (_token, _previous) = (token, previous);

        public static BackupRestorePrivileges Enable()
        {
            var token = DirectoryLockNativeMethods.EnablePrivileges(["SeBackupPrivilege", "SeRestorePrivilege"], out var previous);
            return new BackupRestorePrivileges(token, previous);
        }

        public void Dispose()
        {
            if (_token is null)
                return;
            if (_previous is not null)
                DirectoryLockNativeMethods.RestorePrivileges(_token, _previous);
            _token.Dispose();
        }
    }
}
